using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record UpdateGalleryImageRequest(string Alt);

/// <summary>
/// Pushes Gallery Image edits to the active Band's own site repo via
/// GitHub - ported from the old app's routes/gallery.js. Same shape as
/// MediaController: a gallery image only ever comes into existence
/// through this dashboard.
/// </summary>
[ApiController]
[Route("/api/gallery")]
[Authorize(Policy = "BandAdmin")]
public class GalleryController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    GallerySource gallerySource,
    GallerySiteEditor gallerySiteEditor,
    CatalogStore catalogStore,
    FlyerCache flyerCache,
    Scheduler scheduler) : ControllerBase
{
    private const long MaxPhotoBytes = 20 * 1024 * 1024;

    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    // Edits an existing image's caption. The photo itself is replaced via
    // its own route below, since that also has to regenerate the thumbnail.
    [HttpPut("{galleryRef}")]
    public async Task<IActionResult> Update(string galleryRef, [FromBody] UpdateGalleryImageRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await gallerySource.FindGalleryByRefAsync(band, galleryRef);
        if (item is null) return NotFound(new { error = "Gallery image not found" });

        var alt = request.Alt?.Trim() ?? "";
        if (alt.Length == 0) return BadRequest(new { error = "Nothing to update" });
        if (alt.Length > 200) alt = alt[..200];

        try
        {
            await gallerySiteEditor.UpdateGalleryImageAsync(band, item, new Dictionary<string, string> { ["alt"] = alt });
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // A caption and the photo are both required - nothing exists here
    // until this form creates it.
    [HttpPost]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<IActionResult> Create()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        if (!Request.HasFormContentType) return BadRequest(new { error = "Expected form data" });
        var form = await Request.ReadFormAsync();

        var alt = (form["alt"].ToString() ?? "").Trim();
        if (alt.Length == 0) return BadRequest(new { error = "A caption is required" });
        if (alt.Length > 200) alt = alt[..200];

        ResolvedMedia? resolved;
        try
        {
            resolved = await catalogStore.ResolveMediaInputAsync(
                band.Id, await form.Files.GetFile("photo").ToUploadedFilePayloadAsync(), form["catalogItemId"], form["url"],
                MediaType.Image, User.Identity?.Name, MaxPhotoBytes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        if (resolved is null) return BadRequest(new { error = "A photo is required" });
        // SkiaSharp (used to generate the thumbnail) can decode webp/gif
        // too, but only PNG/JPEG match every existing hand-made gallery
        // entry's format, same restriction the old app (Jimp) already had.
        var ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
        if (ext is not ("png" or "jpg")) return BadRequest(new { error = "Unsupported image type - use PNG or JPEG" });

        try
        {
            var (id, thumb, full) = await gallerySiteEditor.AddGalleryImageAsync(band, alt, resolved.Buffer, ext);
            await flyerCache.WriteDirectlyAsync(band, full, resolved.Buffer);

            try { await scheduler.GenerateAllAsync(band.Id, band); } catch { /* non-fatal, see GigsController */ }
            return Ok(new { ok = true, id });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Replaces an existing image's photo (and regenerates its thumbnail)
    // in place - same full/thumb paths, so gallery.js's own fields don't
    // need editing.
    [HttpPost("{galleryRef}/photo")]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<IActionResult> UploadPhoto(string galleryRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await gallerySource.FindGalleryByRefAsync(band, galleryRef);
        if (item is null) return NotFound(new { error = "Gallery image not found" });
        if (!Request.HasFormContentType) return BadRequest(new { error = "Expected form data" });
        var form = await Request.ReadFormAsync();

        ResolvedMedia? resolved;
        try
        {
            resolved = await catalogStore.ResolveMediaInputAsync(
                band.Id, await form.Files.GetFile("file").ToUploadedFilePayloadAsync(), form["catalogItemId"], form["url"],
                MediaType.Image, User.Identity?.Name, MaxPhotoBytes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        if (resolved is null) return BadRequest(new { error = "No file uploaded" });
        var ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
        if (ext is not ("png" or "jpg")) return BadRequest(new { error = "Unsupported image type - use PNG or JPEG" });

        try
        {
            await gallerySiteEditor.ReplaceGalleryPhotoAsync(band, item, resolved.Buffer);
            await flyerCache.WriteDirectlyAsync(band, item.Full!, resolved.Buffer);
            // The regenerated thumbnail's bytes aren't available here
            // without re-deriving them - simplest to drop the stale
            // cached thumb and let the next cache-priming cycle refetch it.
            flyerCache.DeleteCached(band, item.Thumb);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Deletes the entry from the live site entirely, same as media items.
    [HttpDelete("{galleryRef}")]
    public async Task<IActionResult> Delete(string galleryRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await gallerySource.FindGalleryByRefAsync(band, galleryRef);
        if (item is null) return NotFound(new { error = "Gallery image not found" });

        try
        {
            await gallerySiteEditor.DeleteGalleryImageAsync(band, item);
            await db.ScheduleItems.Where(s => s.BandId == band.Id && s.ContentType == "Gallery Image" && s.GalleryRef == galleryRef).ExecuteDeleteAsync();
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
