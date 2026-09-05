using System.Collections.Concurrent;
using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>
/// Reads the `gigs` array straight out of the Band's own live site's
/// js/calendar.js (fetched over HTTPS from Band.SiteBaseUrl, read-only) -
/// ported from the old app's gigsSource.js. Per-Band, 60s in-memory cache
/// (static, so it survives across requests/DI scopes the same way the old
/// app's module-level `let cache` did within one Node process).
///
/// Test Mode (reading a local site repo clone instead of the live URL)
/// isn't ported yet - this always hits the live site.
/// </summary>
public class GigsSource(HttpClient http)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<Guid, (DateTime FetchedAt, List<Gig> Gigs)> Cache = new();

    public void InvalidateCache(Guid bandId) => Cache.TryRemove(bandId, out _);

    public async Task<List<Gig>> LoadGigsAsync(Band band)
    {
        if (Cache.TryGetValue(band.Id, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            return cached.Gigs;

        if (string.IsNullOrWhiteSpace(band.SiteBaseUrl)) return cached.Gigs ?? [];

        try
        {
            var url = band.SiteBaseUrl.TrimEnd('/') + "/js/calendar.js";
            var code = await http.GetStringAsync(url);
            var gigs = SiteJsArrayParser.ParseArray<Gig>(code, "gigs");
            Cache[band.Id] = (DateTime.UtcNow, gigs);
            return gigs;
        }
        catch
        {
            // Falls back to the last known-good copy (even if stale)
            // rather than going blank on a transient network blip.
            return cached.Gigs ?? [];
        }
    }

    public async Task<Gig?> FindGigByRefAsync(Band band, string reference)
    {
        var gigs = await LoadGigsAsync(band);
        return gigs.FirstOrDefault(g => SiteContentRef.GigRef(g) == reference);
    }
}
