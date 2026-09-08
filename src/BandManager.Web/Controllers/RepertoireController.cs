using System.Text;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record AddRepertoireEntryRequest(Guid SongId, string? Status);
public record SetRepertoireStatusRequest(string Status);
public record AddBandInstrumentRequest(string Name);

/// <summary>
/// The active Band's repertoire (RepertoireEntry rows joined to the
/// shared Song catalog) and its instrument roster (BandInstrument), used
/// to decide which of a Song's Tunings entries apply here. Reading is
/// open to the whole Band (BandMember); building/editing the repertoire
/// is BandAdmin-only, per the "a Band Admin should be able to build the
/// repertoire" request - overridden per-action below.
/// </summary>
[ApiController]
[Route("/api/repertoire")]
[Authorize(Policy = "BandMember")]
public class RepertoireController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private const long MaxImportBytes = 5 * 1024 * 1024;

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object SerializeSong(Song s, Dictionary<string, string> tunings) => new
    {
        id = s.Id,
        title = s.Title,
        originalArtist = s.OriginalArtist,
        album = s.Album,
        key = s.Key,
        lengthSeconds = s.LengthSeconds,
        youTubeUrl = s.YouTubeUrl,
        spotifyUrl = s.SpotifyUrl,
        songsterrUrl = s.SongsterrUrl,
        tunings
    };

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var entries = await db.RepertoireEntries.AsNoTracking()
            .Include(e => e.Song)
            .Where(e => e.BandId == bandId)
            .OrderBy(e => e.Song.Title)
            .ToListAsync();

        var result = entries.Select(e => new
        {
            id = e.Id,
            status = e.Status.ToString(),
            addedAt = e.AddedAt,
            song = SerializeSong(e.Song, e.Song.Tunings ?? [])
        });
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Add([FromBody] AddRepertoireEntryRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        if (!await db.Songs.AnyAsync(s => s.Id == request.SongId))
            return BadRequest(new { error = "That song doesn't exist." });
        if (await db.RepertoireEntries.AnyAsync(e => e.BandId == bandId && e.SongId == request.SongId))
            return BadRequest(new { error = "Already in the repertoire." });

        var status = RepertoireStatus.New;
        if (!string.IsNullOrEmpty(request.Status) && !Enum.TryParse(request.Status, ignoreCase: true, out status))
            return BadRequest(new { error = "Invalid status." });

        var entry = new RepertoireEntry { BandId = bandId, SongId = request.SongId, Status = status };
        db.RepertoireEntries.Add(entry);
        await db.SaveChangesAsync();
        return Ok(new { ok = true, id = entry.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetRepertoireStatusRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!Enum.TryParse<RepertoireStatus>(request.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = "Invalid status." });

        var entry = await db.RepertoireEntries.FirstOrDefaultAsync(e => e.Id == id && e.BandId == bandId);
        if (entry is null) return NotFound(new { error = "Not found" });

        entry.Status = status;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var entry = await db.RepertoireEntries.FirstOrDefaultAsync(e => e.Id == id && e.BandId == bandId);
        if (entry is null) return NotFound(new { error = "Not found" });

        // A GigSet referencing this Song (via GigSetSong) isn't touched -
        // removing something from the repertoire doesn't rewrite history
        // for a gig that already used it.
        db.RepertoireEntries.Remove(entry);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- CSV bulk import, merged into this Band's repertoire ---

    [HttpGet("import/template")]
    public IActionResult ImportTemplate()
    {
        var bytes = Encoding.UTF8.GetBytes(SongCsvImportService.BuildTemplateCsv());
        return File(bytes, "text/csv", "song-catalog-template.csv");
    }

    // Every row lands in this Band's repertoire either way. Against the
    // shared catalog (matched by Title+OriginalArtist, case-insensitive):
    // an exact match with no field differences just needs the repertoire
    // entry; a match with different fields stages a SongEditRequest for
    // just those fields (same review path as SongsController.ProposeEdit -
    // the catalog keeps showing the old value until SuperAdmin approves,
    // but this Band's repertoire entry is added immediately regardless);
    // no match at all creates the Song immediately (so it's visible to
    // everyone right away) plus a SongEditRequest whose "old" side is null
    // for every field, purely so SuperAdmin can review it - approving or
    // rejecting a brand-new song never touches the Song itself, since there
    // was never an accepted prior value to fall back to.
    // BandMember-accessible (not BandAdmin-only) - it lives on the
    // Repertoire page now, open to whoever's already allowed to add
    // songs there by hand; nothing here is more sensitive than that.
    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<IActionResult> Import()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });
        var form = await Request.ReadFormAsync();
        var payload = await form.Files.GetFile("file").ToUploadedFilePayloadAsync();
        if (payload is null) return BadRequest(new { error = "No file provided" });

        var parsed = SongCsvImportService.Parse(payload.Bytes);
        if (parsed.Errors.Count > 0)
        {
            return BadRequest(new
            {
                error = $"{parsed.Errors.Count} row(s) failed validation - fix and re-upload.",
                rowErrors = parsed.Errors
            });
        }

        var newSongsAdded = 0;
        var changesSubmittedForReview = 0;
        var unchanged = 0;
        var alreadyPendingSkipped = 0;

        await using var tx = await db.Database.BeginTransactionAsync();

        var seen = new HashSet<(string, string)>();
        foreach (var row in parsed.Rows)
        {
            var key = (row.Title.ToLowerInvariant(), row.OriginalArtist.ToLowerInvariant());
            if (!seen.Add(key)) continue; // same row twice in one file - only process once

            var existing = await db.Songs.FirstOrDefaultAsync(s =>
                s.Title.ToLower() == row.Title.ToLower() && s.OriginalArtist != null && s.OriginalArtist.ToLower() == row.OriginalArtist.ToLower());

            Guid songId;
            if (existing is null)
            {
                var song = new Song
                {
                    Title = row.Title,
                    OriginalArtist = row.OriginalArtist,
                    Album = row.Album,
                    Key = row.Key,
                    LengthSeconds = row.LengthSeconds,
                    YouTubeUrl = row.YouTubeUrl,
                    SpotifyUrl = row.SpotifyUrl,
                    SongsterrUrl = row.SongsterrUrl
                };
                db.Songs.Add(song);
                await db.SaveChangesAsync(); // song.Id needed below
                songId = song.Id;

                var newChanges = SongEditDiff.Build(
                    null, null, null, null, null, null, null, null,
                    row.Title, row.OriginalArtist, row.Album, row.Key, row.LengthSeconds, row.YouTubeUrl, row.SpotifyUrl, row.SongsterrUrl);
                db.SongEditRequests.Add(new SongEditRequest { SongId = songId, RequestedByUserId = userId.Value, BandId = bandId, Changes = newChanges });
                newSongsAdded++;
            }
            else
            {
                songId = existing.Id;
                var fieldChanges = SongEditDiff.Build(
                    existing.Title, existing.OriginalArtist, existing.Album, existing.Key, existing.LengthSeconds, existing.YouTubeUrl, existing.SpotifyUrl, existing.SongsterrUrl,
                    row.Title, row.OriginalArtist, row.Album, row.Key, row.LengthSeconds, row.YouTubeUrl, row.SpotifyUrl, row.SongsterrUrl);

                if (fieldChanges.Count == 0)
                {
                    unchanged++;
                }
                else if (await db.SongEditRequests.AnyAsync(r => r.SongId == songId && r.Status == EditRequestStatus.Pending))
                {
                    alreadyPendingSkipped++;
                }
                else
                {
                    db.SongEditRequests.Add(new SongEditRequest { SongId = songId, RequestedByUserId = userId.Value, BandId = bandId, Changes = fieldChanges });
                    changesSubmittedForReview++;
                }
            }

            if (!await db.RepertoireEntries.AnyAsync(e => e.BandId == bandId && e.SongId == songId))
                db.RepertoireEntries.Add(new RepertoireEntry { BandId = bandId, SongId = songId, Status = RepertoireStatus.New });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new { ok = true, newSongsAdded, changesSubmittedForReview, unchanged, alreadyPendingSkipped });
    }

    // --- This Band's instrument roster (drives which Song.Tunings keys show up) ---

    [HttpGet("instruments")]
    public async Task<IActionResult> ListInstruments()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var instruments = await db.BandInstruments.AsNoTracking()
            .Where(i => i.BandId == bandId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Name)
            .Select(i => new { id = i.Id, name = i.Name })
            .ToListAsync();
        return Ok(instruments);
    }

    [HttpPost("instruments")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> AddInstrument([FromBody] AddBandInstrumentRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required." });
        if (await db.BandInstruments.AnyAsync(i => i.BandId == bandId && i.Name == name))
            return BadRequest(new { error = "Already have an instrument with that name." });

        var maxSort = await db.BandInstruments.Where(i => i.BandId == bandId)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        var instrument = new BandInstrument { BandId = bandId, Name = name, SortOrder = maxSort + 1 };
        db.BandInstruments.Add(instrument);
        await db.SaveChangesAsync();
        return Ok(new { id = instrument.Id, name = instrument.Name });
    }

    [HttpDelete("instruments/{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> RemoveInstrument(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var instrument = await db.BandInstruments.FirstOrDefaultAsync(i => i.Id == id && i.BandId == bandId);
        if (instrument is null) return NotFound(new { error = "Not found" });

        db.BandInstruments.Remove(instrument);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
