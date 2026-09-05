using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace BandManager.Data.Services;

/// <summary>
/// The Media Catalog: a persistent, per-Band, browsable library of every
/// image/video/audio file this Band's users have touched. Ported from the
/// old app's src/catalogStore.js - registerCatalogItem is the single write
/// path (called directly by the Catalog's own routes, and by Cover Photo
/// generation below). catalogRootPath is a plain constructor parameter
/// (not resolved internally), same pattern as AesGcmCredentialCipher's
/// keyFilePath - resolved from IWebHostEnvironment.ContentRootPath by the
/// Web project's DI registration.
///
/// Not yet ported: the general resolveMediaInput dispatcher that the old
/// app wired into every OTHER upload spot (item artifacts, gig flyers,
/// etc.) so those could pick-from-catalog or paste-a-URL too - this is
/// registration + browse/list/delete only for now, enough for Cover Photo
/// and the Catalog page itself.
/// </summary>
public class CatalogStore(ApplicationDbContext db, string catalogRootPath)
{
    private const int ThumbMaxWidth = 200; // matches the old app's generateThumbnail

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
        CatalogSource source, string? sourceUrl, string? uploadedBy, string? label = null)
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
            UploadedBy = uploadedBy
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

    public async Task<List<CatalogItem>> ListCatalogItemsAsync(Guid bandId, string? q, MediaType? mediaType, int limit = 500, int offset = 0)
    {
        var query = db.CatalogItems.Where(c => c.BandId == bandId);
        if (mediaType is not null) query = query.Where(c => c.MediaType == mediaType);
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

    public async Task<int> DeleteCatalogItemsAsync(Guid bandId, IEnumerable<Guid> ids)
    {
        var items = await db.CatalogItems.Where(c => c.BandId == bandId && ids.Contains(c.Id)).ToListAsync();
        foreach (var item in items)
        {
            try { File.Delete(ResolveFullPath(item)); } catch { /* best-effort */ }
            if (item.ThumbnailPath is not null)
            {
                var thumbRelative = item.ThumbnailPath.StartsWith("data/catalog/") ? item.ThumbnailPath["data/catalog/".Length..] : item.ThumbnailPath;
                try { File.Delete(Path.Combine(catalogRootPath, thumbRelative.Replace('/', Path.DirectorySeparatorChar))); } catch { /* best-effort */ }
            }
        }
        db.CatalogItems.RemoveRange(items);
        await db.SaveChangesAsync();
        return items.Count;
    }
}
