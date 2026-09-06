using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
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
