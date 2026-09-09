namespace BandManager.Data.Entities;

/// <summary>
/// A band's maintained gear catalog - a separate table from a member's
/// personal Gear (see Gear.cs), even though the shapes match, because a
/// piece of gear can be listed here without belonging to any one member's
/// own inventory (OwnerUserId null = a band asset the group pitched in
/// for together), and because copying a member's Gear row in here (see
/// BandGearController's from-member endpoint) is a one-time snapshot, not
/// a live link - editing or deleting the original personal listing never
/// touches this row, and vice versa. This is the list Band Admin selects
/// from when building an Act's Gear List and, from there, its Stage Plot
/// and Tech Rider.
/// </summary>
public class BandGearItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    // Null means a band asset - something the group jointly owns or
    // pitched in for, rather than one member's own piece of gear.
    public Guid? OwnerUserId { get; set; }
    public ApplicationUser? OwnerUser { get; set; }

    public required string Type { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }

    public decimal? LengthInches { get; set; }
    public decimal? WidthInches { get; set; }
    public decimal? DepthInches { get; set; }
    public decimal? WeightPounds { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
