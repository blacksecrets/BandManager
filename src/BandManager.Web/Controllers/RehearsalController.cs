using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveRehearsalRequest(string? Title, string? Location, DateTime StartsAt, DateTime EndsAt);

/// <summary>
/// Full CRUD open to any BandMember, per the explicit "all band members
/// can manage rehearsal entries" request - unlike most other write paths
/// in this app, there's no BandAdmin gate here.
/// </summary>
[ApiController]
[Route("/api/rehearsals")]
[Authorize(Policy = "BandMember")]
public class RehearsalController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(Rehearsal r) => new
    {
        id = r.Id,
        title = r.Title,
        location = r.Location,
        startsAt = r.StartsAt,
        endsAt = r.EndsAt,
        recurringRehearsalRuleId = r.RecurringRehearsalRuleId,
        createdByUserId = r.CreatedByUserId
    };

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (to < from) return BadRequest(new { error = "'to' must be on or after 'from'." });

        var rows = await db.Rehearsals.AsNoTracking()
            .Where(r => r.BandId == bandId && r.StartsAt < to && r.EndsAt > from)
            .OrderBy(r => r.StartsAt)
            .ToListAsync();
        return Ok(rows.Select(Serialize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRehearsalRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (request.EndsAt <= request.StartsAt) return BadRequest(new { error = "End time must be after start time." });

        var title = request.Title?.Trim();
        var location = request.Location?.Trim();

        var rehearsal = new Rehearsal
        {
            BandId = bandId,
            Title = string.IsNullOrEmpty(title) ? null : title,
            Location = string.IsNullOrEmpty(location) ? null : location,
            StartsAt = DateTime.SpecifyKind(request.StartsAt, DateTimeKind.Utc),
            EndsAt = DateTime.SpecifyKind(request.EndsAt, DateTimeKind.Utc),
            CreatedByUserId = userId.Value
        };
        db.Rehearsals.Add(rehearsal);
        await db.SaveChangesAsync();
        return Ok(Serialize(rehearsal));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRehearsalRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (request.EndsAt <= request.StartsAt) return BadRequest(new { error = "End time must be after start time." });

        var rehearsal = await db.Rehearsals.FirstOrDefaultAsync(r => r.Id == id && r.BandId == bandId);
        if (rehearsal is null) return NotFound(new { error = "Not found" });

        var title = request.Title?.Trim();
        var location = request.Location?.Trim();
        rehearsal.Title = string.IsNullOrEmpty(title) ? null : title;
        rehearsal.Location = string.IsNullOrEmpty(location) ? null : location;
        rehearsal.StartsAt = DateTime.SpecifyKind(request.StartsAt, DateTimeKind.Utc);
        rehearsal.EndsAt = DateTime.SpecifyKind(request.EndsAt, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        return Ok(Serialize(rehearsal));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var rehearsal = await db.Rehearsals.FirstOrDefaultAsync(r => r.Id == id && r.BandId == bandId);
        if (rehearsal is null) return Ok(new { ok = true });

        db.Rehearsals.Remove(rehearsal);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
