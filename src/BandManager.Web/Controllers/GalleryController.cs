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
/// Gallery images are a real DB table now (see Entities.GalleryImage) -
/// the database is the source of truth. If the Band has a site
/// configured, every write here also best-effort publishes to that
/// site's gallery.js via GallerySiteEditor (see GigsController's class
/// doc comment for the full reasoning).
///
/// Known interim limitation, not fixed in this pass: a photo is always
/// required to create or replace an entry, and uploading one still
/// requires a site - same as before this rewrite (Create already threw
/// without a site today).
/// </summary>
[ApiController]
[Route("/api/gallery")]
[Authorize(Policy = "BandAdmin")]
public class GalleryController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    GallerySiteEditor gallerySiteEditor,
    GitHubSiteClient gitHub,
    CatalogStore catalogStore,
    CredentialStore credentialStore,
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

    private async Task<bool> HasSiteConfiguredAsync(Band band) =>
        !string.IsNullOrWhiteSpace(band.SiteBaseUrl) && !string.IsNullOrWhiteSpace(band.GitHubOwner) && !string.IsNullOrWhiteSpace(band.GitHubRepo)
        && await credentialStore.GetCredentialAsync(band.Id, "website") is not null;

    private async Task<string?> TryPublishAsync(Band band, GalleryImage item)
    {
        if (!await HasSiteConfiguredAsync(band)) return null;
        try { await gallerySiteEditor.PublishGalleryImageAsync(band, item); return null; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private static object Serialize(GalleryImage item) => new { id = item.Ref, alt = item.Alt, thumb = item.Thumb, full = item.Full };

    [HttpGet("{galleryRef}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(string galleryRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.GalleryImages.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == galleryRef);
        if (item is null) return NotFound(new { error = "Gallery image not found" });
        return Ok(Serialize(item));
    }

    // Edits an existing image's caption. The photo itself is replaced via
    // its own route below, since that also has to regenerate the thumbnail.
    [HttpPut("{galleryRef}")]
    public async Task<IActionResult> Update(string galleryRef, [FromBody] UpdateGalleryImageRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.GalleryImages.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == galleryRef);
        if (item is null) return NotFound(new { error = "Gallery image not found" });

        var alt = request.Alt?.Trim() ?? "";
        if (alt.Length == 0) return BadRequest(new { error = "Nothing to update" });
        if (alt.Length > 200) alt = alt[..200];

        item.Alt = alt;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var pushError = await TryPublishAsync(band, item);
        if (pushError is not null) return StatusCode(502, new { error = $"Saved, but could not push to the site: {pushError}" });
        return Ok(new { ok = true });
    }

    // A caption and the photo are both required - nothing exists here
    // until this form creates it. Still requires a site (see class doc
    // comment).
    [HttpPost]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<IActionResult> Create()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        if (!await HasSiteConfiguredAsync(band))
            return BadRequest(new { error = "This band's website isn't connected yet - add it under Configure Web Presence first." });
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

        var item = new GalleryImage { BandId = band.Id, Ref = Guid.NewGuid().ToString(), Alt = alt };
        item.Full = $"images/gallery/{item.Ref}.{ext}";
        item.Thumb = $"images/gallery/thumbs/{item.Ref}-thumb.jpg";
        db.GalleryImages.Add(item);
        await db.SaveChangesAsync();

        try
        {
            var thumbBytes = GallerySiteEditor.GenerateThumbnail(resolved.Buffer);
            await gitHub.PutBinaryFileAsync(band, item.Full, resolved.Buffer, $"Add gallery photo \"{alt}\"");
            await gitHub.PutBinaryFileAsync(band, item.Thumb, thumbBytes, $"Add gallery thumbnail \"{alt}\"");
            await flyerCache.WriteDirectlyAsync(band, item.Full, resolved.Buffer);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }

        var pushError = await TryPublishAsync(band, item);
        try { await scheduler.GenerateAllAsync(band.Id, band); } catch { /* non-fatal, see GigsController */ }

        if (pushError is not null) return StatusCode(502, new { error = $"Gallery image created, but could not push to the site: {pushError}", id = item.Ref });
        return Ok(new { ok = true, id = item.Ref });
    }

    // Replaces an existing image's photo (and regenerates its thumbnail)
    // in place - still requires a site (see class doc comment).
    [HttpPost("{galleryRef}/photo")]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<IActionResult> UploadPhoto(string galleryRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.GalleryImages.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == galleryRef);
        if (item is null) return NotFound(new { error = "Gallery image not found" });
        if (!await HasSiteConfiguredAsync(band))
            return BadRequest(new { error = "This band's website isn't connected yet - add it under Configure Web Presence first." });
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
            var thumbBytes = GallerySiteEditor.GenerateThumbnail(resolved.Buffer);
            var fullSha = await gitHub.GetFileShaAsync(band, item.Full!);
            var thumbSha = await gitHub.GetFileShaAsync(band, item.Thumb!);
            await gitHub.PutBinaryFileAsync(band, item.Full!, resolved.Buffer, $"Update gallery photo \"{item.Alt}\"", fullSha);
            await gitHub.PutBinaryFileAsync(band, item.Thumb!, thumbBytes, $"Update gallery thumbnail \"{item.Alt}\"", thumbSha);
            item.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await flyerCache.WriteDirectlyAsync(band, item.Full!, resolved.Buffer);
            await flyerCache.WriteDirectlyAsync(band, item.Thumb!, thumbBytes);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Deletes the entry entirely, same as media items.
    [HttpDelete("{galleryRef}")]
    public async Task<IActionResult> Delete(string galleryRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.GalleryImages.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == galleryRef);
        if (item is null) return NotFound(new { error = "Gallery image not found" });

        string? pushError = null;
        if (await HasSiteConfiguredAsync(band))
        {
            try { await gallerySiteEditor.UnpublishGalleryImageAsync(band, item.Ref, item.Alt); }
            catch (InvalidOperationException ex) { pushError = ex.Message; }
        }

        db.GalleryImages.Remove(item);
        await db.ScheduleItems.Where(s => s.BandId == band.Id && s.ContentType == "Gallery Image" && s.GalleryRef == galleryRef).ExecuteDeleteAsync();
        await db.SaveChangesAsync();

        if (pushError is not null) return StatusCode(502, new { error = $"Gallery image deleted, but could not remove it from the site: {pushError}" });
        return Ok(new { ok = true });
    }
}
