using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace BandManager.Data.Services;

public record ResolvedMedia(byte[] Buffer, string MimeType, string? Ext, string Mode);

/// <summary>An already-read upload's bytes - the Web layer extracts these
/// from its own IFormFile (ASP.NET Core-specific, not available to this
/// plain class library project) before calling ResolveMediaInputAsync.</summary>
public record UploadedFilePayload(byte[] Bytes, string MimeType, string? FileName);

/// <summary>
/// The Media Catalog: a persistent, per-Band, browsable library of every
/// image/video/audio file this Band's users have touched. Ported from the
/// old app's src/catalogStore.js - registerCatalogItem is the single write
/// path (called directly by the Catalog's own routes, and by Cover Photo
/// generation below). catalogRootPath is a plain constructor parameter
/// (not resolved internally), same pattern as AesGcmCredentialCipher's
/// keyFilePath - resolved from IWebHostEnvironment.ContentRootPath by the
/// Web project's DI registration.
/// </summary>
public class CatalogStore(ApplicationDbContext db, string catalogRootPath, HttpClient http)
{
    private const int ThumbMaxWidth = 200; // matches the old app's generateThumbnail
    private const long DefaultMaxUrlBytes = 200 * 1024 * 1024;

    private static readonly Dictionary<string, string> ExtForMime = new()
    {
        ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp", ["image/gif"] = "gif",
        ["video/mp4"] = "mp4", ["video/webm"] = "webm", ["video/quicktime"] = "mov", ["video/x-matroska"] = "mkv",
        ["audio/mpeg"] = "mp3", ["audio/mp4"] = "m4a", ["audio/wav"] = "wav", ["audio/x-wav"] = "wav", ["audio/ogg"] = "ogg"
    };

    public static string ExtForMimeType(string mimeType, string? originalFilename)
    {
        if (ExtForMime.TryGetValue(mimeType, out var ext)) return ext;
        var fromName = originalFilename is not null ? Path.GetExtension(originalFilename).TrimStart('.').ToLowerInvariant() : "";
        return string.IsNullOrEmpty(fromName) ? "bin" : fromName;
    }

