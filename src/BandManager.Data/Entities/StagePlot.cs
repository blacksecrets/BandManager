namespace BandManager.Data.Entities;

/// <summary>
/// One stage plot per Act - created lazily the first time an item is
/// placed (see StagePlotController), not alongside the Act itself, since
/// most Acts start with an empty plot and there's nothing to store until
/// then.
/// </summary>
public class StagePlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActId { get; set; }
    public Act Act { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StagePlotItem> Items { get; set; } = new List<StagePlotItem>();
}

/// <summary>
/// One piece of gear placed on a Stage Plot - X/Y are fractional (0.0-1.0
/// across the stage diagram), same convention as the Flyer editor's field
/// positions (see flyerEditor.js). VisibleId is the sequential, per-plot
/// number shown as this item's badge on the diagram and referenced in the
/// Tech Rider's gear legend/notes (computed at render time by walking
/// items in VisibleId order and reading the linked BandGearItem's own
/// Type/Make/Model/dimensions/weight - never stored redundantly here, see
/// the plan's "derived data doesn't get its own table" reasoning).
/// </summary>
public class StagePlotItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StagePlotId { get; set; }
    public StagePlot StagePlot { get; set; } = null!;
    public Guid BandGearItemId { get; set; }
    public BandGearItem BandGearItem { get; set; } = null!;

    public int VisibleId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
}
