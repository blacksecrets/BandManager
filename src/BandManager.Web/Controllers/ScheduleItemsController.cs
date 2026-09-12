using System.Text.Json;
using System.Text.RegularExpressions;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Publishers;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record FartRequest(string? ImageUrl);
public record SetAssigneesRequest(Guid? AssigneeUserId1, Guid? AssigneeUserId2);

/// <summary>
/// The Dashboard board - ported from the old app's routes/api.js GET
/// /items + serializeItem, scoped to the active Band. Field names are
/// snake_case to match wwwroot/assets/dashboard.js's existing expectations.
///
/// gig/media/gallery cross-referencing (SerializeAsync below) reads live
/// from the Band's own site via GigsSource/MediaSource/GallerySource - a
/// 60s in-process cache per Band, so serializing a whole list of items
/// only actually re-fetches the site's calendar.js/media.js/gallery.js
/// once per unique ref-bearing item type, not once per item. gig_issues
/// (a "what's still missing on the site itself" checklist) isn't ported -
/// a separate, smaller gap than the cross-referencing itself.
///
/// Publishing is wired for Facebook, Instagram, Google Business Profile,
/// and no-api ("mark posted yourself") items - Facebook Cover Photo
/// (needs Catalog + image generation) and TikTok/YouTube/Bandsintown/
/// Spotify (no posting API exists for any of them, same as the old app)
/// aren't wired.
/// </summary>
[ApiController]
[Route("/api/items")]
[Authorize(Policy = "BandMember")]
public partial class ScheduleItemsController(
    ApplicationDbContext db,
    Scheduler scheduler,
    IActiveBandAccessor activeBand,
    IWebHostEnvironment env,
    FacebookPublisher facebookPublisher,
    InstagramPublisher instagramPublisher,
    GoogleBusinessPublisher googleBusinessPublisher,
    CatalogStore catalogStore) : ControllerBase
{
    [GeneratedRegex(@"^cadence-([0-9a-fA-F-]{36})")]
    private static partial Regex CadenceRuleIdInTemplateKey();

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null)
        {
            bandId = default;
            return BadRequest(new { error = "No active band selected." });
        }
        bandId = id.Value;
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var items = await db.ScheduleItems
            .Include(i => i.Artifacts)
            .Where(i => i.BandId == bandId && !i.IsArchived)
            .OrderBy(i => i.DueDate == null)
            .ThenBy(i => i.DueDate)
            .ToListAsync();

        var contentTypes = await db.ContentTypes.ToDictionaryAsync(c => c.Id);
        var accounts = await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id);

        var results = new List<object>();
        foreach (var item in items) results.Add(await SerializeAsync(item, contentTypes, accounts));
        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var item = await db.ScheduleItems.Include(i => i.Artifacts)
            .FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound();

        var contentTypes = await db.ContentTypes.ToDictionaryAsync(c => c.Id);
        var accounts = await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id);
        return Ok(await SerializeAsync(item, contentTypes, accounts));
    }

    /// <summary>Reassigns one tile - any band member can do this (not
    /// BandAdmin-gated, matching the collaborative "anyone can pick up or
    /// hand off a task" spirit of the board). Notifies whoever is newly
    /// added (not someone who was already assigned, and not someone being
    /// removed) - Phase 6's NotificationPreference will gate whether that
    /// notification actually gets emailed; for now it always lands
    /// in-app.</summary>
    [HttpPut("{id:guid}/assignees")]
    public async Task<IActionResult> SetAssignees(Guid id, [FromBody] SetAssigneesRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (request.AssigneeUserId1 is not null && request.AssigneeUserId1 == request.AssigneeUserId2)
            return BadRequest(new { error = "Can't assign the same person twice." });

        var item = await db.ScheduleItems.FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound(new { error = "Not found" });

        var newIds = new[] { request.AssigneeUserId1, request.AssigneeUserId2 }.Where(i => i is not null).Select(i => i!.Value).ToList();
        foreach (var newId in newIds)
        {
            if (!await db.BandMemberships.AnyAsync(m => m.UserId == newId && m.BandId == bandId))
                return BadRequest(new { error = "Assignee must be a member of this band." });
        }

        var previousIds = new[] { item.AssigneeUserId1, item.AssigneeUserId2 }.Where(i => i is not null).Select(i => i!.Value).ToHashSet();
        item.AssigneeUserId1 = request.AssigneeUserId1;
        item.AssigneeUserId2 = request.AssigneeUserId2;
        await db.SaveChangesAsync();

        foreach (var newId in newIds.Where(i => !previousIds.Contains(i)))
        {
            db.Notifications.Add(new Notification
            {
                UserId = newId,
                Message = $"You've been assigned to \"{item.ContentType} - {item.Category}\".",
                Kind = NotificationKind.ResponsibilityChanged,
                BandId = item.BandId
            });
        }
        await db.SaveChangesAsync();

        return Ok(new { ok = true, assignee_user_id1 = item.AssigneeUserId1, assignee_user_id2 = item.AssigneeUserId2 });
    }

    /// <summary>Manually triggers full cadence generation (recurring +
    /// gig-driven + media/gallery) for the active Band - idempotent (see
    /// Scheduler), safe to call as often as wanted. The background
    /// service already does this every 30 minutes; this endpoint is what
    /// makes a run independently triggerable/testable right now.</summary>
    [HttpPost("generate-recurring")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> GenerateRecurring()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.FindAsync(bandId);
        if (band is null) return NotFound();
        await scheduler.GenerateAllAsync(bandId, band);
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.ScheduleItems.Include(i => i.Artifacts).FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound();

        item.Status = ScheduleItemStatus.Cancelled;
        item.PostedAt = DateTime.UtcNow;
        item.PostedVia = "manual-cancelled";
        item.ArtifactsOwed = false;
        await db.SaveChangesAsync();
        return Ok(await SerializeAsync(item, await db.ContentTypes.ToDictionaryAsync(c => c.Id), await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id)));
    }

    private static readonly Dictionary<string, string> MimePrefixForType = new()
    {
        ["photo"] = "image/",
        ["flyer"] = "image/",
        ["video"] = "video/",
        ["audio"] = "audio/"
    };
    private static readonly HashSet<string> AllArtifactTypes = ["caption", "photo", "video", "flyer", "audio", "event_url"];
    private const long MaxUploadBytes = 200 * 1024 * 1024; // 200MB - generous for a phone video clip

    /// <summary>Upload a required artifact. For file artifacts (photo/
    /// video/flyer/audio), send multipart/form-data with fields
    /// `artifactType` and one of `file` / `catalogItemId` / `url` (see
    /// CatalogStore.ResolveMediaInputAsync - same three-mode contract as
    /// every other upload route). For text artifacts (caption, event_url),
    /// send JSON { artifactType, text }.</summary>
    [HttpPost("{id:guid}/upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.ScheduleItems.Include(i => i.Artifacts).FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound();

        string? artifactType;
        string? text = null;
        IFormFile? file = null;
        string? catalogItemId = null;
        string? url = null;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            artifactType = form["artifactType"];
            file = form.Files.GetFile("file");
            catalogItemId = form["catalogItemId"];
            url = form["url"];
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            artifactType = json.TryGetProperty("artifactType", out var at) ? at.GetString() : null;
            text = json.TryGetProperty("text", out var tx) ? tx.GetString() : null;
        }

        if (string.IsNullOrEmpty(artifactType) || !AllArtifactTypes.Contains(artifactType))
            return BadRequest(new { error = "A valid artifactType is required" });

        var username = User.Identity?.Name ?? "unknown";

        // Every other content type treats a re-upload as "replace" (one
        // photo, swap it out) - Photo Album is the one deliberate
        // exception, where uploading another "photo" artifact means
        // "add another photo to the album," capped at 10 to match both
        // Facebook's and Instagram's own album/carousel limits. Photo
        // Album's caption still replaces normally - only its photos
        // accumulate.
        var isAlbumPhoto = artifactType == "photo" && item.ContentType == "Photo Album";
        if (isAlbumPhoto && item.Artifacts.Count(a => a.ArtifactType == "photo") >= 10)
            return BadRequest(new { error = "A Photo Album can only have up to 10 photos." });

        if (MimePrefixForType.TryGetValue(artifactType, out var mimePrefix))
        {
            // Routes file/catalogItemId/url through the same shared,
            // safe entry point every other upload path in the app uses
            // (see CatalogStore.ResolveMediaInputAsync) - previously this
            // only ever read "file", silently ignoring a "Pick from
            // Catalog" or "Upload from URL" pick made via the shared
            // buildMediaSlotControl widget every Dashboard tile already
            // renders those two extra options for.
            var expectedMediaType = mimePrefix switch { "image/" => MediaType.Image, "video/" => MediaType.Video, _ => MediaType.Audio };
            ResolvedMedia? resolved;
            try
            {
                resolved = await catalogStore.ResolveMediaInputAsync(
                    bandId, await file.ToUploadedFilePayloadAsync(), catalogItemId, url, expectedMediaType, username, MaxUploadBytes);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            if (resolved is null) return BadRequest(new { error = "Provide a file, a Catalog pick, or a URL" });

            var ext = resolved.Ext;
            if (string.IsNullOrEmpty(ext)) ext = mimePrefix switch { "image/" => "jpg", "video/" => "mp4", _ => "mp3" };

            var dir = Path.Combine(env.ContentRootPath, "data", "uploads", item.Id.ToString());
            Directory.CreateDirectory(dir);
            var fileName = $"{artifactType}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{ext}";
            var destPath = Path.Combine(dir, fileName);
            await System.IO.File.WriteAllBytesAsync(destPath, resolved.Buffer);

            if (!isAlbumPhoto) await ClearExistingArtifactAsync(item.Id, artifactType);
            db.Artifacts.Add(new Artifact
            {
                ScheduleItemId = item.Id,
                ArtifactType = artifactType,
                // Relative to ContentRootPath, deliberately kept "data/..."
                // (forward slashes) since wwwroot/assets/dashboard.js's
                // artifactUrl() strips a leading "data/" and prefixes "/"
                // itself - matches the old app's own path convention exactly.
                FilePath = $"data/uploads/{item.Id}/{fileName}",
                UploadedBy = username
            });
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim();
            if (trimmed.Length > 5000) trimmed = trimmed[..5000];
            await ClearExistingArtifactAsync(item.Id, artifactType);
            db.Artifacts.Add(new Artifact { ScheduleItemId = item.Id, ArtifactType = artifactType, TextValue = trimmed, UploadedBy = username });
        }
        else
        {
            return BadRequest(new { error = "Provide a file or text" });
        }

        await db.SaveChangesAsync();

        // If this closes out a manual backfill, clear the "owed" flag.
        var contentType = await db.ContentTypes.FindAsync(item.ContentType);
        var required = contentType?.RequiredArtifacts ?? [];
        var complete = required.All(t => item.Artifacts.Any(a => a.ArtifactType == t));
        if (item.ArtifactsOwed && complete)
        {
            item.ArtifactsOwed = false;
            await db.SaveChangesAsync();
        }

        return Ok(await SerializeAsync(item, await db.ContentTypes.ToDictionaryAsync(c => c.Id), await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id)));
    }

    [HttpDelete("{id:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> DeleteArtifact(Guid id, Guid artifactId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.ScheduleItems.Include(i => i.Artifacts).FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound();

        var artifact = item.Artifacts.FirstOrDefault(a => a.Id == artifactId);
        if (artifact is null) return NotFound(new { error = "Not found" });

        if (artifact.FilePath is not null)
        {
            try { System.IO.File.Delete(Path.Combine(env.ContentRootPath, artifact.FilePath)); } catch { /* best-effort */ }
        }
        db.Artifacts.Remove(artifact);
        await db.SaveChangesAsync();

        return Ok(await SerializeAsync(item, await db.ContentTypes.ToDictionaryAsync(c => c.Id), await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id)));
    }

    /// <summary>"Fart it out" - only allowed once required artifacts are
    /// present. Ported from the old app's POST /items/:id/fart. Facebook
    /// Cover Photo items and Google Business Profile aren't wired yet -
    /// see the class doc comment. imageUrl is optional and only meaningful
    /// for Instagram right now (see InstagramPublisher's doc comment on
    /// why that path isn't actually reachable from the current frontend
    /// yet either).</summary>
    [HttpPost("{id:guid}/fart")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Fart(Guid id, [FromBody] FartRequest? body)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.ScheduleItems.Include(i => i.Artifacts).FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound();

        var contentType = await db.ContentTypes.FindAsync(item.ContentType);
        var required = contentType?.RequiredArtifacts ?? [];
        var presentTypes = item.Artifacts.Select(a => a.ArtifactType).ToHashSet();
        if (!required.All(presentTypes.Contains))
            return BadRequest(new { error = "Not all required artifacts are uploaded yet." });

        var caption = item.Artifacts.FirstOrDefault(a => a.ArtifactType == "caption")?.TextValue;

        try
        {
            object result;
            if (item.ContentType == "Facebook Cover Photo")
            {
                // Not a normal feed post - a dedicated Graph API action, so
                // this bypasses the generic per-platform dispatch entirely
                // and pushes the already-prepared 'photo' artifact straight
                // to SetCoverPhotoAsync.
                var photo = item.Artifacts.FirstOrDefault(a => a.ArtifactType == "photo" && a.FilePath is not null);
                if (photo is null) throw new InvalidOperationException("No cover photo image found on this item.");

                var imageBytes = await System.IO.File.ReadAllBytesAsync(Path.Combine(env.ContentRootPath, photo.FilePath!));
                var coverResult = await facebookPublisher.SetCoverPhotoAsync(bandId, imageBytes);

                // Registered only now, on confirmed publish (not when the
                // artifact was first uploaded, which might get abandoned/
                // replaced before ever being used) - same auto-registration
                // principle as every other upload path.
                try
                {
                    await catalogStore.RegisterCatalogItemAsync(bandId, imageBytes, "image/jpeg",
                        Path.GetFileName(photo.FilePath), CatalogSource.CoverPhoto, sourceUrl: null,
                        uploadedBy: User.Identity?.Name);
                }
                catch { /* best-effort, doesn't fail the publish itself */ }

                result = coverResult;
            }
            else if (item.NoApi)
            {
                result = new { ok = true, note = "No API for this platform - marked posted manually." };
            }
            else if (item.ContentType == "Photo Album" && item.Platform == "Facebook")
            {
                var photoBytes = new List<byte[]>();
                foreach (var p in item.Artifacts.Where(a => a.ArtifactType == "photo" && a.FilePath is not null))
                    photoBytes.Add(await System.IO.File.ReadAllBytesAsync(Path.Combine(env.ContentRootPath, p.FilePath!)));
                result = await facebookPublisher.PublishPhotoAlbumAsync(bandId, caption, photoBytes);
            }
            else if (item.ContentType == "Photo Album" && item.Platform == "Instagram")
            {
                // Instagram's API only ever fetches images by URL, never a
                // raw upload (see InstagramPublisher's doc comment) - this
                // app needs to be reachable at a real public domain for
                // that URL to actually resolve for Instagram's own
                // servers, same pre-existing gap the single-image
                // Instagram path has always had.
                var imageUrls = item.Artifacts
                    .Where(a => a.ArtifactType == "photo" && a.FilePath is not null)
                    .Select(a => $"{Request.Scheme}://{Request.Host}/{a.FilePath!.Replace('\\', '/').Replace("data/", "")}")
                    .ToList();
                result = await instagramPublisher.PublishCarouselAsync(bandId, caption, imageUrls);
            }
            else if (item.Platform == "Facebook")
            {
                result = await facebookPublisher.PublishAsync(bandId, item, item.Artifacts.ToList(), caption, gigTitle: null);
            }
            else if (item.Platform == "Instagram")
            {
                result = await instagramPublisher.PublishAsync(bandId, caption, body?.ImageUrl);
            }
            else if (item.Platform == "Google Business Profile")
            {
                result = await googleBusinessPublisher.PublishAsync(bandId, caption);
            }
            else
            {
                return StatusCode(502, new { ok = false, error = $"Publishing to {item.Platform} isn't wired up yet." });
            }

            // status deliberately stays Open here, not Posted - the tile
            // stays on the board ("Posted: <when>") until explicitly
            // archived via /archive-posted, so a real successful post
            // doesn't just silently vanish.
            item.PostedAt = DateTime.UtcNow;
            item.PostedVia = "api";
            await db.SaveChangesAsync();

            return Ok(new
            {
                ok = true,
                result,
                item = await SerializeAsync(item, await db.ContentTypes.ToDictionaryAsync(c => c.Id), await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id))
            });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Finalizes a successful API post (see Fart above) - moves
    /// it from "posted, still shown so you can confirm it landed" to fully
    /// archived.</summary>
    [HttpPost("{id:guid}/archive-posted")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> ArchivePosted(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.ScheduleItems.Include(i => i.Artifacts).FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound();
        if (item.PostedAt is null) return BadRequest(new { error = "This item has not been posted yet." });

        item.Status = ScheduleItemStatus.Posted;
        await db.SaveChangesAsync();
        return Ok(await SerializeAsync(item, await db.ContentTypes.ToDictionaryAsync(c => c.Id), await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id)));
    }

    /// <summary>Posted directly on the platform, outside this dashboard.
    /// Clears due-date pressure immediately but flags that the archive
    /// still owes the artifacts that were actually used, unless they're
    /// already all uploaded.</summary>
    [HttpPost("{id:guid}/mark-done-manual")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> MarkDoneManual(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.ScheduleItems.Include(i => i.Artifacts).FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (item is null) return NotFound();

        var contentType = await db.ContentTypes.FindAsync(item.ContentType);
        var required = contentType?.RequiredArtifacts ?? [];
        var complete = required.All(t => item.Artifacts.Any(a => a.ArtifactType == t));

        item.Status = ScheduleItemStatus.Posted;
        item.PostedAt = DateTime.UtcNow;
        item.PostedVia = "manual-outside-system";
        item.ArtifactsOwed = !complete;
        await db.SaveChangesAsync();

        return Ok(await SerializeAsync(item, await db.ContentTypes.ToDictionaryAsync(c => c.Id), await db.Accounts.Where(a => a.BandId == bandId).ToDictionaryAsync(a => a.Id)));
    }

    private async Task ClearExistingArtifactAsync(Guid scheduleItemId, string artifactType)
    {
        var existing = await db.Artifacts.Where(a => a.ScheduleItemId == scheduleItemId && a.ArtifactType == artifactType).ToListAsync();
        foreach (var row in existing)
        {
            if (row.FilePath is not null)
            {
                try { System.IO.File.Delete(Path.Combine(env.ContentRootPath, row.FilePath)); } catch { /* best-effort */ }
            }
        }
        db.Artifacts.RemoveRange(existing);
    }

    private async Task<object> SerializeAsync(
        ScheduleItem item,
        Dictionary<string, ContentType> contentTypes,
        Dictionary<Guid, Account> accountsById)
    {
        var presentTypes = item.Artifacts.Select(a => a.ArtifactType).ToHashSet();
        var required = contentTypes.TryGetValue(item.ContentType, out var ct) ? ct.RequiredArtifacts : [];
        var status = StatusEngine.ComputeStatus(item, presentTypes, required);

        string? manualInstructions = null;
        var match = CadenceRuleIdInTemplateKey().Match(item.TemplateKey);
        if (match.Success && Guid.TryParse(match.Groups[1].Value, out var ruleId))
        {
            manualInstructions = (await db.CadenceRules.FindAsync(ruleId))?.ManualInstructions;
        }

        Account? account = item.AccountId is { } accId ? accountsById.GetValueOrDefault(accId) : null;
        var automated = !item.NoApi && account?.EncryptedCredentials is not null;

        // FindAsync checks the DbContext's own tracked-entity cache before
        // hitting the database - serializing a whole list of items for the
        // same Band only ever costs one real query here, not one per item.
        var band = await db.Bands.FindAsync(item.BandId);

        Gig? gig = null;
        List<WithAct> gigWithActs = [];
        MediaItem? media = null;
        GalleryImage? gallery = null;
        if (band is not null)
        {
            if (!string.IsNullOrEmpty(item.GigRef))
            {
                gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == item.GigRef);
                if (gig is not null)
                {
                    gigWithActs = await db.GigWithBands.Where(w => w.GigId == gig.Id).Include(w => w.WithBand)
                        .OrderBy(w => w.SortOrder).Select(w => new WithAct(w.WithBand.Name, w.Url)).ToListAsync();
                }
            }
            if (!string.IsNullOrEmpty(item.MediaRef)) media = await db.MediaItems.FirstOrDefaultAsync(m => m.BandId == band.Id && m.Ref == item.MediaRef);
            if (!string.IsNullOrEmpty(item.GalleryRef)) gallery = await db.GalleryImages.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == item.GalleryRef);
        }

        return new
        {
            id = item.Id,
            template_key = item.TemplateKey,
            platform = item.Platform,
            assignee_user_id1 = item.AssigneeUserId1,
            assignee_user_id2 = item.AssigneeUserId2,
            content_type = item.ContentType,
            category = item.Category,
            example = item.Example,
            due_date = item.DueDate?.ToString("yyyy-MM-dd"),
            no_api = item.NoApi,
            auto_handled = item.AutoHandled,
            gig_ref = item.GigRef,
            media_ref = item.MediaRef,
            gallery_ref = item.GalleryRef,
            artifacts_owed = item.ArtifactsOwed,
            posted_at = item.PostedAt,
            posted_via = item.PostedVia,
            created_at = item.CreatedAt,
            required_artifacts = required,
            artifacts = item.Artifacts.Select(a => new
            {
                id = a.Id,
                artifact_type = a.ArtifactType,
                file_path = a.FilePath,
                text_value = a.TextValue,
                uploaded_by = a.UploadedBy,
                uploaded_at = a.UploadedAt
            }),
            status = new { color = status.Color, label = status.Label, ready = status.Ready },
            gig_title = gig?.Title,
            gig = gig is null ? null : new
            {
                id = gig.Ref,
                title = gig.Title,
                venue = gig.Venue,
                venueUrl = gig.VenueUrl,
                date = GigDateTimeFormatting.FormatDate(gig.Date),
                time = gig.Time,
                doorsTime = GigDateTimeFormatting.FormatTime(gig.DoorsTime),
                openerTime = GigDateTimeFormatting.FormatTime(gig.OpenerTime),
                headlinerTime = GigDateTimeFormatting.FormatTime(gig.HeadlinerTime),
                address = gig.Address,
                with = gigWithActs,
                ticketsUrl = gig.TicketsUrl,
                flyerMain = gig.FlyerMain,
                freeAdmission = gig.FreeAdmission,
                customTicketsText = gig.CustomTicketsText,
                ticketMode = gig.TicketMode
            },
            gig_issues = Array.Empty<string>(),
            media_title = media?.Title,
            media = media is null ? null : new
            {
                id = media.Ref,
                title = media.Title,
                url = media.Url,
                thumbnail = media.Thumbnail
            },
            gallery_title = gallery?.Alt,
            gallery = gallery is null ? null : new
            {
                id = gallery.Ref,
                alt = gallery.Alt,
                thumb = gallery.Thumb,
                full = gallery.Full
            },
            manual_instructions = manualInstructions,
            automated,
            account_label = account?.Label
        };
    }
}
