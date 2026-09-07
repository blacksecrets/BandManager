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
/// Media items are a real DB table now (see Entities.MediaItem) - the
/// database is the source of truth. If the Band has a site configured,
/// every write here also best-effort publishes to that site's media.js
/// via MediaSiteEditor (see GigsController's class doc comment for the
/// full reasoning - same pattern, reused verbatim).
///
/// Known interim limitation, not fixed in this pass: uploading tile art
/// (as opposed to an auto-derived YouTube/SoundCloud thumbnail, which
/// needs no site) still requires a site - same as before this rewrite.
/// </summary>
[ApiController]
[Route("/api/media")]
[Authorize(Policy = "BandAdmin")]
public class MediaController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    MediaSiteEditor mediaSiteEditor,
    GitHubSiteClient gitHub,
    CatalogStore catalogStore,
    CredentialStore credentialStore,
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

    private async Task<bool> HasSiteConfiguredAsync(Band band) =>
        !string.IsNullOrWhiteSpace(band.SiteBaseUrl) && !string.IsNullOrWhiteSpace(band.GitHubOwner) && !string.IsNullOrWhiteSpace(band.GitHubRepo)
        && await credentialStore.GetCredentialAsync(band.Id, "website") is not null;

    private async Task<string?> TryPublishAsync(Band band, MediaItem item)
    {
        if (!await HasSiteConfiguredAsync(band)) return null;
        try { await mediaSiteEditor.PublishMediaItemAsync(band, item); return null; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private static object Serialize(MediaItem item) => new { id = item.Ref, title = item.Title, url = item.Url, thumbnail = item.Thumbnail };

    [HttpGet("{mediaRef}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(string mediaRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.BandId == band.Id && m.Ref == mediaRef);
        if (item is null) return NotFound(new { error = "Media item not found" });
        return Ok(Serialize(item));
    }

    [HttpPut("{mediaRef}")]
    public async Task<IActionResult> Update(string mediaRef, [FromBody] Dictionary<string, string> body)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.BandId == band.Id && m.Ref == mediaRef);
        if (item is null) return NotFound(new { error = "Media item not found" });

        var changed = false;
        if (body.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            item.Title = title.Trim()[..Math.Min(title.Trim().Length, 200)];
            changed = true;
        }
        if (body.TryGetValue("url", out var rawUrl) && !string.IsNullOrWhiteSpace(rawUrl))
        {
            var embed = MediaSiteEditor.ToEmbedUrl(rawUrl);
            if (embed is null) return BadRequest(new { error = "That link doesn't look like a YouTube or SoundCloud URL." });
            item.Url = embed;
            changed = true;
        }
        if (!changed) return BadRequest(new { error = "Nothing to update" });

        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var pushError = await TryPublishAsync(band, item);
        if (pushError is not null) return StatusCode(502, new { error = $"Saved, but could not push to the site: {pushError}" });
        return Ok(new { ok = true });
    }

    // A title and a YouTube/SoundCloud link are required. Tile art is
    // optional - if omitted, a thumbnail is auto-derived from the link
    // itself (no site needed for that); uploading one always overrides
    // that, but still requires a site (see class doc comment).
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

        var embed = MediaSiteEditor.ToEmbedUrl(url);
        if (embed is null) return BadRequest(new { error = "That link doesn't look like a YouTube or SoundCloud URL." });

        var hasArtInput = form.Files.GetFile("art") is not null || !string.IsNullOrEmpty(form["catalogItemId"]) || !string.IsNullOrEmpty(form["url"]);
        if (hasArtInput && !await HasSiteConfiguredAsync(band))
            return BadRequest(new { error = "Uploading custom tile art needs this band's website connected first (Configure Web Presence). You can create the item without it now - a thumbnail auto-derives from the link." });

        ResolvedMedia? resolved = null;
        string? ext = null;
        if (hasArtInput)
        {
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
            if (resolved is not null)
            {
                ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
                if (ext is not ("png" or "jpg" or "webp" or "gif"))
                    return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
            }
        }

        var item = new MediaItem { BandId = band.Id, Ref = Guid.NewGuid().ToString(), Title = title, Url = embed };

        string? thumbnail = null;
        if (resolved is not null)
        {
            thumbnail = $"media/{item.Ref}.{ext}";
        }
        else
        {
            thumbnail = await mediaSiteEditor.DeriveAutoThumbnailAsync(url);
            if (thumbnail is null) return BadRequest(new { error = "Couldn't automatically find a thumbnail for that link - please upload tile art." });
        }
        item.Thumbnail = thumbnail;

        db.MediaItems.Add(item);
        await db.SaveChangesAsync();

        if (resolved is not null)
        {
            try { await gitHub.PutBinaryFileAsync(band, thumbnail!, resolved.Buffer, $"Add tile art for \"{title}\""); }
            catch (InvalidOperationException ex) { return StatusCode(502, new { error = ex.Message }); }
            await flyerCache.WriteDirectlyAsync(band, thumbnail!, resolved.Buffer);
        }

        var pushError = await TryPublishAsync(band, item);
        try { await scheduler.GenerateAllAsync(band.Id, band); } catch { /* non-fatal, see GigsController */ }

        if (pushError is not null) return StatusCode(502, new { error = $"Media item created, but could not push to the site: {pushError}", id = item.Ref });
        return Ok(new { ok = true, id = item.Ref });
    }

    // Replaces an item's tile art - still requires a site (see class doc
    // comment).
    [HttpPost("{mediaRef}/art")]
    [RequestSizeLimit(MaxArtBytes)]
    public async Task<IActionResult> UploadArt(string mediaRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.BandId == band.Id && m.Ref == mediaRef);
        if (item is null) return NotFound(new { error = "Media item not found" });
        if (!await HasSiteConfiguredAsync(band))
            return BadRequest(new { error = "This band's website isn't connected yet - add it under Configure Web Presence first." });
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
            string path;
            var isExternal = (item.Thumbnail ?? "").StartsWith("http://") || (item.Thumbnail ?? "").StartsWith("https://");
            if (isExternal || string.IsNullOrEmpty(item.Thumbnail))
            {
                var ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
                if (ext is not ("png" or "jpg" or "webp" or "gif"))
                    return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
                path = $"media/{item.Ref}.{ext}";
                await gitHub.PutBinaryFileAsync(band, path, resolved.Buffer, $"Add tile art for \"{item.Title}\"");
                item.Thumbnail = path;
            }
            else
            {
                path = item.Thumbnail;
                var sha = await gitHub.GetFileShaAsync(band, path);
                await gitHub.PutBinaryFileAsync(band, path, resolved.Buffer, $"Update tile art for \"{item.Title}\"", sha);
            }
            item.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await flyerCache.WriteDirectlyAsync(band, path, resolved.Buffer);

            var pushError = await TryPublishAsync(band, item);
            if (pushError is not null) return StatusCode(502, new { error = $"Tile art saved, but could not push to the site: {pushError}", path });
            return Ok(new { ok = true, path });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Deletes the entry entirely - a media item has no other existence.
    [HttpDelete("{mediaRef}")]
    public async Task<IActionResult> Delete(string mediaRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.BandId == band.Id && m.Ref == mediaRef);
        if (item is null) return NotFound(new { error = "Media item not found" });

        string? pushError = null;
        if (await HasSiteConfiguredAsync(band))
        {
            try { await mediaSiteEditor.UnpublishMediaItemAsync(band, item.Ref, item.Title); }
            catch (InvalidOperationException ex) { pushError = ex.Message; }
        }

        db.MediaItems.Remove(item);
        await db.ScheduleItems.Where(s => s.BandId == band.Id && s.ContentType == "Media Item" && s.MediaRef == mediaRef).ExecuteDeleteAsync();
        await db.SaveChangesAsync();

        if (pushError is not null) return StatusCode(502, new { error = $"Media item deleted, but could not remove it from the site: {pushError}" });
        return Ok(new { ok = true });
    }
}
