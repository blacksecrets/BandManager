namespace BandManager.Data;

/// <summary>
/// Gig.Date/DoorsTime/OpenerTime/HeadlinerTime are real DateOnly/TimeOnly
/// columns (see Gig.cs), but every existing client - API responses, the
/// connected site's calendar.js, notification text, ICS export - still
/// speaks the same display strings as before this conversion ("Friday,
/// October 3, 2026" / "7:00 PM"). These helpers are the one place that
/// format is defined, so every caller across both projects formats/parses
/// the same way instead of each reinventing it slightly differently.
/// </summary>
public static class GigDateTimeFormatting
{
    public const string DateDisplayFormat = "dddd, MMMM d, yyyy";
    public const string TimeDisplayFormat = "h:mm tt";

    public static string FormatDate(DateOnly d) => d.ToString(DateDisplayFormat);
    public static string? FormatTime(TimeOnly? t) => t?.ToString(TimeDisplayFormat);

    public static TimeOnly? ParseTimeOrNull(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && TimeOnly.TryParse(raw, out var t) ? t : null;
}
