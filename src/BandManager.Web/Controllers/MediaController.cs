using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// Pushes Media Item edits to the active Band's own site repo via GitHub
/// - ported from the old app's routes/media.js. Unlike a Calendar
/// Listing (which already exists on the site before this app touches it),
/// a media item only ever comes into existence through this dashboard.
/// </summary>
[ApiController]
[Route("/api/media")]
[Authorize(Policy = "BandAdmin")]
public class MediaController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    MediaSource mediaSource,
    MediaSiteEditor mediaSiteEditor,
    GitHubSiteClient gitHub,
    CatalogStore catalogStore,
    FlyerCache flyerCache,
    Scheduler scheduler) : ControllerBase
{
    private const long MaxArtBytes = 20 * 1024 * 1024;

    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    [HttpPut("{mediaRef}")]
    public async Task<IActionResult> Update(string mediaRef, [FromBody] Dictionary<string, string> body)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await mediaSource.FindMediaByRefAsync(band, mediaRef);
        if (item is null) return NotFound(new { error = "Media item not found" });

        var fields = new Dictionary<string, string>();
        if (body.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
            fields["title"] = title.Trim()[..Math.Min(title.Trim().Length, 200)];
        if (body.TryGetValue("url", out var url) && !string.IsNullOrWhiteSpace(url))
            fields["url"] = url.Trim()[..Math.Min(url.Trim().Length, 1000)];
        if (fields.Count == 0) return BadRequest(new { error = "Nothing to update" });

        try
        {
            await mediaSiteEditor.UpdateMediaItemAsync(band, item, fields);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // A title and a YouTube/SoundCloud link are required. Tile art is
    // optional - if omitted, a thumbnail is auto-derived from the link
    // itself; uploading one always overrides that.
    [HttpPost]
    [RequestSizeLimit(MaxArtBytes)]
    public async Task<IActionResult> Create()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        if (!Request.HasFormContentType) return BadRequest(new { error = "Expected form data" });
        var form = await Request.ReadFormAsync();

        var title = (form["title"].ToString() ?? "").Trim();
        var url = (form["mediaUrl"].ToString() ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { error = "Title is required" });
        if (url.Length == 0) return BadRequest(new { error = "A YouTube or SoundCloud link is required" });
        if (title.Length > 200) title = title[..200];
        if (url.Length > 1000) url = url[..1000];

        ResolvedMedia? resolved;
        try
        {
            resolved = await catalogStore.ResolveMediaInputAsync(
                band.Id, await form.Files.GetFile("art").ToUploadedFilePayloadAsync(), form["catalogItemId"], form["url"],
                MediaType.Image, User.Identity?.Name, MaxArtBytes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        string? ext = null;
        if (resolved is not null)
        {
            ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
            if (ext is not ("png" or "jpg" or "webp" or "gif"))
                return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
        }

        try
        {
            var (id, thumbnail) = await mediaSiteEditor.AddMediaItemAsync(band, title, url, resolved?.Buffer, ext);
            // Only a locally-supplied thumbnail has bytes worth caching -
            // an auto-derived one is already a live external URL.
            if (resolved is not null) await flyerCache.WriteDirectlyAsync(band, thumbnail, resolved.Buffer);

            try { await scheduler.GenerateAllAsync(band.Id, band); } catch { /* non-fatal, see GigsController */ }
            return Ok(new { ok = true, id });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Replaces an item's tile art. Usually in place at the same file path
    // - but if the current thumbnail is an auto-derived external URL,
    // this is the first time it gets a real repo file, which needs a new
    // path and a text edit to point the field at it.
    [HttpPost("{mediaRef}/art")]
    [RequestSizeLimit(MaxArtBytes)]
    public async Task<IActionResult> UploadArt(string mediaRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await mediaSource.FindMediaByRefAsync(band, mediaRef);
        if (item is null) return NotFound(new { error = "Media item not found" });
        if (!Request.HasFormContentType) return BadRequest(new { error = "Expected form data" });
        var form = await Request.ReadFormAsync();

        ResolvedMedia? resolved;
        try
        {
            resolved = await catalogStore.ResolveMediaInputAsync(
                band.Id, await form.Files.GetFile("file").ToUploadedFilePayloadAsync(), form["catalogItemId"], form["url"],
                MediaType.Image, User.Identity?.Name, MaxArtBytes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        if (resolved is null) return BadRequest(new { error = "Provide a file, a Catalog pick, or a URL" });

        try
        {
            var isExternal = (item.Thumbnail ?? "").StartsWith("http://") || (item.Thumbnail ?? "").StartsWith("https://");
            if (isExternal || string.IsNullOrEmpty(item.Thumbnail))
            {
                var ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
                if (ext is not ("png" or "jpg" or "webp" or "gif"))
                    return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
                var newPath = $"media/{item.Id}.{ext}";
                await gitHub.PutBinaryFileAsync(band, newPath, resolved.Buffer, $"Add tile art for \"{item.Title}\"");
                await mediaSiteEditor.UpdateMediaItemAsync(band, item, new Dictionary<string, string> { ["thumbnail"] = newPath });
                await flyerCache.WriteDirectlyAsync(band, newPath, resolved.Buffer);
                return Ok(new { ok = true, path = newPath });
            }

            var sha = await gitHub.GetFileShaAsync(band, item.Thumbnail!);
            await gitHub.PutBinaryFileAsync(band, item.Thumbnail!, resolved.Buffer, $"Update tile art for \"{item.Title}\"", sha);
            await flyerCache.WriteDirectlyAsync(band, item.Thumbnail!, resolved.Buffer);
            return Ok(new { ok = true, path = item.Thumbnail });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Deletes the entry from the live site entirely - a media item has no
    // other existence, so removing it here means removing it, period.
    [HttpDelete("{mediaRef}")]
    public async Task<IActionResult> Delete(string mediaRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await mediaSource.FindMediaByRefAsync(band, mediaRef);
        if (item is null) return NotFound(new { error = "Media item not found" });

        try
        {
            await mediaSiteEditor.DeleteMediaItemAsync(band, item);
            await db.ScheduleItems.Where(s => s.BandId == band.Id && s.ContentType == "Media Item" && s.MediaRef == mediaRef).ExecuteDeleteAsync();
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
