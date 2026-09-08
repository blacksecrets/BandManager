using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record CatalogFromUrlRequest(string Url);
public record CatalogDeleteRequest(List<Guid> Ids);
public record CatalogUpdateLabelRequest(string Label);

/// <summary>
/// The Media Catalog's own CRUD - ported from the old app's
/// routes/catalog.js, scoped to the active Band. Field names stay
/// snake_case to match wwwroot/assets/catalog.js's existing expectations.
///
/// Not yet ported: paste-a-URL fetching (fetchUrlAsBuffer in the old
/// app's catalogStore.js) and the general resolveMediaInput dispatcher
/// that wires "pick from Catalog"/paste-a-URL into every OTHER upload
/// spot in the app - registration, browse/list/update-label/delete, and
/// direct file upload (including frame-capture/trim/split sources from
/// the video viewer, which are just this same upload endpoint with a
/// different `source` value) are what's here for now.
/// </summary>
[ApiController]
[Route("/api/catalog")]
[Authorize(Policy = "BandMember")]
public class CatalogController(ApplicationDbContext db, CatalogStore catalogStore, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    // Every image that's the source background of at least one Flyer, keyed
    // by CatalogItem id - the winged-F badge, the delete warning, and the
    // "Flyers:" dropdown on an image's modal are all driven from this same
    // one query, run once per list/single-item request rather than N+1
    // per item.
    private async Task<Dictionary<Guid, List<object>>> LoadFlyerUsageAsync(Guid bandId, IEnumerable<Guid>? onlyForItemIds = null)
    {
        IQueryable<Flyer> query = db.Flyers.AsNoTracking()
            .Where(f => f.BandId == bandId && f.SourceCatalogItemId != null)
            .Include(f => f.GeneratedCatalogItem);
        if (onlyForItemIds is not null) query = query.Where(f => onlyForItemIds.Contains(f.SourceCatalogItemId!.Value));

        var flyers = await query.ToListAsync();
        if (flyers.Count == 0) return [];

        var gigRefs = flyers.Select(f => f.GigRef).Distinct().ToList();
        var gigTitlesByRef = await db.Gigs.AsNoTracking()
            .Where(g => g.BandId == bandId && gigRefs.Contains(g.Ref))
            .ToDictionaryAsync(g => g.Ref, g => g.Title);

        var byItem = new Dictionary<Guid, List<object>>();
        foreach (var f in flyers)
        {
            var entry = new
            {
                flyerId = f.Id,
                gigRef = f.GigRef,
                gigTitle = gigTitlesByRef.GetValueOrDefault(f.GigRef, f.GigRef),
                renderedFilePath = f.GeneratedCatalogItem.FilePath
            };
            if (!byItem.TryGetValue(f.SourceCatalogItemId!.Value, out var list)) byItem[f.SourceCatalogItemId!.Value] = list = [];
            list.Add(entry);
        }
        return byItem;
    }

    // For a Flyer-category item (a rendered flyer PNG), which Flyer row it
    // is and whether it's the only one left for its gig - deleting this
    // Catalog item cascades to delete that Flyer row too (see
    // ApplicationDbContext's Cascade on Flyer.GeneratedCatalogItemId), so
    // this is genuinely "removing the flyer," not just an image.
    private async Task<Dictionary<Guid, object>> LoadFlyerInfoAsync(Guid bandId, IEnumerable<Guid>? onlyForGeneratedItemIds = null)
    {
        IQueryable<Flyer> query = db.Flyers.AsNoTracking().Where(f => f.BandId == bandId);
        if (onlyForGeneratedItemIds is not null) query = query.Where(f => onlyForGeneratedItemIds.Contains(f.GeneratedCatalogItemId));

        var flyers = await query.ToListAsync();
        if (flyers.Count == 0) return [];

        var gigRefs = flyers.Select(f => f.GigRef).Distinct().ToList();
        var gigTitlesByRef = await db.Gigs.AsNoTracking()
            .Where(g => g.BandId == bandId && gigRefs.Contains(g.Ref))
            .ToDictionaryAsync(g => g.Ref, g => g.Title);
        var countByGigRef = await db.Flyers.AsNoTracking()
            .Where(f => f.BandId == bandId && gigRefs.Contains(f.GigRef))
            .GroupBy(f => f.GigRef)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

        var result = new Dictionary<Guid, object>();
        foreach (var f in flyers)
        {
            result[f.GeneratedCatalogItemId] = new
            {
                flyerId = f.Id,
                gigRef = f.GigRef,
                gigTitle = gigTitlesByRef.GetValueOrDefault(f.GigRef, f.GigRef),
                isLastFlyerForGig = countByGigRef.GetValueOrDefault(f.GigRef, 1) <= 1
            };
        }
        return result;
    }

    private static object Serialize(CatalogItem item, List<object>? usedInFlyers, object? flyerInfo = null) => new
    {
        id = item.Id,
        media_type = item.MediaType.ToString().ToLowerInvariant(),
        file_path = item.FilePath,
        thumbnail_path = item.ThumbnailPath,
        original_filename = item.OriginalFilename,
        label = item.Label,
        mime_type = item.MimeType,
        file_size = item.FileSize,
        width = item.Width,
        height = item.Height,
        source = ToKebabCase(item.Source.ToString()),
        source_url = item.SourceUrl,
        uploaded_by = item.UploadedBy,
        created_at = item.CreatedAt,
        category = ToKebabCase(item.Category.ToString()),
        used_in_flyers = usedInFlyers ?? [],
        flyer_info = flyerInfo
    };

    private static CatalogCategory? ParseCategory(string? raw) => raw switch
    {
        "general" => CatalogCategory.General,
        "flyer" => CatalogCategory.Flyer,
        _ => null
    };

    private static string ToKebabCase(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append('-');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }

    private static bool TryParseSource(string? raw, out CatalogSource source)
    {
        source = raw switch
        {
            "frame-capture" => CatalogSource.FrameCapture,
            "trim" => CatalogSource.Trim,
            "split" => CatalogSource.Split,
            "cover-photo" => CatalogSource.CoverPhoto,
            "url" => CatalogSource.Url,
            null or "" or "upload" => CatalogSource.Upload,
            _ => CatalogSource.Upload
        };
        return true;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] string? mediaType, [FromQuery] string? category)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        MediaType? parsedType = mediaType switch
        {
            "image" => MediaType.Image,
            "video" => MediaType.Video,
            "audio" => MediaType.Audio,
            _ => null
        };
        var items = await catalogStore.ListCatalogItemsAsync(bandId, q, parsedType, ParseCategory(category));
        var usage = await LoadFlyerUsageAsync(bandId);
        var flyerInfo = await LoadFlyerInfoAsync(bandId);
        return Ok(items.Select(i => Serialize(i, usage.GetValueOrDefault(i.Id), flyerInfo.GetValueOrDefault(i.Id))));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.CatalogItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.BandId == bandId);
        if (item is null) return NotFound(new { error = "Not found" });
        var usage = await LoadFlyerUsageAsync(bandId, [id]);
        var flyerInfo = await LoadFlyerInfoAsync(bandId, [id]);
        return Ok(Serialize(item, usage.GetValueOrDefault(id), flyerInfo.GetValueOrDefault(id)));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Upload()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!Request.HasFormContentType) return BadRequest(new { error = "Provide a file" });

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return BadRequest(new { error = "Provide a file" });

        TryParseSource(form["source"], out var source);

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var buffer = ms.ToArray();

        try
        {
            var item = await catalogStore.RegisterCatalogItemAsync(
                bandId, buffer, file.ContentType, file.FileName, source, sourceUrl: null,
                uploadedBy: User.Identity?.Name);
            return Ok(Serialize(item, null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("from-url")]
    public IActionResult FromUrl([FromBody] CatalogFromUrlRequest request)
    {
        // Not ported yet - see class doc comment.
        return StatusCode(501, new { error = "Adding a Catalog item from a URL isn't wired up yet - upload a file directly for now." });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateLabel(Guid id, [FromBody] CatalogUpdateLabelRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await catalogStore.UpdateCatalogItemLabelAsync(bandId, id, request.Label);
        if (item is null) return NotFound(new { error = "Not found" });
        var usage = await LoadFlyerUsageAsync(bandId, [id]);
        return Ok(Serialize(item, usage.GetValueOrDefault(id)));
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] CatalogDeleteRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var (deleted, errors) = await catalogStore.DeleteCatalogItemsAsync(bandId, request.Ids);
        return Ok(new { ok = true, deleted, errors });
    }
}
