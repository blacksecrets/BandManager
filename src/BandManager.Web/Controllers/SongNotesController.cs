using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SetSongNoteRequest(string? Text);

/// <summary>
/// Self-service per-user notes on the active band's repertoire ("my
/// note" in the Repertoire grid - see RepertoireEntry.cs/SongNote.cs).
/// Unlike NotificationPreferencesController, GET does NOT materialize a
/// row per song - most songs will never get a note from a given member,
/// and the Repertoire grid already has the full song list to merge
/// against, so only actual notes are returned.
/// </summary>
[ApiController]
[Route("/api/song-notes")]
[Authorize(Policy = "BandMember")]
public class SongNotesController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    // My own notes across the active band's whole repertoire, keyed by
    // RepertoireEntryId - the Repertoire grid merges this against its own
    // song list client-side.
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var notes = await db.SongNotes.AsNoTracking()
            .Where(n => n.UserId == userId && n.RepertoireEntry.BandId == bandId)
            .Select(n => new { repertoireEntryId = n.RepertoireEntryId, text = n.Text })
            .ToListAsync();
        return Ok(notes);
    }

    [HttpPut("{repertoireEntryId:guid}")]
    public async Task<IActionResult> Set(Guid repertoireEntryId, [FromBody] SetSongNoteRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        if (!await db.RepertoireEntries.AnyAsync(e => e.Id == repertoireEntryId && e.BandId == bandId))
            return NotFound(new { error = "Not found" });

        var text = request.Text?.Trim();
        var existing = await db.SongNotes.FirstOrDefaultAsync(n => n.UserId == userId && n.RepertoireEntryId == repertoireEntryId);

        if (string.IsNullOrEmpty(text))
        {
            if (existing is not null)
            {
                db.SongNotes.Remove(existing);
                await db.SaveChangesAsync();
            }
            return Ok(new { ok = true, text = (string?)null });
        }

        text = text[..Math.Min(text.Length, 2000)];
        if (existing is null)
        {
            db.SongNotes.Add(new SongNote { UserId = userId.Value, RepertoireEntryId = repertoireEntryId, Text = text });
        }
        else
        {
            existing.Text = text;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, text });
    }

    // Used when printing a setlist and the current user has chosen to
    // include a specific bandmate's notes too - band-scoped like GetMine,
    // just for someone else's UserId. Any band member can read anyone
    // else's notes here (no privacy boundary between bandmates was
    // requested), same openness as the rest of this collaborative board.
    [HttpGet("by-user/{userId:guid}")]
    public async Task<IActionResult> GetByUser(Guid userId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.BandMemberships.AnyAsync(m => m.UserId == userId && m.BandId == bandId))
            return NotFound(new { error = "Not found" });

        var notes = await db.SongNotes.AsNoTracking()
            .Where(n => n.UserId == userId && n.RepertoireEntry.BandId == bandId)
            .Select(n => new { repertoireEntryId = n.RepertoireEntryId, text = n.Text })
            .ToListAsync();
        return Ok(notes);
    }
}
