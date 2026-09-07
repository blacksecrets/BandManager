using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SetAvailabilityRequest(string Status, string? Note);

/// <summary>
/// Band-wide availability roster. Reading is open to the whole band (the
/// Calendar's day-detail panel shows everyone's status). Writing is
/// resource-scoped, not policy-scoped: a BandAdmin can set/clear anyone's
/// row, a plain member can only touch their own - checked per-request
/// here rather than via a class-level [Authorize] policy, since the same
/// action (PUT one day's status) is allowed or denied depending on whose
/// row it targets, not just who's asking.
/// </summary>
[ApiController]
[Route("/api/availability")]
[Authorize(Policy = "BandMember")]
public class AvailabilityController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private async Task<bool> IsBandAdminAsync(Guid bandId, Guid userId)
    {
        if (User.IsSuperAdmin()) return true;
        var membership = await db.BandMemberships.AsNoTracking().FirstOrDefaultAsync(m => m.BandId == bandId && m.UserId == userId);
        return membership?.Role == BandRole.BandAdmin;
    }

    private static object Serialize(Availability a) => new
    {
        userId = a.UserId,
        date = a.Date.ToString("yyyy-MM-dd"),
        status = a.Status.ToString(),
        note = a.Note
    };

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (to < from) return BadRequest(new { error = "'to' must be on or after 'from'." });

        var rows = await db.Availabilities.AsNoTracking()
            .Where(a => a.BandId == bandId && a.Date >= from && a.Date <= to)
            .Include(a => a.User)
            .Select(a => new
            {
                userId = a.UserId,
                firstName = a.User.DisplayName,
                date = a.Date.ToString("yyyy-MM-dd"),
                status = a.Status.ToString(),
                note = a.Note
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPut("{userId:guid}/{date}")]
    public async Task<IActionResult> Set(Guid userId, DateOnly date, [FromBody] SetAvailabilityRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var callerId = User.GetUserId();
        if (callerId is null) return Unauthorized();

        if (userId != callerId && !await IsBandAdminAsync(bandId, callerId.Value))
            return Forbid();

        // The target user must actually be a member of the active band -
        // otherwise a BandAdmin could set availability for someone with no
        // connection to this band at all.
        var isMember = userId == callerId || await db.BandMemberships.AnyAsync(m => m.BandId == bandId && m.UserId == userId);
        if (!isMember) return BadRequest(new { error = "That user isn't a member of this band." });

        if (!Enum.TryParse<AvailabilityStatus>(request.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = "Status must be Available, Unavailable, or Tentative." });

        var note = request.Note?.Trim();
        if (note is { Length: > 300 }) note = note[..300];

        var row = await db.Availabilities.FirstOrDefaultAsync(a => a.BandId == bandId && a.UserId == userId && a.Date == date);
        if (row is null)
        {
            row = new Availability { BandId = bandId, UserId = userId, Date = date };
            db.Availabilities.Add(row);
        }
        row.Status = status;
        row.Note = string.IsNullOrEmpty(note) ? null : note;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(Serialize(row));
    }

    [HttpDelete("{userId:guid}/{date}")]
    public async Task<IActionResult> Clear(Guid userId, DateOnly date)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var callerId = User.GetUserId();
        if (callerId is null) return Unauthorized();

        if (userId != callerId && !await IsBandAdminAsync(bandId, callerId.Value))
            return Forbid();

        var row = await db.Availabilities.FirstOrDefaultAsync(a => a.BandId == bandId && a.UserId == userId && a.Date == date);
        if (row is null) return Ok(new { ok = true });

        db.Availabilities.Remove(row);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
