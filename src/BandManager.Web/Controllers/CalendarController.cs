using BandManager.Data;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// The shared Calendar view - every band member/admin sees the same
/// merged list of gigs and rehearsals for a date range. Gigs/rehearsals
/// are both real DB rows now (see Gig.cs, Rehearsal.cs), so this is a
/// plain two-table query - no live site fetch involved, unlike the old
/// GigsSource-backed model.
/// </summary>
[ApiController]
[Route("/api/calendar")]
[Authorize(Policy = "BandMember")]
public class CalendarController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    // Gig.Date is a free-text display string ("Friday, October 3, 2026"),
    // not a real date column - same parse-on-read approach
    // Scheduler.TryParseGigDate already uses for the same reason (it's
    // what the site's own calendar.js has always stored).
    private static DateOnly? TryParseGigDate(string date) =>
        DateTime.TryParse(date, out var d) ? DateOnly.FromDateTime(d) : null;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (to < from) return BadRequest(new { error = "'to' must be on or after 'from'." });

        var gigs = await db.Gigs.AsNoTracking().Where(g => g.BandId == bandId).ToListAsync();
        var entries = new List<(DateOnly SortDate, object Entry)>();

        foreach (var gig in gigs)
        {
            var parsed = TryParseGigDate(gig.Date);
            if (parsed is not { } date || date < from || date > to) continue;
            entries.Add((date, new
            {
                kind = "gig",
                id = gig.Ref,
                title = gig.Title,
                date = date.ToString("yyyy-MM-dd"),
                displayDate = gig.Date,
                time = gig.Time,
                venue = gig.Venue,
                flyerMain = gig.FlyerMain
            }));
        }

        var rehearsals = await db.Rehearsals.AsNoTracking()
            .Where(r => r.BandId == bandId && r.StartsAt.Date >= from.ToDateTime(TimeOnly.MinValue) && r.StartsAt.Date <= to.ToDateTime(TimeOnly.MinValue))
            .ToListAsync();
        foreach (var r in rehearsals)
        {
            entries.Add((DateOnly.FromDateTime(r.StartsAt), new
            {
                kind = "rehearsal",
                id = r.Id,
                title = r.Title,
                date = r.StartsAt.ToString("yyyy-MM-dd"),
                startsAt = r.StartsAt,
                endsAt = r.EndsAt,
                location = r.Location
            }));
        }

        return Ok(entries.OrderBy(e => e.SortDate).Select(e => e.Entry));
    }
}
