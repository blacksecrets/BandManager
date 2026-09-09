namespace BandManager.Data.Entities;

/// <summary>
/// Join row: one piece of the band's Gear Catalog (BandGearItem) assigned
/// to one Act's Gear List. Deleting either side cascades this away - an
/// Act's Gear List entry means nothing once the Act or the catalog item
/// itself is gone.
/// </summary>
public class ActGearItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActId { get; set; }
    public Act Act { get; set; } = null!;
    public Guid BandGearItemId { get; set; }
    public BandGearItem BandGearItem { get; set; } = null!;
    public int SortOrder { get; set; }
}
