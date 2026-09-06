using BandManager.Data;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace BandManager.Web.Controllers;

/// <summary>
/// Serves the active Band's flyer-cache mirror - gig flyers, media
/// thumbnails, gallery photos, anything wwwroot/assets/dashboard.js
/// builds a "/flyer-cache/{relativePath}" URL for. Deliberately a real
/// endpoint, not a plain static files mapping: the URL itself carries no
/// Band id (it's just the site-relative path exactly as it appears on the
/// Band's own site, e.g. "images/gallery/thumbs/x-thumb.jpg"), so serving
/// it correctly needs the caller's own active Band - and the file may not
/// be cached locally yet the first time it's requested, which a plain
/// static files mapping has no way to fetch on demand.
/// </summary>
[ApiController]
[Route("/flyer-cache")]
[Authorize(Policy = "BandMember")]
public class FlyerCacheController(ApplicationDbContext db, IActiveBandAccessor activeBand, FlyerCache flyerCache) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    [HttpGet("{*path}")]
    public async Task<IActionResult> Get(string path)
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return NotFound();
        var band = await db.Bands.FindAsync(bandId.Value);
        if (band is null) return NotFound();

        var cachedPath = await flyerCache.EnsureCachedAsync(band, path);
        if (cachedPath is null) return NotFound();

        if (!ContentTypeProvider.TryGetContentType(cachedPath, out var contentType))
            contentType = "application/octet-stream";
        return PhysicalFile(cachedPath, contentType);
    }
}
