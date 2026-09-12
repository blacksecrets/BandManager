using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record NotificationPreferenceItem(string Kind, bool EmailEnabled, bool InAppEnabled, int? LeadTimeDays);

/// <summary>
/// Self-service only, one row per (User, Kind) - global across every band
/// the user belongs to (see NotificationPreference.cs's doc comment for
/// why). GET always returns exactly one entry per NotificationKind, even
/// for kinds with no stored row yet, materializing sensible defaults so
/// the Profile page never has to special-case "never configured".
/// </summary>
[ApiController]
[Route("/api/notification-preferences")]
[Authorize]
public class NotificationPreferencesController(ApplicationDbContext db) : ControllerBase
{
    // Kinds a lead-time genuinely applies to - SongEditReviewed and
    // ResponsibilityChanged are event-triggered (fire the moment the
    // triggering thing happens), not something you'd want "N days before".
    private static readonly HashSet<NotificationKind> LeadTimeKinds =
        [NotificationKind.GigReminder, NotificationKind.RehearsalReminder, NotificationKind.AvailabilityReminder];

    private static readonly Dictionary<NotificationKind, int> DefaultLeadTimeDays = new()
    {
        [NotificationKind.GigReminder] = 3,
        [NotificationKind.RehearsalReminder] = 1,
        [NotificationKind.AvailabilityReminder] = 3
    };

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var existing = await db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Kind);

        // OutboundEmailCopy excluded - it's not a personal reminder a user
        // opts into, it's LoggingEmailSender's own unconditional SuperAdmin
        // safety net (see its doc comment). Listing it here would show a
        // toggle that does nothing, since that sender never consults
        // NotificationPreference for this kind.
        var result = Enum.GetValues<NotificationKind>().Where(k => k != NotificationKind.OutboundEmailCopy).Select(kind =>
        {
            var applies = LeadTimeKinds.Contains(kind);
            if (existing.TryGetValue(kind, out var pref))
            {
                return new
                {
                    kind = kind.ToString(),
                    emailEnabled = pref.EmailEnabled,
                    inAppEnabled = pref.InAppEnabled,
                    leadTimeDays = applies ? pref.LeadTimeDays : (int?)null,
                    leadTimeApplies = applies
                };
            }
            return new
            {
                kind = kind.ToString(),
                emailEnabled = true,
                inAppEnabled = true,
                leadTimeDays = applies ? DefaultLeadTimeDays[kind] : (int?)null,
                leadTimeApplies = applies
            };
        });

        return Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] List<NotificationPreferenceItem> items)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var existing = await db.NotificationPreferences.Where(p => p.UserId == userId).ToDictionaryAsync(p => p.Kind);

        foreach (var item in items)
        {
            if (!Enum.TryParse<NotificationKind>(item.Kind, out var kind)) continue;

            var leadTimeDays = LeadTimeKinds.Contains(kind)
                ? Math.Clamp(item.LeadTimeDays ?? DefaultLeadTimeDays.GetValueOrDefault(kind, 1), 0, 60)
                : (int?)null;

            if (existing.TryGetValue(kind, out var pref))
            {
                pref.EmailEnabled = item.EmailEnabled;
                pref.InAppEnabled = item.InAppEnabled;
                pref.LeadTimeDays = leadTimeDays;
            }
            else
            {
                db.NotificationPreferences.Add(new NotificationPreference
                {
                    UserId = userId.Value,
                    Kind = kind,
                    EmailEnabled = item.EmailEnabled,
                    InAppEnabled = item.InAppEnabled,
                    LeadTimeDays = leadTimeDays
                });
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
