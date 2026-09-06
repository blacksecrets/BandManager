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
    // BandManager shell branding in PlatformSettings, which only applies
    // when no Band is selected). Applied everywhere once a Band is active -
    // see branding.js.
    public string? LogoPath { get; set; }
    public string? BackgroundPath { get; set; }
    public string? FaviconPath { get; set; }
    public string? AccentColor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft-delete: archiving a Band (SuperAdmin only) deactivates it for
    // everyone without touching any other Band a member belongs to -
    // BandAccessCheck and the band switcher both exclude archived Bands, so
    // BandAdmins/Users simply can no longer select it, while their
    // memberships elsewhere are untouched. Never hard-deleted: the row (and
    // its schedule/catalog/credentials history) stays for the record.
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public ICollection<BandMembership> Memberships { get; set; } = new List<BandMembership>();
}
