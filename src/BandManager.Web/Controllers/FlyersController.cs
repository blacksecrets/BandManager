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
    bool Bold = false, bool Italic = false, bool Underline = false, double Rotation = 0, bool Skew = false);
public record SaveFlyerRequest(Guid SourceCatalogItemId, string GigRef, List<SaveFlyerFieldDto> Fields, bool Publish = false);

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
    CatalogStore catalogStore,
    CredentialStore credentialStore,
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
                rotation = 0,
                skew = false
            });
            y += type == FlyerFieldType.Image ? 0.14 : 0.07;
        }
        return Ok(fields);
    }

    // Powers the "Edit" button on an already-generated flyer's viewer
    // modal - returns enough to reopen the editor pre-populated with this
    // flyer's actual saved background/fields, instead of the blank
    // known-fields default. Saving from there calls Update below, which
    // overwrites this same Flyer row rather than creating a new one.
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
                rotation = f.Rotation,
                skew = f.Skew
            })
        });
    }

    // Shared by Create and Update - resolves the gig/source image, parses
    // the submitted fields, and renders the final flyer image. Doesn't
    // touch the database beyond reads, so both callers stay free to decide
    // whether the result becomes a new Flyer row or an update to an
    // existing one.
    private record RenderResult(Gig Gig, CatalogItem Source, byte[] Rendered, List<FlyerFieldDef> Fields);

    private async Task<(RenderResult? Result, IActionResult? Error)> RenderFlyerFromRequestAsync(Band band, SaveFlyerRequest request)
    {
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == request.GigRef);
        if (gig is null) return (null, NotFound(new { error = "Gig not found" }));

        var source = await db.CatalogItems.FirstOrDefaultAsync(c => c.Id == request.SourceCatalogItemId && c.BandId == band.Id);
        if (source is null) return (null, NotFound(new { error = "Source image not found" }));

        byte[] backgroundBytes;
        try { backgroundBytes = await catalogStore.GetCatalogItemBufferAsync(band.Id, source.Id); }
        catch (InvalidOperationException ex) { return (null, BadRequest(new { error = ex.Message })); }

        var fields = request.Fields.Select(f => new FlyerFieldDef(
            f.Key, f.Label, Enum.Parse<FlyerFieldType>(f.Type, ignoreCase: true),
            f.X, f.Y, f.FontSize, f.FontFamily, f.Color, f.Included, f.Value,
            f.Bold, f.Italic, f.Underline, f.Rotation, f.Skew)).ToList();

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
        catch (InvalidOperationException ex) { return (null, BadRequest(new { error = ex.Message })); }

        return (new RenderResult(gig, source, rendered, fields), null);
    }

    // Shared by Create and Update - persists whatever the caller already
    // staged on `flyer` (new or mutated) and, only when request.Publish was
    // checked and a site is actually configured, pushes the rendered image
    // live. Unpublished/unconfigured saves still persist normally - the
    // Flyer/Catalog rows are never gated behind the publish checkbox, only
    // the live push is.
    private async Task<IActionResult> SaveAndMaybePublishAsync(Band band, Flyer flyer, Gig gig, byte[] rendered, CatalogItem catalogItem, bool publish)
    {
        if (!publish || !await HasSiteConfiguredAsync(band))
        {
            await db.SaveChangesAsync();
            return Ok(new { ok = true, flyerId = flyer.Id, catalogItemId = catalogItem.Id, published = false });
        }

        try
        {
            await gigsSiteEditor.PushFlyerImageAsync(band, gig, rendered, string.IsNullOrEmpty(gig.FlyerMain) ? $"Add flyer for {gig.Title}" : $"Update flyer for {gig.Title}");

            // A publish here means this flyer's bytes are now what's
            // actually live at FlyerMain's path - keep SelectedFlyerId in
            // sync so "Select Flyer"'s Current badge (and the live-site
            // badge the flyer grid shows) reflects reality even when this
            // flyer was published straight from Create/Update rather than
            // through the Select Flyer picker. Previously only
            // GigsController.SetSelectedFlyer ever touched this field, so
            // publishing a new flyer directly left it pointing at whatever
            // was last explicitly selected (or null).
            gig.SelectedFlyerId = flyer.Id;
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

        return Ok(new { ok = true, flyerId = flyer.Id, catalogItemId = catalogItem.Id, published = true });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveFlyerRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var (result, renderErr) = await RenderFlyerFromRequestAsync(band, request);
        if (renderErr is not null) return renderErr;
        var (gig, source, rendered, fields) = result!;

        var catalogItem = await catalogStore.RegisterCatalogItemAsync(
            band.Id, rendered, "image/png", $"flyer-{gig.Ref}.png",
            CatalogSource.Upload, sourceUrl: null, uploadedBy: User.Identity?.Name,
            label: $"Flyer - {gig.Title}", category: CatalogCategory.Flyer);

        var flyer = new Flyer
        {
            BandId = band.Id,
            GeneratedCatalogItemId = catalogItem.Id,
            SourceCatalogItemId = source.Id,
            GigRef = gig.Ref,
            Fields = fields
        };
        db.Flyers.Add(flyer);

        return await SaveAndMaybePublishAsync(band, flyer, gig, rendered, catalogItem, request.Publish);
    }

    // Editing an already-generated flyer (opened via "Edit" on its viewer
    // modal) now updates that same Flyer row in place instead of Create's
    // always-insert-a-new-row behavior - Richard's testing found the old
    // "editing creates a new Flyer" design meant re-picking a flyer for a
    // gig after tweaking it didn't reliably pick up the change. Only
    // re-pushes to the live site if request.Publish is checked at save
    // time - an already-live flyer being edited does not auto-repush just
    // because it was already live, same one safety gate as Create.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveFlyerRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var flyer = await db.Flyers.FirstOrDefaultAsync(f => f.Id == id && f.BandId == band.Id);
        if (flyer is null) return NotFound(new { error = "Flyer not found" });

        var (result, renderErr) = await RenderFlyerFromRequestAsync(band, request);
        if (renderErr is not null) return renderErr;
        var (gig, source, rendered, fields) = result!;

        var catalogItem = await catalogStore.RegisterCatalogItemAsync(
            band.Id, rendered, "image/png", $"flyer-{gig.Ref}.png",
            CatalogSource.Upload, sourceUrl: null, uploadedBy: User.Identity?.Name,
            label: $"Flyer - {gig.Title}", category: CatalogCategory.Flyer);

        flyer.GeneratedCatalogItemId = catalogItem.Id;
        flyer.SourceCatalogItemId = source.Id;
        flyer.GigRef = gig.Ref;
        flyer.Fields = fields;

        return await SaveAndMaybePublishAsync(band, flyer, gig, rendered, catalogItem, request.Publish);
    }
}
