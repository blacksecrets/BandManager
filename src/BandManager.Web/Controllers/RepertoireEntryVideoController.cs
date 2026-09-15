using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record AddBreakpointRequest(string Label, double TimestampSeconds);
public record UpdateBreakpointRequest(string Label, double TimestampSeconds);
public record ReorderBreakpointsRequest(List<Guid> OrderedIds);
public record SaveTempoSegmentRequest(double StartTimestampSeconds, decimal Bpm, decimal BeatOffsetSeconds);
public record ReplaceTempoSegmentsRequest(List<SaveTempoSegmentRequest> Segments, bool Confirmed);

/// <summary>
/// A Band's stage video for one of its RepertoireEntry rows, plus its
/// breakpoints (jump-points for the Stage Video Player) and tempo segments
/// (the click track's BPM map). Deliberately Band-private - see
/// RepertoireEntryVideo's own doc comment for why this hangs off
/// RepertoireEntry rather than the shared Song. The video file itself
/// goes through the normal Catalog system (CatalogStore) so it gets the
/// same storage/hashing/replace semantics every other upload already has;
/// this controller is just the attachment layer on top. Read open to any
/// BandMember (matches every other repertoire-management read); writes
/// are BandAdmin-only, same split RepertoireController itself uses.
/// </summary>
[ApiController]
[Route("/api/repertoire/{repertoireEntryId:guid}/video")]
[Authorize(Policy = "BandMember")]
public class RepertoireEntryVideoController(ApplicationDbContext db, IActiveBandAccessor activeBand, CatalogStore catalogStore) : ControllerBase
{
    // Stage videos run several minutes, not receipt-photo-sized - a much
    // higher cap than the 20MB receipt/image uploads elsewhere in this app.
    private const long MaxVideoBytes = 500 * 1024 * 1024;

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private async Task<RepertoireEntry?> FindEntryAsync(Guid repertoireEntryId, Guid bandId) =>
        await db.RepertoireEntries.FirstOrDefaultAsync(e => e.Id == repertoireEntryId && e.BandId == bandId);

    private async Task<RepertoireEntryVideo?> FindVideoAsync(Guid repertoireEntryId, Guid bandId) =>
        await db.RepertoireEntryVideos
            .Include(v => v.CatalogItem)
            .Include(v => v.Breakpoints)
            .Include(v => v.TempoSegments)
            .Include(v => v.RepertoireEntry)
            .FirstOrDefaultAsync(v => v.RepertoireEntryId == repertoireEntryId && v.RepertoireEntry.BandId == bandId);

    // Raw stored path, same convention CatalogController.Serialize already
    // uses (file_path) - the client maps it through the same
    // data/catalog/... -> /catalog-files/... rewrite catalog.js's
    // catalogFileUrl() already does, so there's one canonical mapping, not
    // a second server-side copy of it.
    private static object Serialize(RepertoireEntryVideo v) => new
    {
        id = v.Id,
        catalogItemId = v.CatalogItemId,
        filePath = v.CatalogItem?.FilePath,
        durationSeconds = v.DurationSeconds,
        clickTrackConfirmed = v.ClickTrackConfirmed,
        breakpoints = v.Breakpoints.OrderBy(b => b.SortOrder).Select(b => new
        {
            id = b.Id,
            label = b.Label,
            timestampSeconds = b.TimestampSeconds,
            sortOrder = b.SortOrder,
            isSongStart = b.IsSongStart
        }),
        tempoSegments = v.TempoSegments.OrderBy(t => t.SortOrder).Select(t => new
        {
            id = t.Id,
            startTimestampSeconds = t.StartTimestampSeconds,
            bpm = t.Bpm,
            beatOffsetSeconds = t.BeatOffsetSeconds,
            sortOrder = t.SortOrder
        })
    };

    [HttpGet]
    public async Task<IActionResult> Get(Guid repertoireEntryId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var video = await FindVideoAsync(repertoireEntryId, bandId);
        return video is null ? Ok((object?)null) : Ok(Serialize(video));
    }

