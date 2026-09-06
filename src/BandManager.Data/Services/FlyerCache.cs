using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>
/// Mirrors flyer/tile-art/gallery images from the Band's own live site
/// URLs into this app's own local cache, so the app's files are fully
/// self-contained - ported from the old app's flyerCache.js. Flyers are
/// treated as immutable once published - once cached, a file is trusted
/// as-is rather than re-checked against the live copy every run.
/// cacheRootPath is a plain constructor parameter (same pattern as
/// CredentialCipher/CatalogStore), resolved from ContentRootPath by the
/// Web project's DI registration. Per-Band subfolder (cacheRootPath/
/// &lt;bandId&gt;/...), since two Bands' sites could coincidentally use the
/// same relative image path.
///
/// Test Mode (reading a local site repo clone instead of fetching) isn't
/// ported yet - this always hits the live site.
/// </summary>
public class FlyerCache(HttpClient http, string cacheRootPath)
{
    /// <summary>Fetches one image into the local cache if it isn't
    /// already there. Returns the cached file's absolute path, or null if
    /// it couldn't be fetched (e.g. removed from the site, a network
    /// error, or the path was already a full external URL with nothing to
    /// mirror - some older media items point straight at e.g. YouTube's
    /// own thumbnail CDN).</summary>
    /// <summary>Writes bytes already in hand straight into the cache,
    /// rather than deleting the stale entry and waiting for the next
    /// cache-priming cycle to re-fetch it from the live site - which can
    /// lag behind a GitHub push. Used right after a GitHub push of the
    /// same bytes, so the dashboard's own tile shows the new image
    /// immediately instead of a stale one.</summary>
    public async Task WriteDirectlyAsync(Band band, string relativePath, byte[] bytes)
    {
        var bandCacheDir = Path.Combine(cacheRootPath, band.Id.ToString());
        var normalizedRelative = relativePath.Replace('\\', '/').TrimStart('/');
        var destPath = Path.GetFullPath(Path.Combine(bandCacheDir, normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
        if (!destPath.StartsWith(Path.GetFullPath(bandCacheDir), StringComparison.Ordinal)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await File.WriteAllBytesAsync(destPath, bytes);
    }

    /// <summary>Drops a cached file if present (best-effort, never
    /// throws) - used when a fresher copy isn't available in hand to
    /// write directly (e.g. a regenerated thumbnail whose bytes weren't
    /// kept around), so the next cache-priming read just re-fetches it
    /// from the live site instead of serving a stale one.</summary>
    public void DeleteCached(Band band, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        try
        {
            var bandCacheDir = Path.Combine(cacheRootPath, band.Id.ToString());
            var normalizedRelative = relativePath.Replace('\\', '/').TrimStart('/');
            var destPath = Path.GetFullPath(Path.Combine(bandCacheDir, normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destPath.StartsWith(Path.GetFullPath(bandCacheDir), StringComparison.Ordinal)) return;
            File.Delete(destPath);
        }
        catch
        {
            // best-effort
        }
    }

    public async Task<string?> EnsureCachedAsync(Band band, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (relativePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bandCacheDir = Path.Combine(cacheRootPath, band.Id.ToString());
        var normalizedRelative = relativePath.Replace('\\', '/').TrimStart('/');
        var destPath = Path.GetFullPath(Path.Combine(bandCacheDir, normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));

        // Guards against a path escaping the cache dir (e.g. "../../..").
        if (!destPath.StartsWith(Path.GetFullPath(bandCacheDir), StringComparison.Ordinal)) return null;

        if (File.Exists(destPath)) return destPath;
        if (string.IsNullOrWhiteSpace(band.SiteBaseUrl)) return null;

        var url = band.SiteBaseUrl.TrimEnd('/') + "/" + normalizedRelative;
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await File.WriteAllBytesAsync(destPath, bytes);
            return destPath;
        }
        catch
        {
            return null;
        }
    }
}