    public static MediaType? MediaTypeForMime(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType)) return null;
        if (mimeType.StartsWith("image/")) return MediaType.Image;
        if (mimeType.StartsWith("video/")) return MediaType.Video;
        if (mimeType.StartsWith("audio/")) return MediaType.Audio;
        return null;
    }

    /// <summary>Core write path - writes the full file (+ a thumbnail if
    /// it's an image), inserts the row, returns it. A thumbnail failure
    /// (exotic/undecodable image format) is non-fatal - ThumbnailPath just
    /// stays null and the UI falls back to the full file.</summary>
    public async Task<CatalogItem> RegisterCatalogItemAsync(
        Guid bandId, byte[] buffer, string mimeType, string? originalFilename,
        CatalogSource source, string? sourceUrl, string? uploadedBy, string? label = null,
        CatalogCategory category = CatalogCategory.General)
    {
        var mediaType = MediaTypeForMime(mimeType)
            ?? throw new InvalidOperationException($"Unsupported media type: {mimeType}");

        var bandDir = Path.Combine(catalogRootPath, bandId.ToString());
        Directory.CreateDirectory(bandDir);

        var stem = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..24];
        var fileName = $"{stem}.{ExtForMimeType(mimeType, originalFilename)}";
        var filePath = Path.Combine(bandDir, fileName);
        await File.WriteAllBytesAsync(filePath, buffer);

        string? thumbnailRelPath = null;
        int? width = null, height = null;

        if (mediaType == MediaType.Image)
        {
            try
            {
                using var image = SKBitmap.Decode(buffer);
                if (image is not null)
                {
                    width = image.Width;
                    height = image.Height;
                    var scale = Math.Min(1.0, (double)ThumbMaxWidth / image.Width);
                    var w = Math.Max(1, (int)Math.Round(image.Width * scale));
                    var h = Math.Max(1, (int)Math.Round(image.Height * scale));

                    using var resized = image.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default);
                    if (resized is not null)
                    {
                        using var skImage = SKImage.FromBitmap(resized);
                        using var thumbData = skImage.Encode(SKEncodedImageFormat.Jpeg, 85);

                        var thumbsDir = Path.Combine(bandDir, "thumbs");
                        Directory.CreateDirectory(thumbsDir);
                        var thumbName = $"{stem}.jpg";
                        var thumbPath = Path.Combine(thumbsDir, thumbName);
                        await File.WriteAllBytesAsync(thumbPath, thumbData.ToArray());
                        thumbnailRelPath = RelativeToRoot(thumbPath);
                    }
                }
            }
            catch
            {
                // Non-fatal - see doc comment.
            }
        }

        var item = new CatalogItem
        {
            BandId = bandId,
            MediaType = mediaType,
            FilePath = RelativeToRoot(filePath),
            ThumbnailPath = thumbnailRelPath,
            OriginalFilename = originalFilename,
            Label = label ?? originalFilename,
            MimeType = mimeType,
            FileSize = buffer.Length,
            Width = width,
            Height = height,
            Source = source,
            SourceUrl = sourceUrl,
            UploadedBy = uploadedBy,
            Category = category
        };
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    // Stored web-facing, literally prefixed "data/catalog/" regardless of
    // catalogRootPath's actual configured location - wwwroot/assets/catalog.js's
    // catalogFileUrl() strips exactly that prefix and swaps in "catalog-files/"
    // (matching the old app's own path convention, and the same pattern
    // already used for Artifact.FilePath/"data/uploads/").
    private string RelativeToRoot(string fullPath) =>
        "data/catalog/" + Path.GetRelativePath(catalogRootPath, fullPath).Replace('\\', '/');

    public string ResolveFullPath(CatalogItem item)
    {
        var relative = item.FilePath.StartsWith("data/catalog/") ? item.FilePath["data/catalog/".Length..] : item.FilePath;
        return Path.Combine(catalogRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public async Task<byte[]> GetCatalogItemBufferAsync(Guid bandId, Guid id)
    {
        var item = await db.CatalogItems.FirstOrDefaultAsync(c => c.Id == id && c.BandId == bandId)
            ?? throw new InvalidOperationException("Catalog item not found.");
        return await File.ReadAllBytesAsync(ResolveFullPath(item));
    }

    public async Task<List<CatalogItem>> ListCatalogItemsAsync(Guid bandId, string? q, MediaType? mediaType, CatalogCategory? category = null, int limit = 500, int offset = 0)
    {
        var query = db.CatalogItems.Where(c => c.BandId == bandId);
        if (mediaType is not null) query = query.Where(c => c.MediaType == mediaType);
        if (category is not null) query = query.Where(c => c.Category == category);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q}%";
            query = query.Where(c => EF.Functions.ILike(c.Label ?? "", like) || EF.Functions.ILike(c.OriginalFilename ?? "", like));
        }
        return await query.OrderByDescending(c => c.CreatedAt).Skip(offset).Take(limit).ToListAsync();
    }

    public async Task<CatalogItem?> UpdateCatalogItemLabelAsync(Guid bandId, Guid id, string label)
    {
        var item = await db.CatalogItems.FirstOrDefaultAsync(c => c.Id == id && c.BandId == bandId);
        if (item is null) return null;
        item.Label = label;
        await db.SaveChangesAsync();
        return item;
    }

    // Deletes one row (and its files) at a time rather than a single
    // RemoveRange+SaveChanges, so one row's unexpected failure doesn't
    // silently roll back every other item in the same request. Deleting an
    // image that's a Flyer's SourceCatalogItem is allowed (that FK is
    // SetNull, not Restrict - see ApplicationDbContext) - the frontend
    // warns about it beforehand using the used_in_flyers list
    // (CatalogController), this is just a safety net for any other
    // unexpected FK failure. Files are only deleted from disk after the DB
    // row is confirmed gone - deleting the file first (the previous
    // behavior) left an orphaned, still-referenced row pointing at nothing
    // once the DB delete failed.
    public async Task<(int Deleted, List<string> Errors)> DeleteCatalogItemsAsync(Guid bandId, IEnumerable<Guid> ids)
    {
        var items = await db.CatalogItems.Where(c => c.BandId == bandId && ids.Contains(c.Id)).ToListAsync();
        var errors = new List<string>();
        var deletedCount = 0;

        foreach (var item in items)
        {
            db.CatalogItems.Remove(item);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.Entry(item).State = EntityState.Unchanged;
                errors.Add($"\"{item.Label ?? item.OriginalFilename}\" could not be deleted - it's still referenced elsewhere.");
                continue;
            }

            deletedCount++;
            try { File.Delete(ResolveFullPath(item)); } catch { /* best-effort */ }
            if (item.ThumbnailPath is not null)
            {
                var thumbRelative = item.ThumbnailPath.StartsWith("data/catalog/") ? item.ThumbnailPath["data/catalog/".Length..] : item.ThumbnailPath;
                try { File.Delete(Path.Combine(catalogRootPath, thumbRelative.Replace('/', Path.DirectorySeparatorChar))); } catch { /* best-effort */ }
            }
        }
        return (deletedCount, errors);
    }

    /// <summary>Paste-a-URL mode: fetches, validates, and returns bytes -
    /// never touches the Catalog itself (the caller decides whether/how
    /// to register it).</summary>
    public async Task<(byte[] Buffer, string MimeType, string? OriginalFilename)> FetchUrlAsBufferAsync(string url, long maxBytes = DefaultMaxUrlBytes)
    {
        Uri parsed;
        try { parsed = new Uri(url); }
        catch { throw new InvalidOperationException("That is not a valid URL."); }
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Only http/https URLs are supported.");

        HttpResponseMessage res;
        try { res = await http.GetAsync(parsed); }
        catch (Exception ex) { throw new InvalidOperationException($"Could not reach that URL: {ex.Message}"); }
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Could not fetch that URL (HTTP {(int)res.StatusCode}).");

        var contentType = (res.Content.Headers.ContentType?.MediaType ?? "").Trim().ToLowerInvariant();
        if (MediaTypeForMime(contentType) is null)
            throw new InvalidOperationException($"That URL isn't an image, video, or audio file (got \"{(contentType.Length > 0 ? contentType : "an unknown type")}\").");

        var contentLength = res.Content.Headers.ContentLength;
        if (contentLength is not null && contentLength > maxBytes)
            throw new InvalidOperationException($"File is too large (max {maxBytes / 1024 / 1024}MB).");

        var buffer = await res.Content.ReadAsByteArrayAsync();
        if (buffer.Length > maxBytes) throw new InvalidOperationException($"File is too large (max {maxBytes / 1024 / 1024}MB).");
        if (buffer.Length == 0) throw new InvalidOperationException("That URL returned an empty file.");

        // Cheap insurance against a spoofed content-type on an image:
        // round-trip the bytes through the image decoder, which throws on
        // anything it can't decode.
        if (contentType.StartsWith("image/"))
        {
            using var decoded = SKBitmap.Decode(buffer);
            if (decoded is null) throw new InvalidOperationException("That URL did not contain a valid, decodable image.");
        }

        var originalFilename = Path.GetFileName(parsed.LocalPath);
        return (buffer, contentType, string.IsNullOrEmpty(originalFilename) ? null : originalFilename);
    }

    /// <summary>The single entry point every upload route in the app
    /// calls instead of reading the raw multipart form directly - ported
    /// from the old app's resolveMediaInput. Each request only ever
    /// carries one media input, so the three modes share plain,
    /// unprefixed field names (file / catalogItemId / url). Returns a
    /// resolved buffer with Mode == "none" if nothing was provided.
    ///
    /// Auto-registration (every future upload anywhere becomes a Catalog
    /// entry with zero extra steps) happens here for the 'file' and 'url'
    /// modes only - a catalogItemId pick is already a Catalog row, so
    /// it's never re-registered. That's what keeps copy semantics honest:
    /// the destination always gets an independent byte-copy, and the
    /// Catalog row itself is only ever touched by the Catalog routes (or
    /// here, on first creation).</summary>
    public async Task<ResolvedMedia?> ResolveMediaInputAsync(
        Guid bandId, UploadedFilePayload? file, string? catalogItemId, string? url,
        MediaType? expectedMediaType, string? uploadedBy, long maxBytes = DefaultMaxUrlBytes)
    {
        if (file is not null && file.Bytes.Length > 0)
        {
            var mimeType = file.MimeType;
            if (expectedMediaType is not null && MediaTypeForMime(mimeType) != expectedMediaType)
                throw new InvalidOperationException($"Expected a {expectedMediaType.ToString()!.ToLowerInvariant()} file, got {mimeType}");

            try { await RegisterCatalogItemAsync(bandId, file.Bytes, mimeType, file.FileName, CatalogSource.Upload, null, uploadedBy); }
            catch { /* best-effort, same as the old app - a registration failure shouldn't block the actual upload */ }
            return new ResolvedMedia(file.Bytes, mimeType, ExtForMimeType(mimeType, file.FileName), "file");
        }

        if (!string.IsNullOrEmpty(catalogItemId))
        {
            if (!Guid.TryParse(catalogItemId, out var id)) throw new InvalidOperationException("Invalid catalogItemId");
            var item = await db.CatalogItems.FirstOrDefaultAsync(c => c.Id == id && c.BandId == bandId)
                ?? throw new InvalidOperationException("Catalog item not found.");
            if (expectedMediaType is not null && item.MediaType != expectedMediaType)
                throw new InvalidOperationException($"That Catalog item is {item.MediaType.ToString()!.ToLowerInvariant()}, not {expectedMediaType.ToString()!.ToLowerInvariant()}");
            var buffer = await File.ReadAllBytesAsync(ResolveFullPath(item));
            return new ResolvedMedia(buffer, item.MimeType, ExtForMimeType(item.MimeType, item.OriginalFilename), "catalog");
        }

        if (!string.IsNullOrEmpty(url))
        {
            var (buffer, mimeType, originalFilename) = await FetchUrlAsBufferAsync(url, maxBytes);
            if (expectedMediaType is not null && MediaTypeForMime(mimeType) != expectedMediaType)
                throw new InvalidOperationException($"Expected a {expectedMediaType.ToString()!.ToLowerInvariant()} URL, got {mimeType}");
            try { await RegisterCatalogItemAsync(bandId, buffer, mimeType, originalFilename, CatalogSource.Url, url, uploadedBy); }
            catch { /* best-effort */ }
            return new ResolvedMedia(buffer, mimeType, ExtForMimeType(mimeType, originalFilename), "url");
        }

        return null;
    }
}