    // First upload creates the RepertoireEntryVideo row (plus its
    // un-deletable "Song Start" breakpoint at 0); a later call replaces the
    // file in place via CatalogStore.ReplaceCatalogItemFileAsync, same
    // "Replace With This" semantics the Catalog page itself already uses -
    // breakpoints/tempo segments survive a replace untouched (the operator
    // would need to re-check them against the new file's timing by eye,
    // but doesn't lose the work of having entered them).
    [HttpPost]
    [Authorize(Policy = "BandAdmin")]
    [RequestSizeLimit(MaxVideoBytes)]
    public async Task<IActionResult> Upload(Guid repertoireEntryId, [FromForm] double? durationSeconds)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var entry = await FindEntryAsync(repertoireEntryId, bandId);
        if (entry is null) return NotFound(new { error = "Not found" });

        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });
        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided" });
        if (!file.ContentType.StartsWith("video/")) return BadRequest(new { error = "Must be a video file." });

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var buffer = stream.ToArray();
        var userName = User.Identity?.Name;

        var video = await db.RepertoireEntryVideos.Include(v => v.CatalogItem)
            .FirstOrDefaultAsync(v => v.RepertoireEntryId == repertoireEntryId);

        if (video?.CatalogItem is not null)
        {
            await catalogStore.ReplaceCatalogItemFileAsync(video.CatalogItem, buffer, file.ContentType, file.FileName, CatalogSource.StageVideo, userName);
        }
        else
        {
            var catalogItem = await catalogStore.RegisterCatalogItemAsync(
                bandId, buffer, file.ContentType, file.FileName, CatalogSource.StageVideo, sourceUrl: null, uploadedBy: userName,
                label: $"{entry.Id}-stage-video");
            if (video is null)
            {
                video = new RepertoireEntryVideo { RepertoireEntryId = repertoireEntryId, CatalogItemId = catalogItem.Id };
                db.RepertoireEntryVideos.Add(video);
                await db.SaveChangesAsync();
                db.RepertoireEntryVideoBreakpoints.Add(new RepertoireEntryVideoBreakpoint
                {
                    RepertoireEntryVideoId = video.Id, Label = "Song Start", TimestampSeconds = 0, SortOrder = 0, IsSongStart = true
                });
            }
            else
            {
                video.CatalogItemId = catalogItem.Id;
            }
        }

        video.DurationSeconds = durationSeconds;
        await db.SaveChangesAsync();

        var reloaded = await FindVideoAsync(repertoireEntryId, bandId);
        return Ok(Serialize(reloaded!));
    }

    [HttpDelete]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Delete(Guid repertoireEntryId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var video = await FindVideoAsync(repertoireEntryId, bandId);
        if (video is null) return NotFound(new { error = "Not found" });
        db.RepertoireEntryVideos.Remove(video);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- Breakpoints ---

    [HttpPost("breakpoints")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> AddBreakpoint(Guid repertoireEntryId, [FromBody] AddBreakpointRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var video = await FindVideoAsync(repertoireEntryId, bandId);
        if (video is null) return NotFound(new { error = "Attach a video first." });

        var label = request.Label?.Trim();
        if (string.IsNullOrEmpty(label)) return BadRequest(new { error = "Label is required." });
        if (request.TimestampSeconds < 0) return BadRequest(new { error = "Timestamp can't be negative." });

        var breakpoint = new RepertoireEntryVideoBreakpoint
        {
            RepertoireEntryVideoId = video.Id,
            Label = label,
            TimestampSeconds = request.TimestampSeconds,
            SortOrder = video.Breakpoints.Count == 0 ? 0 : video.Breakpoints.Max(b => b.SortOrder) + 1
        };
        db.RepertoireEntryVideoBreakpoints.Add(breakpoint);
        await db.SaveChangesAsync();
        return Ok(new { id = breakpoint.Id, label = breakpoint.Label, timestampSeconds = breakpoint.TimestampSeconds, sortOrder = breakpoint.SortOrder, isSongStart = false });
    }

    [HttpPut("breakpoints/{breakpointId:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> UpdateBreakpoint(Guid repertoireEntryId, Guid breakpointId, [FromBody] UpdateBreakpointRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var video = await FindVideoAsync(repertoireEntryId, bandId);
        var breakpoint = video?.Breakpoints.FirstOrDefault(b => b.Id == breakpointId);
        if (breakpoint is null) return NotFound(new { error = "Not found" });

        // The Song Start row's label stays fixed - only its timestamp is
        // ever meaningfully editable (for videos with lead-in footage).
        if (!breakpoint.IsSongStart)
        {
            var label = request.Label?.Trim();
            if (string.IsNullOrEmpty(label)) return BadRequest(new { error = "Label is required." });
            breakpoint.Label = label;
        }
        if (request.TimestampSeconds < 0) return BadRequest(new { error = "Timestamp can't be negative." });
        breakpoint.TimestampSeconds = request.TimestampSeconds;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("breakpoints/order")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> ReorderBreakpoints(Guid repertoireEntryId, [FromBody] ReorderBreakpointsRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var video = await FindVideoAsync(repertoireEntryId, bandId);
        if (video is null) return NotFound(new { error = "Not found" });

        var byId = video.Breakpoints.ToDictionary(b => b.Id);
        if (request.OrderedIds.Count != byId.Count || request.OrderedIds.Any(id => !byId.ContainsKey(id)))
            return BadRequest(new { error = "The list must contain exactly this video's breakpoints." });

        for (var i = 0; i < request.OrderedIds.Count; i++) byId[request.OrderedIds[i]].SortOrder = i;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("breakpoints/{breakpointId:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> DeleteBreakpoint(Guid repertoireEntryId, Guid breakpointId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var video = await FindVideoAsync(repertoireEntryId, bandId);
        var breakpoint = video?.Breakpoints.FirstOrDefault(b => b.Id == breakpointId);
        if (breakpoint is null) return NotFound(new { error = "Not found" });
        if (breakpoint.IsSongStart) return BadRequest(new { error = "The Song Start breakpoint can't be deleted - move it instead." });

        db.RepertoireEntryVideoBreakpoints.Remove(breakpoint);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- Tempo segments (the click track's BPM map) ---

    // Whole-list replace, not per-segment CRUD - both the AI detector and
    // the correction editor always operate on "here's the full segment
    // list for this video" (add/remove/re-time all at once), so there's no
    // meaningful partial-update case worth a finer-grained API here.
    [HttpPut("tempo-segments")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> ReplaceTempoSegments(Guid repertoireEntryId, [FromBody] ReplaceTempoSegmentsRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var video = await FindVideoAsync(repertoireEntryId, bandId);
        if (video is null) return NotFound(new { error = "Attach a video first." });
        if (request.Segments.Any(s => s.Bpm <= 0)) return BadRequest(new { error = "BPM must be greater than 0." });

        db.RepertoireEntryVideoTempoSegments.RemoveRange(video.TempoSegments);
        var ordered = request.Segments.OrderBy(s => s.StartTimestampSeconds).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            db.RepertoireEntryVideoTempoSegments.Add(new RepertoireEntryVideoTempoSegment
            {
                RepertoireEntryVideoId = video.Id,
                StartTimestampSeconds = ordered[i].StartTimestampSeconds,
                Bpm = ordered[i].Bpm,
                BeatOffsetSeconds = ordered[i].BeatOffsetSeconds,
                SortOrder = i
            });
        }
        video.ClickTrackConfirmed = request.Confirmed;
        await db.SaveChangesAsync();

        var reloaded = await FindVideoAsync(repertoireEntryId, bandId);
        return Ok(Serialize(reloaded!));
    }
}
