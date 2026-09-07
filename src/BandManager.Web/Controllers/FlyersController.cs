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
    bool Bold = false, bool Italic = false, bool Underline = false);
public record SaveFlyerRequest(Guid TemplateId, string GigRef, List<SaveFlyerFieldDto> Fields);

/// <summary>
/// Builds a final flyer image from a FlyerTemplate + typed field values for
/// one specific gig, saves it to the Catalog, and pushes it live as that
/// gig's site flyer - reusing the same GitHubSiteClient/GigsSiteEditor/
/// FlyerCache calls GigsController.UploadFlyer already makes (direct
/// service composition, not an internal HTTP call to that endpoint, since
/// the bytes are already in hand here from FlyerRenderer).
/// </summary>
[ApiController]
[Route("/api/flyers")]
[Authorize(Policy = "BandAdmin")]
public class FlyersController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    GigsSource gigsSource,
    GigsSiteEditor gigsSiteEditor,
    GitHubSiteClient gitHub,
    CatalogStore catalogStore,
    FlyerCache flyerCache,
    IWebHostEnvironment env) : ControllerBase
{
    // Resolved from the standard ASP.NET Core IWebHostEnvironment rather
    // than a raw injected string - a bare `string` constructor parameter
    // isn't resolvable by the DI container, and ProfileController already
    // establishes IWebHostEnvironment injection as this app's pattern for
    // "a controller needs a root path" (see its branding-upload actions).
    private string FontsRootPath => Path.Combine(env.WebRootPath, "fonts");

    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveFlyerRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var gig = await gigsSource.FindGigByRefAsync(band, request.GigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var template = await db.FlyerTemplates.Include(t => t.BackgroundCatalogItem)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId && t.BandId == band.Id);
        if (template is null) return NotFound(new { error = "Template not found" });

        byte[] backgroundBytes;
        try { backgroundBytes = await catalogStore.GetCatalogItemBufferAsync(band.Id, template.BackgroundCatalogItemId); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var fields = request.Fields.Select(f => new FlyerFieldDef(
            f.Key, f.Label, Enum.Parse<FlyerFieldType>(f.Type, ignoreCase: true),
            f.X, f.Y, f.FontSize, f.FontFamily, f.Color, f.Included, f.Value,
            f.Bold, f.Italic, f.Underline)).ToList();

        byte[]? LogoResolver(string catalogItemIdStr)
        {
            if (!Guid.TryParse(catalogItemIdStr, out var logoId)) return null;
            try { return catalogStore.GetCatalogItemBufferAsync(band.Id, logoId).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { return null; }
        }

        byte[] rendered;
        try { rendered = FlyerRenderer.RenderFlyer(backgroundBytes, fields, FontsRootPath, LogoResolver); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var gigRef = SiteContentRef.GigRef(gig);
        var catalogItem = await catalogStore.RegisterCatalogItemAsync(
            band.Id, rendered, "image/png", $"flyer-{gigRef}.png",
            CatalogSource.Upload, sourceUrl: null, uploadedBy: User.Identity?.Name,
            label: $"Flyer - {gig.Title}", category: CatalogCategory.Flyer);

        var flyer = new Flyer
        {
            BandId = band.Id,
            GeneratedCatalogItemId = catalogItem.Id,
            FlyerTemplateId = template.Id,
            GigRef = gigRef,
            Fields = fields
        };
        db.Flyers.Add(flyer);
        await db.SaveChangesAsync();

        try
        {
            if (string.IsNullOrEmpty(gig.FlyerMain))
            {
                var newPath = $"flyers/{gig.Id ?? gigRef}.png";
                await gitHub.PutBinaryFileAsync(band, newPath, rendered, $"Add flyer for {gig.Title}");
                await gigsSiteEditor.UpdateGigFieldsAsync(band, gig, new Dictionary<string, string> { ["flyerMain"] = newPath });
                await flyerCache.WriteDirectlyAsync(band, newPath, rendered);
            }
            else
            {
                var sha = await gitHub.GetFileShaAsync(band, gig.FlyerMain);
                await gitHub.PutBinaryFileAsync(band, gig.FlyerMain, rendered, $"Update flyer for {gig.Title}", sha);
                await flyerCache.WriteDirectlyAsync(band, gig.FlyerMain, rendered);
            }
        }
        catch (InvalidOperationException ex)
        {
            // The Catalog/Flyer rows are already saved at this point - the
            // flyer exists and is viewable even if pushing it live to the
            // site failed (e.g. a transient GitHub error), so this reports
            // the problem without pretending nothing was saved.
            return StatusCode(502, new { error = $"Flyer saved to Catalog, but could not push it live: {ex.Message}", flyerId = flyer.Id, catalogItemId = catalogItem.Id });
        }

        return Ok(new { ok = true, flyerId = flyer.Id, catalogItemId = catalogItem.Id });
    }
}
