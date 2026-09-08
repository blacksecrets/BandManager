using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveFlyerFieldDto(
    string Key, string Label, string Type, double X, double Y, double? FontSize, string? FontFamily, string? Color, bool Included, string? Value,
    bool Bold = false, bool Italic = false, bool Underline = false, double Rotation = 0);
public record SaveFlyerRequest(Guid SourceCatalogItemId, string GigRef, List<SaveFlyerFieldDto> Fields);

/// <summary>
/// Builds a final flyer image from an existing Catalog image + typed field
/// values for one specific gig, saves it to the Catalog, and pushes it live
/// as that gig's site flyer - reusing the same GitHubSiteClient/
/// GigsSiteEditor/FlyerCache calls GigsController.UploadFlyer already
/// makes (direct service composition, not an internal HTTP call to that
/// endpoint, since the bytes are already in hand here from FlyerRenderer).
///
/// There is no "Flyer Template" concept - any General Catalog image can be
/// the background directly (see KnownFields below for the starter field
/// layout every flyer seeds from, moved here from the now-deleted
/// FlyerTemplatesController).
/// </summary>
[ApiController]
[Route("/api/flyers")]
[Authorize(Policy = "BandAdmin")]
public class FlyersController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    GigsSiteEditor gigsSiteEditor,
    GitHubSiteClient gitHub,
    CatalogStore catalogStore,
    CredentialStore credentialStore,
    FlyerCache flyerCache,
    IWebHostEnvironment env) : ControllerBase
{
    // Resolved from the standard ASP.NET Core IWebHostEnvironment rather
    // than a raw injected string - a bare `string` constructor parameter
    // isn't resolvable by the DI container, and ProfileController already
    // establishes IWebHostEnvironment injection as this app's pattern for
    // "a controller needs a root path" (see its branding-upload actions).
    private string FontsRootPath => Path.Combine(env.WebRootPath, "fonts");

    // Where SuperAdmin-uploaded custom fonts live - data/, not wwwroot/,
    // since wwwroot is baked into the image and wiped on redeploy (see
    // CustomFlyerFont.cs and Program.cs's /custom-fonts static route).
    private string CustomFontsRootPath => Path.Combine(env.ContentRootPath, "data", "fonts");

    // Every field the app currently knows how to put on a flyer - seeded
    // in a simple default vertical stack down the left third of the image,
    // white Oswald Bold text, all visible, whenever someone starts a new
    // flyer from an image. With-acts start at a single slot (with-0);
    // flyerEditor.js grows more to match however many With-acts the gig
    // actually has.
    public static readonly (string Key, string Label, FlyerFieldType Type)[] KnownFields =
    [
        ("title", "Title", FlyerFieldType.Text),
        ("date", "Date", FlyerFieldType.Text),
        ("doorsTime", "Doors Time", FlyerFieldType.Text),
        ("openerTime", "Opener Start Time", FlyerFieldType.Text),
        ("headlinerTime", "Headliner Start Time", FlyerFieldType.Text),
        ("venue", "Venue Name", FlyerFieldType.Text),
        ("address", "Venue Address", FlyerFieldType.Text),
        ("with-0", "With", FlyerFieldType.Text),
        ("tickets", "Tickets", FlyerFieldType.Text),
        ("presentedBy-0-name", "Presented By (name)", FlyerFieldType.Text),
        ("presentedBy-0-url", "Presented By (URL)", FlyerFieldType.Text),
        ("presentedBy-0-logo", "Presented By (logo)", FlyerFieldType.Image),
    ];

    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    private async Task<bool> HasSiteConfiguredAsync(Band band) =>
        !string.IsNullOrWhiteSpace(band.SiteBaseUrl) && !string.IsNullOrWhiteSpace(band.GitHubOwner) && !string.IsNullOrWhiteSpace(band.GitHubRepo)
        && await credentialStore.GetCredentialAsync(band.Id, "website") is not null;

    [HttpGet("fonts")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Fonts()
    {
        var bundled = FlyerFonts.Available.Select(f => new { key = f.Key, label = f.Label, fileUrl = (string?)null, format = (string?)null });
        var custom = await db.CustomFlyerFonts.AsNoTracking().OrderBy(f => f.Label).ToListAsync();
        var customEntries = custom.Select(f => new
        {
            key = $"custom-{f.Id}",
            label = f.Label,
            fileUrl = (string?)$"/custom-fonts/{f.Id}{f.Extension}",
            format = (string?)(f.Extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ? "opentype" : "truetype")
        });
        return Ok(bundled.Concat(customEntries));
    }

    // The default field layout a brand-new flyer starts from, for
    // flyerEditor.js to seed when it isn't editing an existing Flyer -
    // same shape/positions FlyerTemplatesController used to seed a
    // template with, just not persisted as its own row anymore.
    [HttpGet("known-fields")]
    [Authorize(Policy = "BandMember")]
    public IActionResult KnownFieldsDefaults([FromQuery] string? defaultFontFamily)
    {
        var defaultFont = string.IsNullOrWhiteSpace(defaultFontFamily) ? FlyerFonts.Available[0].Key : defaultFontFamily;
        var fields = new List<object>();
        var y = 0.08;
        foreach (var (key, label, type) in KnownFields)
        {
            fields.Add(new
            {
                key,
                label,
                type = type.ToString(),
                x = 0.06,
                y,
                fontSize = type == FlyerFieldType.Image ? 0.12 : 0.05,
                fontFamily = defaultFont,
                color = "#ffffff",
                defaultVisible = true,
                rotation = 0
            });
            y += type == FlyerFieldType.Image ? 0.14 : 0.07;
        }
        return Ok(fields);
    }

    // Powers the "Edit" button on an already-generated flyer's viewer
    // modal - returns enough to reopen the editor pre-populated with this
    // flyer's actual saved background/fields, instead of the blank
    // known-fields default. Saving from there still creates a NEW Flyer
    // row (see Create below) - editing never overwrites history.
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(Guid id)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var flyer = await db.Flyers.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.BandId == band.Id);
        if (flyer is null) return NotFound(new { error = "Flyer not found" });
        if (flyer.SourceCatalogItemId is null)
            return BadRequest(new { error = "This flyer's original background image was deleted, so it can't be reopened for editing." });

        return Ok(new
        {
            id = flyer.Id,
            sourceCatalogItemId = flyer.SourceCatalogItemId,
            gigRef = flyer.GigRef,
            fields = flyer.Fields.Select(f => new
            {
                key = f.Key,
                label = f.Label,
                type = f.Type.ToString(),
                x = f.X,
                y = f.Y,
                fontSize = f.FontSize,
                fontFamily = f.FontFamily,
                color = f.Color,
                included = f.DefaultVisible,
                value = f.Value,
                bold = f.Bold,
                italic = f.Italic,
                underline = f.Underline,
                rotation = f.Rotation
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveFlyerRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == request.GigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var source = await db.CatalogItems.FirstOrDefaultAsync(c => c.Id == request.SourceCatalogItemId && c.BandId == band.Id);
        if (source is null) return NotFound(new { error = "Source image not found" });

        byte[] backgroundBytes;
        try { backgroundBytes = await catalogStore.GetCatalogItemBufferAsync(band.Id, source.Id); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var fields = request.Fields.Select(f => new FlyerFieldDef(
            f.Key, f.Label, Enum.Parse<FlyerFieldType>(f.Type, ignoreCase: true),
            f.X, f.Y, f.FontSize, f.FontFamily, f.Color, f.Included, f.Value,
            f.Bold, f.Italic, f.Underline, f.Rotation)).ToList();

        byte[]? LogoResolver(string catalogItemIdStr)
        {
            if (!Guid.TryParse(catalogItemIdStr, out var logoId)) return null;
            try { return catalogStore.GetCatalogItemBufferAsync(band.Id, logoId).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { return null; }
        }

        var customFontKeys = fields.Select(f => f.FontFamily).Where(k => k is not null && k.StartsWith("custom-", StringComparison.Ordinal))
            .Select(k => k!["custom-".Length..]).Distinct().ToList();
        var customFontsById = customFontKeys.Count == 0 ? []
            : await db.CustomFlyerFonts.AsNoTracking().Where(f => customFontKeys.Contains(f.Id.ToString())).ToDictionaryAsync(f => $"custom-{f.Id}", f => Path.Combine(CustomFontsRootPath, $"{f.Id}{f.Extension}"));
        string? CustomFontPathResolver(string key) => customFontsById.GetValueOrDefault(key);

        byte[] rendered;
        try { rendered = FlyerRenderer.RenderFlyer(backgroundBytes, fields, FontsRootPath, LogoResolver, CustomFontPathResolver); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var gigRef = gig.Ref;
        var catalogItem = await catalogStore.RegisterCatalogItemAsync(
            band.Id, rendered, "image/png", $"flyer-{gigRef}.png",
            CatalogSource.Upload, sourceUrl: null, uploadedBy: User.Identity?.Name,
            label: $"Flyer - {gig.Title}", category: CatalogCategory.Flyer);

        var flyer = new Flyer
        {
            BandId = band.Id,
            GeneratedCatalogItemId = catalogItem.Id,
            SourceCatalogItemId = source.Id,
            GigRef = gigRef,
            Fields = fields
        };
        db.Flyers.Add(flyer);

        // Still requires a site to push the rendered image live, same
        // "known interim limitation" as GigsController's own flyer paths -
        // the Flyer/Catalog rows above are saved either way.
        if (!await HasSiteConfiguredAsync(band))
        {
            await db.SaveChangesAsync();
            return Ok(new { ok = true, flyerId = flyer.Id, catalogItemId = catalogItem.Id });
        }

        try
        {
            if (string.IsNullOrEmpty(gig.FlyerMain))
            {
                var newPath = $"flyers/{gigRef}.png";
                await gitHub.PutBinaryFileAsync(band, newPath, rendered, $"Add flyer for {gig.Title}");
                gig.FlyerMain = newPath;
                await flyerCache.WriteDirectlyAsync(band, newPath, rendered);
            }
            else
            {
                var sha = await gitHub.GetFileShaAsync(band, gig.FlyerMain);
                await gitHub.PutBinaryFileAsync(band, gig.FlyerMain, rendered, $"Update flyer for {gig.Title}", sha);
                await flyerCache.WriteDirectlyAsync(band, gig.FlyerMain, rendered);
            }
            gig.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var withActs = await db.GigWithBands.Where(w => w.GigId == gig.Id).Include(w => w.WithBand)
                .OrderBy(w => w.SortOrder).Select(w => new WithAct(w.WithBand.Name, w.Url)).ToListAsync();
            await gigsSiteEditor.PublishGigAsync(band, gig, withActs);
        }
        catch (InvalidOperationException ex)
        {
            // The Catalog/Flyer rows are already saved at this point - the
            // flyer exists and is viewable even if pushing it live to the
            // site failed (e.g. a transient GitHub error), so this reports
            // the problem without pretending nothing was saved.
            await db.SaveChangesAsync();
            return StatusCode(502, new { error = $"Flyer saved to Catalog, but could not push it live: {ex.Message}", flyerId = flyer.Id, catalogItemId = catalogItem.Id });
        }

        return Ok(new { ok = true, flyerId = flyer.Id, catalogItemId = catalogItem.Id });
    }
}
