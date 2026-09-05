using System.Collections.Concurrent;
using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>Reads `mediaItems` from the Band's own live js/media.js -
/// same approach and caveats as GigsSource, ported from mediaSource.js.</summary>
public class MediaSource(HttpClient http)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<Guid, (DateTime FetchedAt, List<MediaItem> Items)> Cache = new();

    public void InvalidateCache(Guid bandId) => Cache.TryRemove(bandId, out _);

    public async Task<List<MediaItem>> LoadMediaItemsAsync(Band band)
    {
        if (Cache.TryGetValue(band.Id, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            return cached.Items;

        if (string.IsNullOrWhiteSpace(band.SiteBaseUrl)) return cached.Items ?? [];

        try
        {
            var url = band.SiteBaseUrl.TrimEnd('/') + "/js/media.js";
            var code = await http.GetStringAsync(url);
            var items = SiteJsArrayParser.ParseArray<MediaItem>(code, "mediaItems");
            Cache[band.Id] = (DateTime.UtcNow, items);
            return items;
        }
        catch
        {
            return cached.Items ?? [];
        }
    }

    public async Task<MediaItem?> FindMediaByRefAsync(Band band, string reference)
    {
        var items = await LoadMediaItemsAsync(band);
        return items.FirstOrDefault(m => SiteContentRef.MediaRef(m) == reference);
    }
}
