namespace BandManager.Data.Entities;

/// <summary>
/// A tenant. Everything band-specific (Setup, Cadence, Catalog, branding,
/// schedule) hangs off a BandId; reference data (Platforms, ContentTypes)
/// stays global and unscoped since it's identical for every Band.
/// </summary>
public class Band
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }

    // Website platform config - replaces the old app's hardcoded
    // OWNER/REPO constants (src/github.js) and CALENDAR_URL constant
    // (src/gigsSource.js), now per-band instead of global.
    public string? SiteBaseUrl { get; set; }
    public string? GitHubOwner { get; set; }
    public string? GitHubRepo { get; set; }

    // This Band's own branding (distinct from the platform-level
    // BandManager shell branding in PlatformBranding, which only applies
    // to the Login page and SuperAdmin screens).
    public string? LogoPath { get; set; }
    public string? AccentColor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BandMembership> Memberships { get; set; } = new List<BandMembership>();
}
