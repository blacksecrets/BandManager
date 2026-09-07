using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveRecurringRehearsalRuleRequest(string DayOfWeek, string StartTime, int DurationMinutes, string? Location, bool Active);

/// <summary>
/// BandAdmin-only, unlike RehearsalController's open-to-any-member CRUD -
/// setting the standing schedule is an admin decision, editing/canceling
/// one week's occurrence is not (see Rehearsal.cs's doc comment).
/// </summary>
[ApiController]
[Route("/api/recurring-rehearsal-rules")]
[Authorize(Policy = "BandAdmin")]
public class RecurringRehearsalRuleController(ApplicationDbContext db, IActiveBandAccessor activeBand, Scheduler scheduler) : ControllerBase
{
    private static readonly string[] ValidDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(RecurringRehearsalRule r) => new
    {
        id = r.Id,
        dayOfWeek = r.DayOfWeek,
        startTime = r.StartTime.ToString("HH:mm"),
        durationMinutes = r.DurationMinutes,
        location = r.Location,
        active = r.Active
    };

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var rows = await db.RecurringRehearsalRules.AsNoTracking()
            .Where(r => r.BandId == bandId)
            .OrderBy(r => r.DayOfWeek)
            .ToListAsync();
        return Ok(rows.Select(Serialize));
    }

    private static bool TryValidate(SaveRecurringRehearsalRuleRequest request, out TimeOnly startTime, out string? error)
    {
        error = null;
        startTime = default;
        if (!ValidDays.Contains(request.DayOfWeek))
        {
            error = "Invalid day of week.";
            return false;
        }
        if (!TimeOnly.TryParse(request.StartTime, out startTime))
        {
            error = "Invalid start time.";
            return false;
        }
        if (request.DurationMinutes is < 15 or > 720)
        {
            error = "Duration must be between 15 and 720 minutes.";
            return false;
        }
        return true;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRecurringRehearsalRuleRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!TryValidate(request, out var startTime, out var validationError))
            return BadRequest(new { error = validationError });

        var location = request.Location?.Trim();
        var rule = new RecurringRehearsalRule
        {
            BandId = bandId,
            DayOfWeek = request.DayOfWeek,
            StartTime = startTime,
            DurationMinutes = request.DurationMinutes,
            Location = string.IsNullOrEmpty(location) ? null : location,
            Active = request.Active,
            CreatedByUserId = userId.Value
        };
        db.RecurringRehearsalRules.Add(rule);
        await db.SaveChangesAsync();

        await scheduler.GenerateRehearsalsFromRulesAsync(bandId);
        return Ok(Serialize(rule));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRecurringRehearsalRuleRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!TryValidate(request, out var startTime, out var validationError))
            return BadRequest(new { error = validationError });

        var rule = await db.RecurringRehearsalRules.FirstOrDefaultAsync(r => r.Id == id && r.BandId == bandId);
        if (rule is null) return NotFound(new { error = "Not found" });

        var location = request.Location?.Trim();
        rule.DayOfWeek = request.DayOfWeek;
        rule.StartTime = startTime;
        rule.DurationMinutes = request.DurationMinutes;
        rule.Location = string.IsNullOrEmpty(location) ? null : location;
        rule.Active = request.Active;
        await db.SaveChangesAsync();

        if (rule.Active) await scheduler.GenerateRehearsalsFromRulesAsync(bandId);
        return Ok(Serialize(rule));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var rule = await db.RecurringRehearsalRules.FirstOrDefaultAsync(r => r.Id == id && r.BandId == bandId);
        if (rule is null) return Ok(new { ok = true });

        db.RecurringRehearsalRules.Remove(rule);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
