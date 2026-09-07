using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record CreateFlyerTemplateRequest(Guid BackgroundCatalogItemId, string Name, string? DefaultFontFamily = null);
public record FlyerFieldDefDto(
    string Key, string Label, string Type, double X, double Y, double? FontSize, string? FontFamily, string? Color, bool DefaultVisible,
    bool Bold = false, bool Italic = false, bool Underline = false);
public record UpdateFlyerTemplateRequest(string? Name, string? DefaultFontFamily, List<FlyerFieldDefDto> Fields);

/// <summary>
/// Flyer Templates - a reusable background image (a Catalog item,
/// Category == FlyerTemplate) plus a default field layout used by both the
/// Template Edit modal (position/font/color/visibility only) and the Flyer
/// Editor (which starts from these defaults, then lets values/positions be
/// overridden per-flyer - see FlyersController).
/// </summary>
[ApiController]
[Route("/api/flyer-templates")]
[Authorize(Policy = "BandAdmin")]
public class FlyerTemplatesController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    // Every field the app currently knows how to put on a flyer - seeded
    // onto a brand-new template so the Template Edit modal always starts
    // with the full set to drag/hide/style, per "insert text fields for
    // all required information" from the feature request. With-acts start
    // at a single slot (with-0); the Flyer Editor grows more to match
    // whatever gig it's building for (see FlyersController's doc comment).
    private static readonly (string Key, string Label, FlyerFieldType Type)[] KnownFields =
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
        ("presentedByName", "Presented By (name)", FlyerFieldType.Text),
        ("presentedByUrl", "Presented By (URL)", FlyerFieldType.Text),
        ("presentedByLogo", "Presented By (logo)", FlyerFieldType.Image),
    ];

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(FlyerTemplate t) => new
    {
        id = t.Id,
        name = t.Name,
        defaultFontFamily = t.DefaultFontFamily,
        backgroundCatalogItemId = t.BackgroundCatalogItemId,
        backgroundFilePath = t.BackgroundCatalogItem?.FilePath,
        fields = t.Fields,
        createdAt = t.CreatedAt
    };

    [HttpGet("fonts")]
    [Authorize(Policy = "BandMember")]
    public IActionResult Fonts() => Ok(FlyerFonts.Available.Select(f => new { key = f.Key, label = f.Label }));

    [HttpGet]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var templates = await db.FlyerTemplates.AsNoTracking().Include(t => t.BackgroundCatalogItem)
            .Where(t => t.BandId == bandId).OrderBy(t => t.Name).ToListAsync();
        return Ok(templates.Select(Serialize));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var template = await db.FlyerTemplates.AsNoTracking().Include(t => t.BackgroundCatalogItem)
            .FirstOrDefaultAsync(t => t.Id == id && t.BandId == bandId);
        if (template is null) return NotFound(new { error = "Not found" });
        return Ok(Serialize(template));
    }

    // Seeds every known field in a simple default vertical stack down the
    // left third of the image, white Oswald Bold text, all visible - the
    // author drags/hides/restyles from there in the Template Edit modal.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFlyerTemplateRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var background = await db.CatalogItems.FirstOrDefaultAsync(c => c.Id == request.BackgroundCatalogItemId && c.BandId == bandId);
        if (background is null) return BadRequest(new { error = "Background image not found." });

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required." });

        var defaultFont = string.IsNullOrWhiteSpace(request.DefaultFontFamily) ? FlyerFonts.Available[0].Key : request.DefaultFontFamily;

        var fields = new List<FlyerFieldDef>();
        var y = 0.08;
        foreach (var (key, label, type) in KnownFields)
        {
            fields.Add(new FlyerFieldDef(key, label, type, 0.06, y, type == FlyerFieldType.Image ? 0.12 : 0.05, defaultFont, "#ffffff", true));
            y += type == FlyerFieldType.Image ? 0.14 : 0.07;
        }

        var template = new FlyerTemplate { BandId = bandId, BackgroundCatalogItemId = background.Id, Name = name, DefaultFontFamily = defaultFont, Fields = fields };
        db.FlyerTemplates.Add(template);
        await db.SaveChangesAsync();
        return Ok(Serialize(template));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFlyerTemplateRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var template = await db.FlyerTemplates.FirstOrDefaultAsync(t => t.Id == id && t.BandId == bandId);
        if (template is null) return NotFound(new { error = "Not found" });

        if (!string.IsNullOrWhiteSpace(request.Name)) template.Name = request.Name.Trim();
        template.DefaultFontFamily = string.IsNullOrWhiteSpace(request.DefaultFontFamily) ? null : request.DefaultFontFamily;
        template.Fields = request.Fields.Select(f => new FlyerFieldDef(
            f.Key, f.Label, Enum.Parse<FlyerFieldType>(f.Type, ignoreCase: true),
            f.X, f.Y, f.FontSize, f.FontFamily, f.Color, f.DefaultVisible,
            Bold: f.Bold, Italic: f.Italic, Underline: f.Underline)).ToList();

        await db.SaveChangesAsync();
        return Ok(Serialize(template));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var template = await db.FlyerTemplates.FirstOrDefaultAsync(t => t.Id == id && t.BandId == bandId);
        if (template is null) return NotFound(new { error = "Not found" });

        // The background CatalogItem is left alone (still Category ==
        // FlyerTemplate) - deleting just the template row, not the image,
        // matches how removing a RepertoireEntry never touches its Song.
        db.FlyerTemplates.Remove(template);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
