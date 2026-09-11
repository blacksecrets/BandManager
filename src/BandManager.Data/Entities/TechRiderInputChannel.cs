namespace BandManager.Data.Entities;

/// <summary>
/// One row of an Act's Tech Rider Input/Mic Splitter Channel List - the
/// structured version of the real Tech Rider PDF's channel table (channel
/// #, source, mic recommendation, "if venue provides mics & stands").
/// Per-Act, not shared, since two Acts on the same band can have entirely
/// different input lists.
/// </summary>
public class TechRiderInputChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActId { get; set; }
    public Act Act { get; set; } = null!;

    public int ChannelNumber { get; set; }
    public required string Source { get; set; }
    public string? MicRecommendation { get; set; }

    // "Venue" | "Band" | "Either" - free-ish string rather than an enum,
    // matching this app's established convention for small closed-ish
    // picklists shown as a <select> client-side (see Gear.Type/GearTypes.cs).
    public string? ProvidedBy { get; set; }
    public string? PositioningNotes { get; set; }

    public int SortOrder { get; set; }
}
