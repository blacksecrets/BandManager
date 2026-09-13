using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// Backs the "My next two weeks" dashboard widget: everything on the
/// active Band's calendar in the next 14 days, plus this user's own open
/// Gig Prep tasks for gigs in that window, in one call - the "in one
/// glance" the widget promises would mean three separate round trips
/// (Gigs, Rehearsals, Gig Prep) without this. Deliberately doesn't
/// materialize a Gig Prep checklist that doesn't exist yet - "open
/// tasks" only ever means tasks from a checklist this user has already
/// started (GigPrepController's own lazy-copy-on-first-open behavior),
/// not every gig's would-be default list; a gig you haven't started
/// prepping just doesn't contribute any tasks here, same as opening its
/// checklist page itself wouldn't show anything until you did.
/// </summary>
[ApiController]
[Route("/api/dashboard")]
[Authorize(Policy = "BandMember")]
public class DigestController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    [HttpGet("next-two-weeks")]
    public async Task<IActionResult> NextTwoWeeks()
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return BadRequest(new { error = "No active band selected." });
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddDays(14);
        var nowUtc = DateTime.UtcNow;
        var horizonUtc = nowUtc.AddDays(14);

        var gigs = await db.Gigs.AsNoTracking()
            .Where(g => g.BandId == bandId && !g.IsArchived && g.Date >= today && g.Date <= horizon)
            .OrderBy(g => g.Date)
            .Select(g => new { g.Ref, g.Title, g.Venue, Date = g.Date.ToString("dddd, MMMM d, yyyy"), SortDate = g.Date })
            .ToListAsync();

        var rehearsals = await db.Rehearsals.AsNoTracking()
            .Where(r => r.BandId == bandId && r.StartsAt >= nowUtc && r.StartsAt <= horizonUtc)
            .OrderBy(r => r.StartsAt)
            .Select(r => new { r.Id, Title = r.Title ?? "Rehearsal", r.Location, r.StartsAt })
            .ToListAsync();

        var openPrepTasks = await db.GigPrepChecklistItems.AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsChecked
                && i.Gig.BandId == bandId && !i.Gig.IsArchived
                && i.Gig.Date >= today && i.Gig.Date <= horizon)
            .OrderBy(i => i.Gig.Date).ThenBy(i => i.SortOrder)
            .Select(i => new { i.Id, GigRef = i.Gig.Ref, GigTitle = i.Gig.Title, i.ListType, i.Text })
            .ToListAsync();

        return Ok(new { gigs, rehearsals, openPrepTasks });
    }
}
