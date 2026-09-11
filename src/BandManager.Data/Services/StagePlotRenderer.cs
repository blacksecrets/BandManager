using SkiaSharp;

namespace BandManager.Data.Services;

public record StagePlotItemDef(int VisibleId, double X, double Y, double Rotation);

/// <summary>
/// Draws a Stage Plot's diagram server-side - a generic stage rectangle
/// with the same Upstage/Downstage x Left/Center/Right zone language the
/// real Tech Rider PDF already uses, plus a numbered circle per placed
/// item. Deliberately black-and-white/high-contrast rather than
/// photo-based or color-coded: stage-plot-tool research specifically
/// flags this as more legible in print and dim backstage lighting than
/// the current PDF's colorful photo plot. Matches the in-app drag
/// editor's own visual language exactly (see stagePlotEditor.css) -
/// same shape, same numbered-circle-badge convention - so the live
/// preview and the published/printed document never diverge.
/// </summary>
public static class StagePlotRenderer
{
    private const int Width = 1600;
    private const int Height = 900; // 16:9, matches stagePlotEditor.css's aspect-ratio
    private const float ItemRadius = 26;

    public static byte[] RenderStagePlot(List<StagePlotItemDef> items, string fontsRootPath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xF5, 0xF5, 0xF0));

        using var borderPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 6, IsStroke = true, IsAntialias = true };
        canvas.DrawRect(3, 3, Width - 6, Height - 6, borderPaint);

        var labelTypeface = FlyerFonts.LoadTypeface(fontsRootPath, "roboto-regular");
        using var labelFont = new SKFont(labelTypeface, 20);
        using var labelPaint = new SKPaint { Color = new SKColor(0x99, 0x99, 0x99), IsAntialias = true };

        canvas.DrawText("UPSTAGE LEFT", 20, 34, SKTextAlign.Left, labelFont, labelPaint);
        canvas.DrawText("UPSTAGE CENTER", Width / 2f, 34, SKTextAlign.Center, labelFont, labelPaint);
        canvas.DrawText("UPSTAGE RIGHT", Width - 20, 34, SKTextAlign.Right, labelFont, labelPaint);
        canvas.DrawText("DOWNSTAGE LEFT", 20, Height - 20, SKTextAlign.Left, labelFont, labelPaint);
        canvas.DrawText("DOWNSTAGE CENTER", Width / 2f, Height - 20, SKTextAlign.Center, labelFont, labelPaint);
        canvas.DrawText("DOWNSTAGE RIGHT", Width - 20, Height - 20, SKTextAlign.Right, labelFont, labelPaint);

        using var circleFillPaint = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22), IsAntialias = true };
        using var circleBorderPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, IsStroke = true, IsAntialias = true };
        var badgeTypeface = FlyerFonts.LoadTypeface(fontsRootPath, "oswald-bold");
        using var badgeFont = new SKFont(badgeTypeface, 28);
        using var badgePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        foreach (var item in items)
        {
            var x = (float)(item.X * Width);
            var y = (float)(item.Y * Height);

            // Rotation only orients the marker itself (matching the in-app
            // editor's own rotate handle) - the badge number stays
            // upright and centered so it's always readable regardless.
            canvas.Save();
            if (item.Rotation != 0) canvas.RotateDegrees((float)item.Rotation, x, y);
            canvas.DrawCircle(x, y, ItemRadius, circleFillPaint);
            canvas.DrawCircle(x, y, ItemRadius, circleBorderPaint);
            canvas.Restore();

            var text = item.VisibleId.ToString();
            canvas.DrawText(text, x, y + 10, SKTextAlign.Center, badgeFont, badgePaint);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
