using System.Security.Cryptography;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
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
public class CalendarController(ApplicationDbContext db, IActiveBandAccessor activeBand, CalendarFeedService feedService) : ControllerBase
{
    /// <summary>Every non-archived band the given user actually belongs to
    /// - the ICS feed/export cover all of them, not just whichever one
    /// happens to be "active" right now (a subscribed calendar app has no
    /// concept of that, and an export is a one-off snapshot anyway).</summary>
    private async Task<List<Guid>> MemberBandIdsAsync(Guid userId) =>
        await db.BandMemberships.Where(m => m.UserId == userId && !m.Band.IsArchived)
            .Select(m => m.BandId).ToListAsync();

    /// <summary>Get-or-create the caller's feed token and return the full
    /// subscribable URL - BandMember-gated same as the rest of this
    /// controller, even though the feed itself is anonymous once issued
    /// (the token is what protects it after that point).</summary>
    [HttpGet("feed-token")]
    public async Task<IActionResult> GetFeedToken()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var existing = await db.CalendarFeedTokens.FirstOrDefaultAsync(t => t.UserId == userId);
        if (existing is null)
        {
            existing = new CalendarFeedToken { UserId = userId.Value, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant() };
            db.CalendarFeedTokens.Add(existing);
            await db.SaveChangesAsync();
        }

        var url = $"{Request.Scheme}://{Request.Host}/api/calendar/feed/{existing.Token}.ics";
        return Ok(new { url });
    }

    [HttpGet("/api/calendar/feed/{token}.ics")]
    [AllowAnonymous]
    public async Task<IActionResult> Feed(string token)
    {
        var feedToken = await db.CalendarFeedTokens.FirstOrDefaultAsync(t => t.Token == token);
        if (feedToken is null) return NotFound();

        var bandIds = await MemberBandIdsAsync(feedToken.UserId);
        var ics = await feedService.BuildIcsAsync(bandIds);
        return Content(ics, "text/calendar");
    }

    [HttpGet("export.ics")]
    public async Task<IActionResult> Export()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var bandIds = await MemberBandIdsAsync(userId.Value);
        var ics = await feedService.BuildIcsAsync(bandIds);
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar", "bandmanager-calendar.ics");
    }

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

        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var rehearsals = await db.Rehearsals.AsNoTracking()
            .Where(r => r.BandId == bandId && r.StartsAt.Date >= fromUtc && r.StartsAt.Date <= toUtc)
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
