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

    // This band's own contact info, editable from Band Admin > General.
    // Same field shape as ApplicationUser's/Venue's - a US-shaped,
    // nullable, XXX-YYY-ZZZZ-formatted phone number and USPS-validatable
    // address, distinct from any Venue's address (a Venue is where the
    // band plays, this is the band's own mailing address).
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    // IANA time zone id (e.g. "America/New_York") - .NET on Linux resolves
    // TimeZoneInfo by these ids natively, no separate Windows-id mapping
    // needed. Nothing reads this yet (every date/time in the app today is
    // free text - see Gig.Date/Time) - it's a real field with nowhere to
    // apply itself until something needs to compute across time zones.
    public string? TimeZone { get; set; }

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

    // False for a "with-band" stub row - an unclaimed identity created so
    // a Gig's With-acts can reference a real Band instead of free text
    // (see GigWithBand), with no BandMembership/login/access of its own.
    // May become a real onboarded tenant later if that band signs up.
    // Migration default true, so every already-real Band (Black Secrets,
    // Attica, ...) is unaffected. SuperAdmin's band list, the band
    // switcher, and archive/unarchive UI all filter to IsOnboarded so
    // stub rows don't clutter real tenant management.
    public bool IsOnboarded { get; set; } = true;

    public ICollection<BandMembership> Memberships { get; set; } = new List<BandMembership>();
}
