using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveRehearsalRequest(
    string? Title, string? Location, DateTime StartsAt, DateTime EndsAt,
    string? GigRef = null, string? FloatingSetlistRef = null, string? Agenda = null, string? Notes = null);

/// <summary>
/// Full CRUD open to any BandMember, per the explicit "all band members
/// can manage rehearsal entries" request - unlike most other write paths
/// in this app, there's no BandAdmin gate here.
/// </summary>
[ApiController]
[Route("/api/rehearsals")]
[Authorize(Policy = "BandMember")]
public class RehearsalController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    GoogleCalendarPushService googleCalendarPush,
    OutlookCalendarPushService outlookCalendarPush,
    ILogger<RehearsalController> logger) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    // Same resource-scoped check AvailabilityController already
    // establishes for "most of this write is open to any Band Member, but
    // one part of it needs BandAdmin" - Agenda/Notes below are the part
    // that does.
    private async Task<bool> IsBandAdminAsync(Guid bandId, Guid userId)
    {
        if (User.IsSuperAdmin()) return true;
        var membership = await db.BandMemberships.AsNoTracking().FirstOrDefaultAsync(m => m.BandId == bandId && m.UserId == userId);
        return membership?.Role == BandRole.BandAdmin;
    }

    // Same best-effort, never-block-the-write shape as
    // GigsController.PushGigToExternalCalendarsAsync - see its doc
    // comment for why this is safe to call unconditionally right now.
    private async Task PushRehearsalToExternalCalendarsAsync(Rehearsal rehearsal)
    {
        try { await googleCalendarPush.PushRehearsalAsync(rehearsal); }
        catch (Exception ex) { logger.LogWarning(ex, "Google Calendar push failed for rehearsal {RehearsalId}", rehearsal.Id); }
        try { await outlookCalendarPush.PushRehearsalAsync(rehearsal); }
        catch (Exception ex) { logger.LogWarning(ex, "Outlook Calendar push failed for rehearsal {RehearsalId}", rehearsal.Id); }
    }

    private async Task DeleteRehearsalFromExternalCalendarsAsync(Guid bandId, Guid rehearsalId)
    {
        try { await googleCalendarPush.DeleteRehearsalAsync(bandId, rehearsalId); }
        catch (Exception ex) { logger.LogWarning(ex, "Google Calendar delete failed for rehearsal {RehearsalId}", rehearsalId); }
        try { await outlookCalendarPush.DeleteRehearsalAsync(bandId, rehearsalId); }
        catch (Exception ex) { logger.LogWarning(ex, "Outlook Calendar delete failed for rehearsal {RehearsalId}", rehearsalId); }
    }

    private static object Serialize(Rehearsal r, string? gigTitle, string? floatingSetlistName) => new
    {
        id = r.Id,
        title = r.Title,
        location = r.Location,
        startsAt = r.StartsAt,
        endsAt = r.EndsAt,
        recurringRehearsalRuleId = r.RecurringRehearsalRuleId,
        createdByUserId = r.CreatedByUserId,
        gigRef = r.GigRef,
        gigTitle,
        floatingSetlistRef = r.FloatingSetlistRef,
        floatingSetlistName,
        agenda = r.Agenda,
        notes = r.Notes
    };

    // Batch-resolves display names for whichever ref each rehearsal has
    // set - same dictionary-lookup shape GigSetsController.ListGigs
    // already uses, so a date-range list of rehearsals costs 0-2 extra
    // queries total, not one per row.
    private async Task<(Dictionary<string, string> GigTitles, Dictionary<string, string> FloatingNames)> ResolveRefNamesAsync(Guid bandId, IEnumerable<Rehearsal> rehearsals)
    {
        var gigRefs = rehearsals.Where(r => r.GigRef != null).Select(r => r.GigRef!).Distinct().ToList();
        var floatingRefs = rehearsals.Where(r => r.FloatingSetlistRef != null).Select(r => r.FloatingSetlistRef!).Distinct().ToList();

        var gigTitles = gigRefs.Count == 0
            ? []
            : await db.Gigs.AsNoTracking().Where(g => g.BandId == bandId && gigRefs.Contains(g.Ref)).ToDictionaryAsync(g => g.Ref, g => g.Title);
        var floatingNames = floatingRefs.Count == 0
            ? []
            : await db.GigSets.AsNoTracking().Where(s => s.BandId == bandId && floatingRefs.Contains(s.GigRef) && s.IsFloating)
                .ToDictionaryAsync(s => s.GigRef, s => s.Name ?? "Untitled setlist");
        return (gigTitles, floatingNames);
    }

    // A gig link and a floating-setlist link are mutually exclusive - if
    // the client somehow sent both, the gig link wins (a real gig's own
    // setlist is the more concrete choice).
    private static void ApplyGigAndSetlistRefs(Rehearsal rehearsal, SaveRehearsalRequest request)
    {
        var gigRef = string.IsNullOrWhiteSpace(request.GigRef) ? null : request.GigRef.Trim();
        var floatingRef = string.IsNullOrWhiteSpace(request.FloatingSetlistRef) ? null : request.FloatingSetlistRef.Trim();
        if (gigRef is not null) floatingRef = null;
        rehearsal.GigRef = gigRef;
        rehearsal.FloatingSetlistRef = floatingRef;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (to < from) return BadRequest(new { error = "'to' must be on or after 'from'." });

        var rows = await db.Rehearsals.AsNoTracking()
            .Where(r => r.BandId == bandId && r.StartsAt < to && r.EndsAt > from)
            .OrderBy(r => r.StartsAt)
            .ToListAsync();
        var (gigTitles, floatingNames) = await ResolveRefNamesAsync(bandId, rows);
        return Ok(rows.Select(r => Serialize(r,
            r.GigRef is { } gr ? gigTitles.GetValueOrDefault(gr) : null,
            r.FloatingSetlistRef is { } fr ? floatingNames.GetValueOrDefault(fr) : null)));
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
        ApplyGigAndSetlistRefs(rehearsal, request);
        if (await IsBandAdminAsync(bandId, userId.Value))
        {
            rehearsal.Agenda = string.IsNullOrWhiteSpace(request.Agenda) ? null : request.Agenda.Trim();
            rehearsal.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        }
        db.Rehearsals.Add(rehearsal);
        await db.SaveChangesAsync();
        await PushRehearsalToExternalCalendarsAsync(rehearsal);
        var (gigTitles, floatingNames) = await ResolveRefNamesAsync(bandId, [rehearsal]);
        return Ok(Serialize(rehearsal,
            rehearsal.GigRef is { } grc ? gigTitles.GetValueOrDefault(grc) : null,
            rehearsal.FloatingSetlistRef is { } frc ? floatingNames.GetValueOrDefault(frc) : null));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRehearsalRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (request.EndsAt <= request.StartsAt) return BadRequest(new { error = "End time must be after start time." });

        var rehearsal = await db.Rehearsals.FirstOrDefaultAsync(r => r.Id == id && r.BandId == bandId);
        if (rehearsal is null) return NotFound(new { error = "Not found" });

        var userId = User.GetUserId();
        var title = request.Title?.Trim();
        var location = request.Location?.Trim();
        rehearsal.Title = string.IsNullOrEmpty(title) ? null : title;
        rehearsal.Location = string.IsNullOrEmpty(location) ? null : location;
        rehearsal.StartsAt = DateTime.SpecifyKind(request.StartsAt, DateTimeKind.Utc);
        rehearsal.EndsAt = DateTime.SpecifyKind(request.EndsAt, DateTimeKind.Utc);
        ApplyGigAndSetlistRefs(rehearsal, request);
        if (userId is not null && await IsBandAdminAsync(bandId, userId.Value))
        {
            rehearsal.Agenda = string.IsNullOrWhiteSpace(request.Agenda) ? null : request.Agenda.Trim();
            rehearsal.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        }
        await db.SaveChangesAsync();
        await PushRehearsalToExternalCalendarsAsync(rehearsal);
        var (gigTitles, floatingNames) = await ResolveRefNamesAsync(bandId, [rehearsal]);
        return Ok(Serialize(rehearsal,
            rehearsal.GigRef is { } gru ? gigTitles.GetValueOrDefault(gru) : null,
            rehearsal.FloatingSetlistRef is { } fru ? floatingNames.GetValueOrDefault(fru) : null));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var rehearsal = await db.Rehearsals.FirstOrDefaultAsync(r => r.Id == id && r.BandId == bandId);
        if (rehearsal is null) return Ok(new { ok = true });

        db.Rehearsals.Remove(rehearsal);
        await db.SaveChangesAsync();
        await DeleteRehearsalFromExternalCalendarsAsync(bandId, id);
        return Ok(new { ok = true });
    }
}
