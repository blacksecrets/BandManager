using SkiaSharp;

namespace BandManager.Data.Services;

/// <summary>
/// A small curated, bundled font list for flyer text fields - all
/// SIL Open Font License (free to bundle/redistribute), matching this
/// project's Apache-2.0-only SkiaSharp precedent. Bundled as real .ttf
/// files under wwwroot/fonts/{key}.ttf rather than a Google Fonts CDN
/// link, for two reasons: the server can't reach a CDN at render time
/// (FlyerRenderer needs the actual bytes for SKTypeface), and using the
/// SAME files for the client-side live preview (via flyerFonts.css's
/// @font-face rules pointing at the same path) guarantees the preview
/// matches the final composite exactly - a CDN's own hinting/subsetting
/// could otherwise subtly diverge.
/// </summary>
public static class FlyerFonts
{
    public static readonly (string Key, string Label)[] Available =
    [
        ("oswald-bold", "Oswald Bold"),
        ("roboto-regular", "Roboto"),
        ("playfair-bold", "Playfair Display"),
        ("bebas-neue", "Bebas Neue"),
        ("pacifico", "Pacifico"),
        ("anton", "Anton"),
        ("merriweather", "Merriweather"),
    ];

    private static readonly Dictionary<string, SKTypeface> Cache = new();
    private static readonly Lock CacheLock = new();

    public static SKTypeface LoadTypeface(string fontsRootPath, string? key)
    {
        var resolvedKey = Available.Any(f => f.Key == key) ? key! : Available[0].Key;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(resolvedKey, out var cached)) return cached;
            var typeface = SKTypeface.FromFile(Path.Combine(fontsRootPath, $"{resolvedKey}.ttf")) ?? SKTypeface.Default;
            Cache[resolvedKey] = typeface;
            return typeface;
        }
    }
}
