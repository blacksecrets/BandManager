using BandManager.Data.Entities;
using SkiaSharp;
using static BandManager.Data.Services.SiteTextEditing;

namespace BandManager.Data.Services;

/// <summary>
/// Edits a Band's own public site's js/gallery.js - ported from the old
/// app's galleryEditor.js. Same technique as MediaSiteEditor, with one
/// difference: a gallery entry needs both a full-size photo and a
/// thumbnail, generated here (SkiaSharp, matching ImageTools.cs) rather
/// than asking for two uploads.
/// </summary>
public class GallerySiteEditor(GitHubSiteClient gitHub, GallerySource gallerySource)
{
    private const string FilePath = "js/gallery.js";
    private const int ThumbMaxWidth = 200; // matches every existing hand-made thumbnail

    private static TextBlock? FindGalleryBlock(string code, GalleryImage image) =>
        !string.IsNullOrEmpty(image.Id) ? FindBlock(code, "id", image.Id) : FindBlock(code, "alt", image.Alt ?? "");

    /// <summary>Always outputs a JPEG thumbnail regardless of the source
    /// format - a fixed, predictable output rather than trying to
    /// preserve/re-encode whatever format was uploaded, matching every
    /// existing hand-made thumb.</summary>
    private static byte[] GenerateThumbnail(byte[] buffer)
    {
        using var source = SKBitmap.Decode(buffer) ?? throw new InvalidOperationException("Could not decode the source image.");
        var scale = Math.Min(1.0, (double)ThumbMaxWidth / source.Width);
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var resized = source.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default)
            ?? throw new InvalidOperationException("Could not resize the source image.");
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>fields: {alt} - only key currently supported. Photo
    /// replacement is a separate method (ReplaceGalleryPhotoAsync), since
    /// it needs to regenerate the thumbnail too.</summary>
    public async Task UpdateGalleryImageAsync(Band band, GalleryImage item, Dictionary<string, string> fields)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read gallery.js from the site repo.");

        var block = FindGalleryBlock(file.Content, item)
            ?? throw new InvalidOperationException($"Couldn't find \"{item.Id ?? item.Alt}\" in gallery.js - it may have already changed on the site.");

        var newBlock = block.Text;
        foreach (var (field, value) in fields)
        {
            newBlock = SetField(newBlock, field, value);
        }

        var newContent = file.Content[..block.Start] + newBlock + file.Content[block.End..];
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Update \"{fields.GetValueOrDefault("alt", item.Alt)}\" gallery image", file.Sha);
        gallerySource.InvalidateCache(band.Id);
    }

    /// <summary>fullBytes: the uploaded photo, as-is. Generates and
    /// uploads the thumbnail too. Returns the new entry's id, thumb path,
    /// and full path.</summary>
    public async Task<(string Id, string Thumb, string Full)> AddGalleryImageAsync(Band band, string alt, byte[] fullBytes, string ext)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read gallery.js from the site repo.");

        var bounds = FindArrayBounds(file.Content, "galleryImages")
            ?? throw new InvalidOperationException("Could not find the galleryImages array in gallery.js.");
        var arrayText = file.Content[bounds.Start..bounds.End];

        var id = UniqueId(file.Content, Slugify(alt, "gallery-image"));
        var full = $"images/gallery/{id}.{ext}";
        var thumb = $"images/gallery/thumbs/{id}-thumb.jpg";

        var thumbBytes = GenerateThumbnail(fullBytes);
        await gitHub.PutBinaryFileAsync(band, full, fullBytes, $"Add gallery photo \"{alt}\"");
        await gitHub.PutBinaryFileAsync(band, thumb, thumbBytes, $"Add gallery thumbnail \"{alt}\"");

        var blockText = string.Join('\n',
            "    {",
            $"        id: \"{id}\",",
            $"        thumb: \"{thumb}\",",
            $"        full: \"{full}\",",
            $"        alt: \"{EscapeForQuotes(alt)}\"",
            "    }");

        var newArrayText = InsertBeforeClose(arrayText, ']', blockText)
            ?? throw new InvalidOperationException("Could not figure out where to insert the new gallery image.");

        var newContent = file.Content[..bounds.Start] + newArrayText + file.Content[bounds.End..];
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Add \"{alt}\" gallery image", file.Sha);
        gallerySource.InvalidateCache(band.Id);
        return (id, thumb, full);
    }

    /// <summary>Replaces an existing entry's photo in place - same full/
    /// thumb paths, so gallery.js's own fields don't need editing.</summary>
    public async Task ReplaceGalleryPhotoAsync(Band band, GalleryImage item, byte[] fullBytes)
    {
        var thumbBytes = GenerateThumbnail(fullBytes);
        var fullSha = await gitHub.GetFileShaAsync(band, item.Full!);
        var thumbSha = await gitHub.GetFileShaAsync(band, item.Thumb!);
        await gitHub.PutBinaryFileAsync(band, item.Full!, fullBytes, $"Update gallery photo \"{item.Alt}\"", fullSha);
        await gitHub.PutBinaryFileAsync(band, item.Thumb!, thumbBytes, $"Update gallery thumbnail \"{item.Alt}\"", thumbSha);
    }

    public async Task DeleteGalleryImageAsync(Band band, GalleryImage item)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read gallery.js from the site repo.");

        var block = FindGalleryBlock(file.Content, item)
            ?? throw new InvalidOperationException($"Couldn't find \"{item.Id ?? item.Alt}\" in gallery.js - it may have already changed on the site.");

        var newContent = RemoveBlock(file.Content, block);
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Remove \"{item.Alt}\" gallery image", file.Sha);
        gallerySource.InvalidateCache(band.Id);
    }
}
