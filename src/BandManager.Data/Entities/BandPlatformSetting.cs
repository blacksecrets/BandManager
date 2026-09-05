namespace BandManager.Data.Entities;

public enum PlatformMode
{
    Manual = 0, // no credentials - "Fart it out" just records that you posted it yourself
    Wired = 1   // a real Account with credentials exists for this platform
}

/// <summary>
/// Dashboard visibility vs. wiring - a distinction the old single-tenant
/// schema didn't need (it just had platforms.supports_posting as a global
/// flag). A BandAdmin can show a platform on the Dashboard in pure-manual
/// mode without ever touching Setup's credential form, or wire it for
/// real via an Account row - independently, per Band.
/// </summary>
public class BandPlatformSetting
{
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;

    public bool ShowOnDashboard { get; set; }
    public PlatformMode Mode { get; set; } = PlatformMode.Manual;
}

/// <summary>
/// Band-scoped key/value settings - Test Mode config, the Meta App ID/
/// Secret pair (shared by both the Facebook and Instagram Accounts under
/// one Band), etc. Mirrors the old app's global `settings` table, now
/// per-band instead of global.
/// </summary>
public class BandSetting
{
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string Key { get; set; }
    public string? Value { get; set; }
}
