using System.Collections.Concurrent;
using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>
/// Reads the `gigs` array straight out of the Band's own live site's
/// js/calendar.js (fetched over HTTPS from Band.SiteBaseUrl, read-only).
/// No longer the app's own read path for gigs (the database is now the
/// source of truth - see Entities.Gig) - kept only for the one-time DB
/// backfill (importing a band's existing site gigs) and any future
/// "resync from what's actually live on the site" admin tool. Per-Band,
/// 60s in-memory cache (static, so it survives across requests/DI scopes).
///
/// Test Mode (reading a local site repo clone instead of the live URL)
/// isn't ported yet - this always hits the live site.
/// </summary>
public class GigsSource(HttpClient http)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<Guid, (DateTime FetchedAt, List<SiteGig> Gigs)> Cache = new();

    public void InvalidateCache(Guid bandId) => Cache.TryRemove(bandId, out _);

    public async Task<List<SiteGig>> LoadGigsAsync(Band band)
    {
        if (Cache.TryGetValue(band.Id, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            return cached.Gigs;

        if (string.IsNullOrWhiteSpace(band.SiteBaseUrl)) return cached.Gigs ?? [];

        try
        {
            var url = band.SiteBaseUrl.TrimEnd('/') + "/js/calendar.js";
            var code = await http.GetStringAsync(url);
            var gigs = SiteJsArrayParser.ParseArray<SiteGig>(code, "gigs");
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

    public async Task<SiteGig?> FindGigByRefAsync(Band band, string reference)
    {
        var gigs = await LoadGigsAsync(band);
        return gigs.FirstOrDefault(g => SiteContentRef.GigRef(g) == reference);
    }
}
