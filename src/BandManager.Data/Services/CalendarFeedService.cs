using System.Text;
using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data.Services;

/// <summary>
/// Builds a plain RFC 5545 ICS document from a set of Bands' Gigs/
/// Rehearsals - no external library, since the format is simple flat text.
/// Serves both the anonymous subscription feed (CalendarFeedToken) and the
/// authenticated one-off file download - same content either way, only
/// the caller/auth differs.
/// </summary>
public class CalendarFeedService(ApplicationDbContext db)
{
    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>Gigs have only a free-text Time field (not a real time
    /// column - see Gig.cs), so a gig always renders as an all-day ICS
    /// event; the time, if any, is folded into the event summary instead
    /// of attempted as a parsed DTSTART time.</summary>
    private static void AppendGigEvent(StringBuilder sb, Gig gig, DateOnly date)
    {
        var summary = gig.Time is { Length: > 0 } ? $"{gig.Title} ({gig.Time})" : gig.Title;
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:gig-{gig.Id}@bandmanager\r\n");
        sb.Append($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}\r\n");
        sb.Append($"DTSTART;VALUE=DATE:{date:yyyyMMdd}\r\n");
        sb.Append($"DTEND;VALUE=DATE:{date.AddDays(1):yyyyMMdd}\r\n");
        sb.Append($"SUMMARY:{Escape(summary)}\r\n");
        if (!string.IsNullOrWhiteSpace(gig.Venue)) sb.Append($"LOCATION:{Escape(gig.Venue)}\r\n");
        sb.Append("END:VEVENT\r\n");
    }

    // Rehearsal.StartsAt/EndsAt are always stored with DateTimeKind.Utc
    // (see RehearsalController/Scheduler.GenerateRehearsalsFromRulesAsync) -
    // safe to render with a trailing Z.
    private static void AppendRehearsalEvent(StringBuilder sb, Rehearsal rehearsal)
    {
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:rehearsal-{rehearsal.Id}@bandmanager\r\n");
        sb.Append($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}\r\n");
        sb.Append($"DTSTART:{rehearsal.StartsAt:yyyyMMddTHHmmssZ}\r\n");
        sb.Append($"DTEND:{rehearsal.EndsAt:yyyyMMddTHHmmssZ}\r\n");
        sb.Append($"SUMMARY:{Escape(rehearsal.Title ?? "Rehearsal")}\r\n");
        if (!string.IsNullOrWhiteSpace(rehearsal.Location)) sb.Append($"LOCATION:{Escape(rehearsal.Location)}\r\n");
        sb.Append("END:VEVENT\r\n");
    }

    public async Task<string> BuildIcsAsync(List<Guid> bandIds)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//BandManager//Calendar//EN\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:PUBLISH\r\n");
        sb.Append("X-WR-CALNAME:BandManager\r\n");

        var gigs = await db.Gigs.AsNoTracking().Where(g => bandIds.Contains(g.BandId)).ToListAsync();
        foreach (var gig in gigs) AppendGigEvent(sb, gig, gig.Date);

        var rehearsals = await db.Rehearsals.AsNoTracking().Where(r => bandIds.Contains(r.BandId)).ToListAsync();
        foreach (var rehearsal in rehearsals) AppendRehearsalEvent(sb, rehearsal);

        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }
}
