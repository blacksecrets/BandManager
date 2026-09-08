namespace BandManager.Data.Entities;

/// <summary>
/// A font a SuperAdmin has uploaded to grow the flyer editor's font list
/// beyond the 7 bundled ones - platform-wide, not band-scoped, same
/// "SuperAdmin sets it up once for everyone" pattern as OAuth app
/// registration. The actual font file lives under data/fonts/{Id}{Extension}
/// (persistent bind mount, served via Program.cs's authenticated
/// /custom-fonts static route) - never wwwroot, since wwwroot is baked
/// into the image and wiped on redeploy. A flyer field references one of
/// these via FlyerFieldDef.FontFamily set to $"custom-{Id}" - see
/// FlyerFonts.cs/FlyerRenderer.cs.
/// </summary>
public class CustomFlyerFont
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";

    // Extension only (".ttf" or ".otf") - the file on disk is named
    // {Id}{Extension}, so this is enough to reconstruct both the physical
    // path and the public /custom-fonts URL without storing either.
    public string Extension { get; set; } = ".ttf";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
