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
    public static byte[] RenderFlyer(byte[] backgroundBytes, List<FlyerFieldDef> fields, string fontsRootPath, Func<string, byte[]?>? logoResolver, Func<string, string?>? customFontPathResolver = null)
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

            DrawTextField(canvas, background, field, fontsRootPath, customFontPathResolver);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // Bold/Italic/Underline are synthetic (Embolden, a canvas skew, and a
    // manually-drawn line) rather than switching to a different font file -
    // most of the 7 bundled fonts are single-weight display faces with no
    // separate bold/italic variant bundled to switch to.
    private static void DrawTextField(SKCanvas canvas, SKBitmap background, FlyerFieldDef field, string fontsRootPath, Func<string, string?>? customFontPathResolver)
    {
        var fontSizePx = (float)((field.FontSize ?? 0.04) * background.Height);
        var customFontPath = field.FontFamily is not null ? customFontPathResolver?.Invoke(field.FontFamily) : null;
        var typeface = FlyerFonts.LoadTypeface(fontsRootPath, field.FontFamily, customFontPath);
        using var font = new SKFont(typeface, fontSizePx) { Embolden = field.Bold };
        using var paint = new SKPaint { Color = ParseColor(field.Color), IsAntialias = true };

        var x = (float)(field.X * background.Width);
        var y = (float)(field.Y * background.Height);

        canvas.Save();
        if (field.Rotation != 0) canvas.RotateDegrees((float)field.Rotation, x, y);
        if (field.Italic)
        {
            // Classic fake-italic: shear the canvas around the text's own
            // origin so the skew doesn't also shift its position.
            canvas.Translate(x, y);
            canvas.Skew(-0.25f, 0);
            canvas.Translate(-x, -y);
        }

        canvas.DrawText(field.Value, x, y, SKTextAlign.Left, font, paint);

        if (field.Underline)
        {
            var width = font.MeasureText(field.Value);
            font.GetFontMetrics(out var metrics);
            var underlineY = y + (metrics.UnderlinePosition ?? fontSizePx * 0.1f);
            var thickness = metrics.UnderlineThickness ?? Math.Max(1f, fontSizePx * 0.05f);
            using var linePaint = new SKPaint { Color = ParseColor(field.Color), IsAntialias = true, StrokeWidth = thickness };
            canvas.DrawLine(x, underlineY, x + width, underlineY, linePaint);
        }

        canvas.Restore();
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

        var x = (float)(field.X * background.Width);
        var y = (float)(field.Y * background.Height);

        canvas.Save();
        if (field.Rotation != 0) canvas.RotateDegrees((float)field.Rotation, x, y);
        canvas.DrawBitmap(resizedLogo, x, y, new SKSamplingOptions());
        canvas.Restore();
    }

    private static SKColor ParseColor(string? hex) =>
        !string.IsNullOrEmpty(hex) && SKColor.TryParse(hex, out var c) ? c : SKColors.Black;
}
