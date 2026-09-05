using SkiaSharp;

namespace BandManager.Data.Services;

/// <summary>
/// Ported from the old app's src/imageTools.js (Jimp -> SkiaSharp - not
/// SixLabors.ImageSharp, whose 4.x line pushes a paid commercial license;
/// SkiaSharp is Apache 2.0, unambiguously free, which matters for
/// something meant to actually run cheaply). Same letterboxing math.
/// </summary>
public static class ImageTools
{
    public const int FbCoverWidth = 820;
    public const int FbCoverHeight = 312;

    /// <summary>Fits the source image into Facebook's cover photo box,
    /// letterboxed/pillarboxed with black wherever it doesn't fill the
    /// box, and returns the result as JPEG bytes.</summary>
    public static byte[] MakeFacebookCoverPhoto(byte[] inputBytes)
    {
        using var source = SKBitmap.Decode(inputBytes)
            ?? throw new InvalidOperationException("Could not decode the source image.");

        var scale = Math.Min((double)FbCoverWidth / source.Width, (double)FbCoverHeight / source.Height);
        var newW = Math.Max(1, (int)Math.Round(source.Width * scale));
        var newH = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = source.Resize(new SKImageInfo(newW, newH), SKSamplingOptions.Default)
            ?? throw new InvalidOperationException("Could not resize the source image.");

        var x = (FbCoverWidth - newW) / 2;
        var y = (FbCoverHeight - newH) / 2;

        using var surface = SKSurface.Create(new SKImageInfo(FbCoverWidth, FbCoverHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.DrawBitmap(resized, x, y, new SKSamplingOptions());
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
