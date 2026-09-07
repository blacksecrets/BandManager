using BandManager.Data.Entities;
using SkiaSharp;

namespace BandManager.Data.Services;

/// <summary>
/// Composites a flyer's background image with its positioned text/image
/// fields into a final raster - the first text/shape-drawing code in this
/// codebase (ImageTools.MakeFacebookCoverPhoto only ever letterboxes one
/// bitmap). Kept as a pure static function like ImageTools, with the logo
/// lookup passed in as a delegate rather than a direct CatalogStore
/// dependency, so it stays testable/reusable without a DB context.
/// Encodes PNG, not JPEG: flyers are photo + crisp vector text, and JPEG's
/// chroma subsampling visibly smears small text edges - a cost that
/// doesn't apply to ImageTools' pure-photo letterboxing use case.
/// </summary>
public static class FlyerRenderer
{
    public static byte[] RenderFlyer(byte[] backgroundBytes, List<FlyerFieldDef> fields, string fontsRootPath, Func<string, byte[]?>? logoResolver)
    {
        SKBitmap? decoded;
        try
        {
            decoded = SKBitmap.Decode(backgroundBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not decode the flyer background.", ex);
        }
        using var background = decoded ?? throw new InvalidOperationException("Could not decode the flyer background.");

        using var surface = SKSurface.Create(new SKImageInfo(background.Width, background.Height));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(background, 0, 0, new SKSamplingOptions());

        foreach (var field in fields.Where(f => f.DefaultVisible && !string.IsNullOrEmpty(f.Value)))
        {
            if (field.Type == FlyerFieldType.Image)
            {
                DrawLogoField(canvas, background, field, logoResolver);
                continue;
            }

            var fontSizePx = (float)((field.FontSize ?? 0.04) * background.Height);
            var typeface = FlyerFonts.LoadTypeface(fontsRootPath, field.FontFamily);
            using var font = new SKFont(typeface, fontSizePx);
            using var paint = new SKPaint { Color = ParseColor(field.Color), IsAntialias = true };
            canvas.DrawText(field.Value, (float)(field.X * background.Width), (float)(field.Y * background.Height), SKTextAlign.Left, font, paint);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawLogoField(SKCanvas canvas, SKBitmap background, FlyerFieldDef field, Func<string, byte[]?>? logoResolver)
    {
        if (logoResolver is null) return;
        var logoBytes = logoResolver(field.Value!);
        if (logoBytes is null) return;
        using var logoBmp = SKBitmap.Decode(logoBytes);
        if (logoBmp is null) return;

        var targetHeight = (int)((field.FontSize ?? 0.1) * background.Height);
        var targetWidth = (int)(targetHeight * (logoBmp.Width / (double)logoBmp.Height));
        using var resizedLogo = logoBmp.Resize(new SKImageInfo(Math.Max(1, targetWidth), Math.Max(1, targetHeight)), SKSamplingOptions.Default);
        if (resizedLogo is null) return;

        canvas.DrawBitmap(resizedLogo, (float)(field.X * background.Width), (float)(field.Y * background.Height), new SKSamplingOptions());
    }

    private static SKColor ParseColor(string? hex) =>
        !string.IsNullOrEmpty(hex) && SKColor.TryParse(hex, out var c) ? c : SKColors.Black;
}
