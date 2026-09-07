using System.Collections.Concurrent;
using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>Reads `galleryImages` from the Band's own live js/gallery.js -
/// same approach and caveats as GigsSource, ported from gallerySource.js.</summary>
public class GallerySource(HttpClient http)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<Guid, (DateTime FetchedAt, List<SiteGalleryImage> Images)> Cache = new();

    public void InvalidateCache(Guid bandId) => Cache.TryRemove(bandId, out _);

    public async Task<List<SiteGalleryImage>> LoadSiteGalleryImagesAsync(Band band)
    {
        if (Cache.TryGetValue(band.Id, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            return cached.Images;

        if (string.IsNullOrWhiteSpace(band.SiteBaseUrl)) return cached.Images ?? [];

        try
        {
            var url = band.SiteBaseUrl.TrimEnd('/') + "/js/gallery.js";
            var code = await http.GetStringAsync(url);
            var images = SiteJsArrayParser.ParseArray<SiteGalleryImage>(code, "galleryImages");
            Cache[band.Id] = (DateTime.UtcNow, images);
            return images;
        }
        catch
        {
            return cached.Images ?? [];
        }
    }

    public async Task<SiteGalleryImage?> FindGalleryByRefAsync(Band band, string reference)
    {
        var images = await LoadSiteGalleryImagesAsync(band);
        return images.FirstOrDefault(g => SiteContentRef.GalleryRef(g) == reference);
    }
}
