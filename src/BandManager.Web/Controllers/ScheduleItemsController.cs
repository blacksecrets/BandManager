using System.Text.Json;
using System.Text.RegularExpressions;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Publishers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record FartRequest(string? ImageUrl);

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
            .Where(i => i.BandId == bandId)
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
    /// `artifactType` and `file`. For text artifacts (caption, event_url),
    /// send JSON { artifactType, text }. Ported from the old app's
    /// POST /items/:id/upload - Catalog integration (pick-from-catalog,
    /// paste-a-URL) isn't ported yet, so this only supports a raw upload
    /// or raw text, not those two extra input modes.</summary>
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

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            artifactType = form["artifactType"];
            file = form.Files.GetFile("file");
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

        if (MimePrefixForType.TryGetValue(artifactType, out var mimePrefix))
        {
            if (file is null) return BadRequest(new { error = "Provide a file" });
            if (!(file.ContentType ?? "").StartsWith(mimePrefix, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = $"Expected a {mimePrefix.TrimEnd('/')} file for \"{artifactType}\", got {file.ContentType}" });

            var ext = Path.GetExtension(file.FileName).TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = mimePrefix switch { "image/" => "jpg", "video/" => "mp4", _ => "mp3" };

            var dir = Path.Combine(env.ContentRootPath, "data", "uploads", item.Id.ToString());
            Directory.CreateDirectory(dir);
            var fileName = $"{artifactType}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{ext}";
            var destPath = Path.Combine(dir, fileName);
            await using (var stream = System.IO.File.Create(destPath))
            {
                await file.CopyToAsync(stream);
            }

            await ClearExistingArtifactAsync(item.Id, artifactType);
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
            owner = item.Owner,
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
                date = gig.Date,
                time = gig.Time,
                doorsTime = gig.DoorsTime,
                openerTime = gig.OpenerTime,
                headlinerTime = gig.HeadlinerTime,
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
