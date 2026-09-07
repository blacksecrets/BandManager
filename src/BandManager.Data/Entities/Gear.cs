namespace BandManager.Data.Entities;

/// <summary>
/// One item in a band member's personal gear inventory - not band-scoped
/// (the same person's gear list follows them across every band they're
/// in, the same way their profile does). Type is a free-ish string
/// rather than an enum (see GearTypes.cs for the curated picklist +
/// each type's default Setting names) so a new category never needs a
/// migration to add. Specs are all optional/nullable, inches and pounds -
/// nothing here is required beyond a type, so "just save it" (no specs,
/// no settings) always works, per the explicit fallback requirement.
/// </summary>
public class Gear
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public required string Type { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }

    public decimal? LengthInches { get; set; }
    public decimal? WidthInches { get; set; }
    public decimal? DepthInches { get; set; }
    public decimal? WeightPounds { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GearSetting> Settings { get; set; } = new List<GearSetting>();
}

/// <summary>One named setting on one piece of Gear ("Gain", "Reverb",
/// "Polar Pattern"...) - Value starts blank when auto-populated from
/// GearTypes' per-type template at creation time (there's no live lookup
/// of "the correct value", see GearTypes.cs's doc comment for why), and
/// the user can add/edit/delete rows freely afterward so the list always
/// reflects their actual gear, not just the starting template.</summary>
public class GearSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GearId { get; set; }
    public Gear Gear { get; set; } = null!;
    public required string Name { get; set; }
    public string? Value { get; set; }
    public int SortOrder { get; set; }
}
