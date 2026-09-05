namespace BandManager.Data.Entities;

/// <summary>
/// Global, unscoped reference data - what's possible, not what's
/// configured. Identical for every Band, seeded once (see
/// PlatformSeedData), not user-editable. Mirrors the old app's
/// src/seedPlatforms.js PLATFORMS array.
/// </summary>
public class Platform
{
    public required string Id { get; set; } // e.g. "facebook", matches the old app's string ids
    public required string DisplayName { get; set; }
    public bool SupportsPosting { get; set; }
    public List<string>? CredentialFields { get; set; }
    public string? SetupInstructions { get; set; } // HTML, rendered as-is by the frontend
    public int SortOrder { get; set; }
}
