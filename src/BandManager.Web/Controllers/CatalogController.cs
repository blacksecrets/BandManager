using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandManager.Web.Controllers;

public record CatalogFromUrlRequest(string Url);
public record CatalogDeleteRequest(List<Guid> Ids);
public record CatalogUpdateLabelRequest(string Label);
public record CatalogReclassifyRequest(string Category);

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
public class CatalogController(CatalogStore catalogStore, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(CatalogItem item) => new
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
        category = ToKebabCase(item.Category.ToString())
    };

    private static CatalogCategory? ParseCategory(string? raw) => raw switch
    {
        "general" => CatalogCategory.General,
        "flyer-template" => CatalogCategory.FlyerTemplate,
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
        return Ok(items.Select(Serialize));
    }

    [HttpPut("{id:guid}/reclassify")]
    public async Task<IActionResult> Reclassify(Guid id, [FromBody] CatalogReclassifyRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var category = ParseCategory(request.Category);
        if (category is null) return BadRequest(new { error = "Invalid category." });

        var (item, error) = await catalogStore.ReclassifyItemAsync(bandId, id, category.Value);
        if (error is not null) return item is null && error == "Not found" ? NotFound(new { error }) : BadRequest(new { error });
        return Ok(Serialize(item!));
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
            return Ok(Serialize(item));
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
        return Ok(Serialize(item));
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] CatalogDeleteRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var count = await catalogStore.DeleteCatalogItemsAsync(bandId, request.Ids);
        return Ok(new { ok = true, deleted = count });
    }
}
