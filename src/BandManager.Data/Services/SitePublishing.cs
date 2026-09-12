using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>
/// Whether a Band has a live, connected website - the one check every
/// site-publishing call site needs before attempting any of the
/// interfaces below (a Band with no site simply never calls them).
/// Centralizes what was five identical copies of the same three-field
/// check, one private method per controller that publishes something.
/// </summary>
public interface IBandSiteConnection
{
    Task<bool> HasSiteConfiguredAsync(Band band);
}

public class BandSiteConnection(CredentialStore credentialStore) : IBandSiteConnection
{
    public async Task<bool> HasSiteConfiguredAsync(Band band) =>
        !string.IsNullOrWhiteSpace(band.SiteBaseUrl) && !string.IsNullOrWhiteSpace(band.GitHubOwner) && !string.IsNullOrWhiteSpace(band.GitHubRepo)
        && await credentialStore.GetCredentialAsync(band.Id, "website") is not null;
}

/// <summary>
/// Everything GigsController/FlyersController need to publish a Gig (and
/// its flyer) to a Band's connected site. The only implementation today,
/// GigsSiteEditor, does it by rewriting a GitHub-hosted calendar.js -
/// this interface exists so a future site format (a BandManager-hosted
/// site, most likely) can be swapped in behind it without either
/// controller changing. Static, pure helpers with no site I/O of their
/// own (none on this particular editor) stay off the interface; only
/// the methods that actually talk to a site belong here.
/// </summary>
public interface IGigSitePublisher
{
    Task PushFlyerImageAsync(Band band, Gig gig, byte[] rendered, string commitMessage);
    Task PublishGigAsync(Band band, Gig gig, List<WithAct> withActs);
    Task UnpublishGigAsync(Band band, string gigRef, string gigTitle);
}

/// <summary>Same reasoning as IGigSitePublisher, for GalleryController's
/// js/gallery.js. GallerySiteEditor.GenerateThumbnail stays a plain
/// static helper - it never touches the site, so it isn't part of this
/// interface and callers keep using it by concrete type name.</summary>
public interface IGallerySitePublisher
{
    Task PublishGalleryImageAsync(Band band, GalleryImage item);
    Task UnpublishGalleryImageAsync(Band band, string galleryRef, string? alt);
}

/// <summary>Same reasoning as IGigSitePublisher, for MediaController's
/// js/media.js. DeriveAutoThumbnailAsync is included (unlike the pure
/// static helpers on the other editors) because, for SoundCloud, it
/// makes its own external call and MediaController genuinely depends on
/// it as part of "the media publishing service" - MediaSiteEditor.
/// ToEmbedUrl stays a plain static helper since it's pure string
/// transformation with no I/O at all.</summary>
public interface IMediaSitePublisher
{
    Task<string?> DeriveAutoThumbnailAsync(string rawUrl);
    Task PublishMediaItemAsync(Band band, MediaItem item);
    Task UnpublishMediaItemAsync(Band band, string mediaRef, string title);
}

/// <summary>Same reasoning as IGigSitePublisher, for TechRiderController's
/// PDF + epk.html Downloads section. TechRiderSiteEditor.PdfPath stays a
/// plain static helper - it's pure path derivation, no site I/O.</summary>
public interface ITechRiderSitePublisher
{
    Task PublishAsync(Band band, Act act, byte[] pdfBytes);
}
