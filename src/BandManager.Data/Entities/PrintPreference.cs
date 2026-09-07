namespace BandManager.Data.Entities;

public enum PrintLineSpacing { None = 0, Single = 1, Double = 2 }

/// <summary>
/// One user's saved formatting for printed setlists - global across
/// every band they're in (a personal habit, same reasoning as
/// NotificationPreference not being band-scoped). Exactly one row per
/// user; PrintPreferencesController materializes the documented default
/// (Arial, Bold, 14pt, double-spaced, numbered) for anyone who's never
/// saved one, so the print view never has to special-case "unconfigured".
/// </summary>
public class PrintPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string FontFamily { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public int FontSizePt { get; set; }
    public PrintLineSpacing LineSpacing { get; set; }
    public bool NumberLines { get; set; } = true;
}
