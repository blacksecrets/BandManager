using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandManager.Web.Controllers;

public record UpdateAccentColorRequest(string Color);

/// <summary>
/// The active Band's own branding (logo/background/favicon/accent color) -
/// distinct from ProfileController's platform-wide branding, which only
/// applies when no Band is selected (see branding.js). BandAdmin of the
/// active Band, or SuperAdmin regardless - same as every other
/// BandAdmin-policy route.
/// </summary>
[ApiController]
[Route("/api/band-admin")]
[Authorize(Policy = "BandAdmin")]
public class BandAdminController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    IWebHostEnvironment env) : ControllerBase
{
    private const long MaxAssetBytes = 20 * 1024 * 1024;
    private static readonly string[] AssetTypes = ["logo", "background", "favicon"];

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static string? GetPath(Band band, string type) => type switch
    {
        "logo" => band.LogoPath,
        "background" => band.BackgroundPath,
        "favicon" => band.FaviconPath,
        _ => null
    };

    private static void SetPath(Band band, string type, string? value)
    {
        switch (type)
        {
            case "logo": band.LogoPath = value; break;
            case "background": band.BackgroundPath = value; break;
            case "favicon": band.FaviconPath = value; break;
        }
    }

    // Anonymous (overrides the controller-level BandAdmin policy) - unlike
    // every other action here, this is read by branding.js on every page
    // for every viewer (any Band role, or even pre-login), the same way
    // ProfileController's platform-branding GetBranding is public. Nothing
    // sensitive lives in a logo/background/favicon path.
    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBranding()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.FindAsync(bandId);
        if (band is null) return NotFound();
        return Ok(new
        {
            logoUrl = band.LogoPath is { } logo ? $"/band-branding/{logo}" : null,
            backgroundUrl = band.BackgroundPath is { } bg ? $"/band-branding/{bg}" : null,
            faviconUrl = band.FaviconPath is { } fav ? $"/band-branding/{fav}" : null,
            accentColor = band.AccentColor
        });
    }

    [HttpPost("branding/{type}")]
    [RequestSizeLimit(MaxAssetBytes)]
    public async Task<IActionResult> UploadAsset(string type)
    {
        if (!AssetTypes.Contains(type)) return BadRequest(new { error = "Invalid branding type" });
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided" });
        if (!(file.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Expected an image file, got {file.ContentType}" });

        var band = await db.Bands.FindAsync(bandId);
        if (band is null) return NotFound();

        var brandingRoot = Path.Combine(env.ContentRootPath, "data", "band-branding");
        var bandDir = Path.Combine(brandingRoot, bandId.ToString());
        Directory.CreateDirectory(bandDir);

        if (GetPath(band, type) is { } existing)
        {
            try { System.IO.File.Delete(Path.Combine(brandingRoot, existing)); } catch { /* best-effort */ }
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".png";
        var fileName = $"{type}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(bandDir, fileName)))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = $"{bandId}/{fileName}";
        SetPath(band, type, relativePath);
        await db.SaveChangesAsync();
        return Ok(new { ok = true, url = $"/band-branding/{relativePath}" });
    }

    [HttpDelete("branding/{type}")]
    public async Task<IActionResult> DeleteAsset(string type)
    {
        if (!AssetTypes.Contains(type)) return BadRequest(new { error = "Invalid branding type" });
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.FindAsync(bandId);
        if (band is null) return NotFound();

        if (GetPath(band, type) is { } existing)
        {
            var brandingRoot = Path.Combine(env.ContentRootPath, "data", "band-branding");
            try { System.IO.File.Delete(Path.Combine(brandingRoot, existing)); } catch { /* best-effort */ }
            SetPath(band, type, null);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    [HttpPut("branding/accent-color")]
    public async Task<IActionResult> UpdateAccentColor([FromBody] UpdateAccentColorRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var color = request.Color?.Trim() ?? "";
        if (color.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
            return BadRequest(new { error = "Color must be a hex value like #c00000, or blank to clear it." });

        var band = await db.Bands.FindAsync(bandId);
        if (band is null) return NotFound();
        band.AccentColor = color.Length > 0 ? color : null;
        await db.SaveChangesAsync();
        return Ok(new { ok = true, accentColor = band.AccentColor });
    }
}
