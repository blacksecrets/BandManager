namespace BandManager.Data.Entities;

/// <summary>
/// One FOH EQ reference card for an Act's Tech Rider - e.g. "Shure SM57
/// on Electric Guitar Amp" with a small Frequency/EQ Move/Reason table.
/// Deliberately per-Act, not a shared/global library: the right EQ
/// starting point depends on what this mic is miking in THIS act's own
/// setup (a guitar cab vs. a snare vs. a vocal), not the mic model alone -
/// the same SM57 can carry different notes on two different Acts. See the
/// plan's "Key facts" section for the full reasoning (a plan-review
/// correction from an earlier, wrongly-global design).
///
/// FrequencyRowsJson holds the small Frequency/EQ Move/Reason table as a
/// JSON array (e.g. [{"frequency":"100-150Hz","eqMove":"Cut","reason":
/// "Boxiness"}]) rather than a separate child table - a handful of rows
/// tied 1:1 to their parent note, never queried independently, doesn't
/// earn its own table.
/// </summary>
public class TechRiderMicEqNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActId { get; set; }
    public Act Act { get; set; } = null!;

    public required string MicModel { get; set; }
    public string? Context { get; set; }
    public required string FrequencyRowsJson { get; set; }
    public string? GeneralNotes { get; set; }
    public int SortOrder { get; set; }
}
