using BandManager.Data.Entities;
using SkiaSharp;
using static BandManager.Data.Services.SiteTextEditing;

namespace BandManager.Data.Services;

/// <summary>
/// Publishes a DB GalleryImage row (the source of truth - see
/// GalleryImage.cs) to a Band's public site's js/gallery.js, best-effort
/// and one-way - mirrors GigsSiteEditor's PublishGigAsync (always
/// rewrites the whole block from the DB row's current state). Thumbnail
/// generation (SkiaSharp, matching ImageTools.cs) stays here as a pure,
/// reusable helper - a gallery entry needs both a full-size photo and a
/// thumbnail, generated rather than asking for two uploads.
/// </summary>
public class GallerySiteEditor(GitHubSiteClient gitHub)
{
    private const string FilePath = "js/gallery.js";
    private const int ThumbMaxWidth = 200; // matches every existing hand-made thumbnail

    /// <summary>Always outputs a JPEG thumbnail regardless of the source
    /// format - a fixed, predictable output rather than trying to
    /// preserve/re-encode whatever format was uploaded, matching every
    /// existing hand-made thumb.</summary>
    public static byte[] GenerateThumbnail(byte[] buffer)
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

    /// <summary>Writes item's current full state to gallery.js - replacing
    /// its existing block (matched by id == item.Ref) if present, else
    /// appending a new one.</summary>
    public async Task PublishGalleryImageAsync(Band band, GalleryImage item)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read gallery.js from the site repo.");

        var blockText = string.Join('\n',
            "    {",
            $"        id: \"{EscapeForQuotes(item.Ref)}\",",
            $"        thumb: \"{EscapeForQuotes(item.Thumb ?? "")}\",",
            $"        full: \"{EscapeForQuotes(item.Full ?? "")}\",",
            $"        alt: \"{EscapeForQuotes(item.Alt ?? "")}\"",
            "    }");

        var existingBlock = FindBlock(file.Content, "id", item.Ref);
        string newContent;
        if (existingBlock is not null)
        {
            newContent = file.Content[..existingBlock.Start] + blockText + file.Content[existingBlock.End..];
        }
        else
        {
            var bounds = FindArrayBounds(file.Content, "galleryImages")
                ?? throw new InvalidOperationException("Could not find the galleryImages array in gallery.js.");
            var arrayText = file.Content[bounds.Start..bounds.End];
            var newArrayText = InsertBeforeClose(arrayText, ']', blockText)
                ?? throw new InvalidOperationException("Could not figure out where to insert the gallery image.");
            newContent = file.Content[..bounds.Start] + newArrayText + file.Content[bounds.End..];
        }

        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Update \"{item.Alt}\" gallery image", file.Sha);
    }

    /// <summary>Removes item's block from gallery.js, if present - a
    /// no-op if the site never had it.</summary>
    public async Task UnpublishGalleryImageAsync(Band band, string galleryRef, string? alt)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read gallery.js from the site repo.");

        var block = FindBlock(file.Content, "id", galleryRef);
        if (block is null) return;

        var newContent = RemoveBlock(file.Content, block);
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Remove \"{alt}\" gallery image", file.Sha);
    }
}
