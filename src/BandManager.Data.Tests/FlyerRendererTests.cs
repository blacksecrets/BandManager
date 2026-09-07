using BandManager.Data.Entities;
using BandManager.Data.Services;
using SkiaSharp;
using Xunit;

namespace BandManager.Data.Tests;

/// <summary>
/// FlyerRenderer is the first text/image-compositing code in this app
/// (ImageTools only ever letterboxes a single bitmap) - these tests use the
/// real bundled font files (the same ones the live app serves from
/// wwwroot/fonts and the ones the client-side preview's @font-face rules
/// point at), so a renamed/missing/corrupt font file fails a fast unit test
/// instead of only showing up as silently-wrong text on a real flyer.
/// </summary>
public class FlyerRendererTests
{
    // Walks up from the test assembly's output directory to the repo's
    // src/ folder rather than hardcoding a drive-specific path, so this
    // still works if the repo is checked out somewhere else.
    private static readonly string FontsRootPath = LocateFontsRoot();

    private static string LocateFontsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "BandManager.Web")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Could not locate src/BandManager.Web from the test output directory.");
        return Path.Combine(dir.FullName, "BandManager.Web", "wwwroot", "fonts");
    }

    private static byte[] MakeSolidBackground(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void RenderFlyer_ProducesAValidPngAtTheBackgroundsOwnDimensions()
    {
        var background = MakeSolidBackground(400, 600, SKColors.Black);
        var fields = new List<FlyerFieldDef>
        {
            new("title", "Title", FlyerFieldType.Text, 0.1, 0.1, 0.05, "oswald-bold", "#ffffff", true, "Test Gig Title")
        };

        var result = FlyerRenderer.RenderFlyer(background, fields, FontsRootPath, logoResolver: null);

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(400, decoded!.Width);
        Assert.Equal(600, decoded.Height);
    }

    [Fact]
    public void RenderFlyer_WithNoFields_StillProducesABackgroundOnlyImage()
    {
        var background = MakeSolidBackground(200, 200, SKColors.Blue);

        var result = FlyerRenderer.RenderFlyer(background, [], FontsRootPath, logoResolver: null);

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        // Center pixel should still be the background color - nothing drew over it.
        Assert.Equal(SKColors.Blue, decoded!.GetPixel(100, 100));
    }

    [Fact]
    public void RenderFlyer_SkipsFieldsMarkedNotVisible()
    {
        var background = MakeSolidBackground(300, 300, SKColors.Black);
        var fields = new List<FlyerFieldDef>
        {
            new("hidden", "Hidden", FlyerFieldType.Text, 0.1, 0.1, 0.1, "oswald-bold", "#ffffff", DefaultVisible: false, Value: "Should not appear")
        };

        var withHidden = FlyerRenderer.RenderFlyer(background, fields, FontsRootPath, logoResolver: null);
        var withNoFields = FlyerRenderer.RenderFlyer(background, [], FontsRootPath, logoResolver: null);

        // A hidden field must render byte-identically to no field at all -
        // proves it was genuinely skipped, not just drawn transparently.
        Assert.Equal(withNoFields, withHidden);
    }

    [Fact]
    public void RenderFlyer_SkipsFieldsWithNoValue()
    {
        var background = MakeSolidBackground(300, 300, SKColors.Black);
        var fields = new List<FlyerFieldDef>
        {
            new("empty", "Empty", FlyerFieldType.Text, 0.1, 0.1, 0.1, "oswald-bold", "#ffffff", DefaultVisible: true, Value: null)
        };

        var result = FlyerRenderer.RenderFlyer(background, fields, FontsRootPath, logoResolver: null);
        var baseline = FlyerRenderer.RenderFlyer(background, [], FontsRootPath, logoResolver: null);

        Assert.Equal(baseline, result);
    }

    [Fact]
    public void RenderFlyer_DrawingText_ActuallyChangesPixels()
    {
        var background = MakeSolidBackground(400, 200, SKColors.Black);
        var fields = new List<FlyerFieldDef>
        {
            new("title", "Title", FlyerFieldType.Text, 0.05, 0.4, 0.3, "anton", "#ffffff", true, "TEST")
        };

        var result = FlyerRenderer.RenderFlyer(background, fields, FontsRootPath, logoResolver: null);
        var baseline = FlyerRenderer.RenderFlyer(background, [], FontsRootPath, logoResolver: null);

        Assert.NotEqual(baseline, result);
    }

    [Theory]
    [InlineData(true, false, false)] // Bold
    [InlineData(false, true, false)] // Italic
    [InlineData(false, false, true)] // Underline
    public void RenderFlyer_EachStyleToggle_ChangesPixelsComparedToPlainText(bool bold, bool italic, bool underline)
    {
        var background = MakeSolidBackground(400, 200, SKColors.Black);
        var plainFields = new List<FlyerFieldDef>
        {
            new("title", "Title", FlyerFieldType.Text, 0.05, 0.4, 0.3, "anton", "#ffffff", true, "TEST")
        };
        var styledFields = new List<FlyerFieldDef>
        {
            new("title", "Title", FlyerFieldType.Text, 0.05, 0.4, 0.3, "anton", "#ffffff", true, "TEST", bold, italic, underline)
        };

        var plain = FlyerRenderer.RenderFlyer(background, plainFields, FontsRootPath, logoResolver: null);
        var styled = FlyerRenderer.RenderFlyer(background, styledFields, FontsRootPath, logoResolver: null);

        Assert.NotEqual(plain, styled);
    }

    [Fact]
    public void RenderFlyer_StyleFlags_DefaultToFalse_RenderingIdenticallyToExplicitFalse()
    {
        var background = MakeSolidBackground(300, 150, SKColors.Black);
        var implicitFields = new List<FlyerFieldDef>
        {
            new("title", "Title", FlyerFieldType.Text, 0.05, 0.4, 0.3, "anton", "#ffffff", true, "TEST")
        };
        var explicitFields = new List<FlyerFieldDef>
        {
            new("title", "Title", FlyerFieldType.Text, 0.05, 0.4, 0.3, "anton", "#ffffff", true, "TEST", Bold: false, Italic: false, Underline: false)
        };

        var a = FlyerRenderer.RenderFlyer(background, implicitFields, FontsRootPath, logoResolver: null);
        var b = FlyerRenderer.RenderFlyer(background, explicitFields, FontsRootPath, logoResolver: null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void RenderFlyer_ImageField_CompositesTheResolvedLogoBytes()
    {
        var background = MakeSolidBackground(400, 400, SKColors.Black);
        var logoBytes = MakeSolidBackground(50, 50, SKColors.Yellow);
        var fields = new List<FlyerFieldDef>
        {
            new("logo", "Logo", FlyerFieldType.Image, 0.1, 0.1, 0.1, null, null, true, "some-catalog-item-id")
        };

        var result = FlyerRenderer.RenderFlyer(background, fields, FontsRootPath, logoResolver: _ => logoBytes);

        using var decoded = SKBitmap.Decode(result);
        // The logo was placed at x=0.1*400=40, y=0.1*400=40 - sample a
        // pixel well inside its resized footprint.
        var pixel = decoded!.GetPixel(60, 55);
        Assert.NotEqual(SKColors.Black, pixel);
    }

    [Fact]
    public void RenderFlyer_ImageField_SkipsGracefully_WhenLogoResolverReturnsNull()
    {
        var background = MakeSolidBackground(200, 200, SKColors.Black);
        var fields = new List<FlyerFieldDef>
        {
            new("logo", "Logo", FlyerFieldType.Image, 0.1, 0.1, 0.1, null, null, true, "missing-id")
        };

        // Must not throw even though the "resolved" logo is unavailable -
        // a deleted Catalog item shouldn't break rendering the rest of the flyer.
        var exception = Record.Exception(() =>
            FlyerRenderer.RenderFlyer(background, fields, FontsRootPath, logoResolver: _ => null));

        Assert.Null(exception);
    }

    [Fact]
    public void RenderFlyer_ImageField_SkipsGracefully_WhenNoLogoResolverProvided()
    {
        var background = MakeSolidBackground(200, 200, SKColors.Black);
        var fields = new List<FlyerFieldDef>
        {
            new("logo", "Logo", FlyerFieldType.Image, 0.1, 0.1, 0.1, null, null, true, "some-id")
        };

        var exception = Record.Exception(() =>
            FlyerRenderer.RenderFlyer(background, fields, FontsRootPath, logoResolver: null));

        Assert.Null(exception);
    }

    [Fact]
    public void RenderFlyer_InvalidBackgroundBytes_ThrowsAClearError()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            FlyerRenderer.RenderFlyer(garbage, [], FontsRootPath, logoResolver: null));

        Assert.Contains("decode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("oswald-bold")]
    [InlineData("roboto-regular")]
    [InlineData("playfair-bold")]
    [InlineData("bebas-neue")]
    [InlineData("pacifico")]
    [InlineData("anton")]
    [InlineData("merriweather")]
    public void FlyerFonts_LoadTypeface_LoadsEveryBundledFontWithoutFallingBackToDefault(string key)
    {
        var typeface = FlyerFonts.LoadTypeface(FontsRootPath, key);

        // SKTypeface.Default is what a failed SKTypeface.FromFile falls
        // back to (see FlyerFonts.LoadTypeface) - if this is ever equal to
        // it for a key that's supposed to be a real bundled font, the
        // .ttf file is missing, renamed, or corrupt.
        Assert.NotEqual(SKTypeface.Default.FamilyName, typeface.FamilyName);
    }

    [Fact]
    public void FlyerFonts_LoadTypeface_FallsBackToFirstAvailableFont_ForUnknownKey()
    {
        var typeface = FlyerFonts.LoadTypeface(FontsRootPath, "not-a-real-font-key");
        var expected = FlyerFonts.LoadTypeface(FontsRootPath, FlyerFonts.Available[0].Key);

        Assert.Equal(expected.FamilyName, typeface.FamilyName);
    }

    [Fact]
    public void FlyerFonts_LoadTypeface_FallsBackToFirstAvailableFont_ForNullKey()
    {
        var typeface = FlyerFonts.LoadTypeface(FontsRootPath, null);
        var expected = FlyerFonts.LoadTypeface(FontsRootPath, FlyerFonts.Available[0].Key);

        Assert.Equal(expected.FamilyName, typeface.FamilyName);
    }

    [Fact]
    public void FlyerFonts_Available_HasNoDuplicateKeys()
    {
        var keys = FlyerFonts.Available.Select(f => f.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
