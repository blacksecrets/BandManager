namespace BandManager.Data.Entities;

/// <summary>
/// Global (not Band-scoped) key/value settings - the BandManager shell's
/// own branding (logo/background filenames for the login page), managed
/// by SuperAdmin only. Mirrors the old app's single-tenant `settings`
/// table entries (logo_filename/background_filename), which applied to
/// the whole app since it only ever had one tenant - now scoped to just
/// the platform level, since each Band gets its own branding separately
/// (Band.LogoPath/AccentColor).
/// </summary>
public class PlatformSetting
{
    public required string Key { get; set; }
    public string? Value { get; set; }
}
