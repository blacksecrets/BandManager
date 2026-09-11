namespace BandManager.Data.Entities;

/// <summary>
/// One row of an Act's Tech Rider Monitor Mix table - which stage
/// position gets what in their monitor (e.g. "Lead Vocal: vocals up,
/// light kick/bass"). Per-Act, same shape/pattern as
/// TechRiderInputChannel.cs.
/// </summary>
public class TechRiderMonitorMix
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActId { get; set; }
    public Act Act { get; set; } = null!;

    public required string Position { get; set; }
    public required string MixDescription { get; set; }
    public int SortOrder { get; set; }
}
