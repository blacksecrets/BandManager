using BandManager.Data;
using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Services;

/// <summary>
/// Per-band reminder generation - called from the same 30-minute
/// background loop as Scheduler.GenerateAllAsync (see
/// CadenceGenerationBackgroundService). For each band member, checks
/// their own NotificationPreference (falling back to sensible defaults
/// for anyone who's never visited the Profile page's notification
/// settings - see NotificationPreferencesController) and, for anything
/// due within their configured lead time, writes an in-app Notification
/// and/or sends an email. Idempotent the same way Scheduler.cs is: an
/// existence check (UserId, Kind, GigId/RehearsalId) before writing, so
/// re-running the loop never double-reminds anyone for the same event.
/// </summary>
public class NotificationReminderService(ApplicationDbContext db, IEmailSender emailSender)
{
    private static readonly Dictionary<NotificationKind, int> DefaultLeadTimeDays = new()
    {
        [NotificationKind.GigReminder] = 3,
        [NotificationKind.RehearsalReminder] = 1,
        [NotificationKind.AvailabilityReminder] = 3
    };

    public async Task GenerateDueRemindersAsync(Guid bandId)
    {
        var memberUserIds = await db.BandMemberships.Where(m => m.BandId == bandId).Select(m => m.UserId).ToListAsync();
        if (memberUserIds.Count == 0) return;

        var users = await db.Users.Where(u => memberUserIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var prefsByUserKind = await db.NotificationPreferences
            .Where(p => memberUserIds.Contains(p.UserId))
            .ToDictionaryAsync(p => (p.UserId, p.Kind));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var gigs = await db.Gigs.Where(g => g.BandId == bandId && !g.IsArchived && g.Date >= today).ToListAsync();
        var upcomingGigs = gigs.Select(g => (Gig: g, Date: g.Date)).ToList();

        var rehearsals = await db.Rehearsals.Where(r => r.BandId == bandId).ToListAsync();
        var upcomingRehearsals = rehearsals
            .Select(r => (Rehearsal: r, Date: DateOnly.FromDateTime(r.StartsAt)))
            .Where(x => x.Date >= today)
            .ToList();

        foreach (var userId in memberUserIds)
        {
            if (!users.TryGetValue(userId, out var user)) continue;

            await RemindGigsAsync(userId, user, today, upcomingGigs, prefsByUserKind);
            await RemindRehearsalsAsync(userId, user, today, upcomingRehearsals, prefsByUserKind);
            await RemindMissingAvailabilityAsync(userId, user, today, upcomingGigs, upcomingRehearsals, prefsByUserKind);
        }
    }

    private (bool Email, bool InApp, int LeadDays)? ResolvePreference(
        Guid userId, NotificationKind kind, Dictionary<(Guid, NotificationKind), NotificationPreference> prefsByUserKind)
    {
        if (prefsByUserKind.TryGetValue((userId, kind), out var pref))
        {
            if (!pref.EmailEnabled && !pref.InAppEnabled) return null;
            return (pref.EmailEnabled, pref.InAppEnabled, pref.LeadTimeDays ?? DefaultLeadTimeDays[kind]);
        }
        // No row yet - matches NotificationPreferencesController's GET
        // defaults (both channels on, standard lead time), so a member
        // who's never opened notification settings still gets reminded.
        return (true, true, DefaultLeadTimeDays[kind]);
    }

    private async Task NotifyAsync(ApplicationUser user, NotificationKind kind, string message, bool email, bool inApp, Guid? gigId = null, Guid? rehearsalId = null, Guid? bandId = null)
    {
        if (inApp)
        {
            db.Notifications.Add(new Notification { UserId = user.Id, Message = message, Kind = kind, GigId = gigId, RehearsalId = rehearsalId, BandId = bandId });
        }
        if (email && !string.IsNullOrWhiteSpace(user.Email))
        {
            await emailSender.SendAsync(user.Email, "BandManager reminder", $"<p>{System.Net.WebUtility.HtmlEncode(message)}</p>");
        }
    }

    private async Task RemindGigsAsync(
        Guid userId, ApplicationUser user, DateOnly today, List<(Gig Gig, DateOnly Date)> upcomingGigs,
        Dictionary<(Guid, NotificationKind), NotificationPreference> prefsByUserKind)
    {
        var resolved = ResolvePreference(userId, NotificationKind.GigReminder, prefsByUserKind);
        if (resolved is not { } p) return;

        foreach (var (gig, date) in upcomingGigs)
        {
            if ((date.DayNumber - today.DayNumber) != p.LeadDays) continue;
            if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.Kind == NotificationKind.GigReminder && n.GigId == gig.Id))
                continue;

            await NotifyAsync(user, NotificationKind.GigReminder, $"Upcoming gig: {gig.Title} on {GigDateTimeFormatting.FormatDate(gig.Date)}.", p.Email, p.InApp, gigId: gig.Id, bandId: gig.BandId);
        }
        await db.SaveChangesAsync();
    }

    private async Task RemindRehearsalsAsync(
        Guid userId, ApplicationUser user, DateOnly today, List<(Rehearsal Rehearsal, DateOnly Date)> upcomingRehearsals,
        Dictionary<(Guid, NotificationKind), NotificationPreference> prefsByUserKind)
    {
        var resolved = ResolvePreference(userId, NotificationKind.RehearsalReminder, prefsByUserKind);
        if (resolved is not { } p) return;

        foreach (var (rehearsal, date) in upcomingRehearsals)
        {
            if ((date.DayNumber - today.DayNumber) != p.LeadDays) continue;
            if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.Kind == NotificationKind.RehearsalReminder && n.RehearsalId == rehearsal.Id))
                continue;

            var when = rehearsal.StartsAt.ToString("dddd h:mm tt");
            await NotifyAsync(user, NotificationKind.RehearsalReminder, $"Upcoming rehearsal: {rehearsal.Title ?? "Rehearsal"} - {when}.", p.Email, p.InApp, rehearsalId: rehearsal.Id, bandId: rehearsal.BandId);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Nudges a member to set their availability for an upcoming
    /// gig/rehearsal they haven't responded to yet - not a reminder that
    /// something is happening, a reminder that THEY haven't said whether
    /// they can make it.</summary>
    private async Task RemindMissingAvailabilityAsync(
        Guid userId, ApplicationUser user, DateOnly today,
        List<(Gig Gig, DateOnly Date)> upcomingGigs, List<(Rehearsal Rehearsal, DateOnly Date)> upcomingRehearsals,
        Dictionary<(Guid, NotificationKind), NotificationPreference> prefsByUserKind)
    {
        var resolved = ResolvePreference(userId, NotificationKind.AvailabilityReminder, prefsByUserKind);
        if (resolved is not { } p) return;

        var relevantDates = upcomingGigs.Select(x => x.Date).Concat(upcomingRehearsals.Select(x => x.Date))
            .Where(d => d.DayNumber - today.DayNumber is var diff && diff >= 0 && diff <= p.LeadDays)
            .Distinct()
            .ToList();
        if (relevantDates.Count == 0) return;

        var answeredDates = await db.Availabilities
            .Where(a => a.UserId == userId && relevantDates.Contains(a.Date))
            .Select(a => a.Date)
            .ToListAsync();
        var answeredSet = answeredDates.ToHashSet();

        foreach (var (gig, date) in upcomingGigs)
        {
            if (!relevantDates.Contains(date) || answeredSet.Contains(date)) continue;
            if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.Kind == NotificationKind.AvailabilityReminder && n.GigId == gig.Id))
                continue;
            await NotifyAsync(user, NotificationKind.AvailabilityReminder, $"Set your availability for {gig.Title} on {GigDateTimeFormatting.FormatDate(gig.Date)}.", p.Email, p.InApp, gigId: gig.Id, bandId: gig.BandId);
        }
        foreach (var (rehearsal, date) in upcomingRehearsals)
        {
            if (!relevantDates.Contains(date) || answeredSet.Contains(date)) continue;
            if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.Kind == NotificationKind.AvailabilityReminder && n.RehearsalId == rehearsal.Id))
                continue;
            var when = rehearsal.StartsAt.ToString("dddd");
            await NotifyAsync(user, NotificationKind.AvailabilityReminder, $"Set your availability for the {when} rehearsal.", p.Email, p.InApp, rehearsalId: rehearsal.Id, bandId: rehearsal.BandId);
        }
        await db.SaveChangesAsync();
    }
}
