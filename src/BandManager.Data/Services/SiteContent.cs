using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BandManager.Data.Services;

/// <summary>One supporting act, as it appears in the site's own calendar.js
/// JSON shape - see SiteGig.With and EffectiveWith below. The DB-side
/// equivalent, GigWithBand, references a real Band row instead of a free
/// name/url pair; this stays what GigsSiteEditor publishes to the site.</summary>
public record WithAct(string? Name, string? Url);

/// <summary>The site's own calendar.js JSON shape for one gig - used only
/// for reading an existing site during the one-time DB backfill, and for
/// publishing (GigsSiteEditor) a DB-side Entities.Gig back out to a
/// connected site. The database (Entities.Gig) is the source of truth;
/// this is purely the site-facing wire shape, renamed from the pre-DB-
/// migration "Gig" to avoid colliding with Entities.Gig.</summary>
public record SiteGig(
    string? Id, string Title, string? Venue, string? VenueUrl, string Date, string? Time,
    string? Address,
    // WithArtists/WithArtistsUrl are kept, never removed - real bands'
    // already-live calendar.js files still have only these on older gigs,
    // and SiteJsArrayParser's case-insensitive JSON deserialize means an
    // old gig block with no "with" key just leaves With null here, so
    // nothing breaks on read. Use EffectiveWith() below rather than
    // checking these two fields directly.
    string? WithArtists, string? WithArtistsUrl,
    List<WithAct>? With,
    // Doors/Opener/Headliner are additive - purely for flyer field
    // precision, not wired into the site's own calendar display, which
    // keeps using the free-text Time field exactly as before.
    string? DoorsTime, string? OpenerTime, string? HeadlinerTime,
    string? TicketsUrl,
    string? FlyerMain, bool FreeAdmission, string? CustomTicketsText, string? TicketMode);

public static class SiteGigExtensions
{
    /// <summary>The normalized view every consumer should read from
    /// instead of checking With vs. the legacy scalar pair separately: the
    /// new list if it has anything, else a single-item list built from the
    /// legacy pair if either half is set, else empty - never both at
    /// once.</summary>
    public static List<WithAct> EffectiveWith(this SiteGig gig) =>
        gig.With is { Count: > 0 } ? gig.With
        : !string.IsNullOrEmpty(gig.WithArtists) || !string.IsNullOrEmpty(gig.WithArtistsUrl)
            ? [new WithAct(gig.WithArtists, gig.WithArtistsUrl)]
            : [];
}

/// <summary>The site's own media.js JSON shape - see SiteGig's doc comment
/// for why this is named Site-prefixed (distinct from the DB-side
/// Entities.MediaItem, which is now the source of truth).</summary>
public record SiteMediaItem(string? Id, string Title, string? Url, string? Thumbnail);

/// <summary>The site's own gallery.js JSON shape - see SiteGig's doc
/// comment (distinct from the DB-side Entities.GalleryImage).</summary>
public record SiteGalleryImage(string? Id, string? Alt, string? Thumb, string? Full);

/// <summary>Stable per-record refs, ported byte-for-byte from the old
/// app's gigsSource.js/mediaSource.js/gallerySource.js hashing (SHA1,
/// first 12 hex chars). Used today only for the one-time DB backfill (to
/// preserve a gig/media/gallery item's existing ref so already-existing
/// Flyer/GigSet/ScheduleItem rows keep resolving) and by the site
/// publishers when writing a brand-new site-authored id is not the
/// concern (the DB row's own Ref is what's authoritative going forward).</summary>
public static class SiteContentRef
{
    private static string Sha1First12(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..12];
    }

    public static string GigRef(SiteGig gig) => !string.IsNullOrEmpty(gig.Id) ? gig.Id : Sha1First12($"{gig.Date}|{gig.Title}");
    public static string MediaRef(SiteMediaItem item) => !string.IsNullOrEmpty(item.Id) ? item.Id : Sha1First12($"{item.Title}|{item.Url}");
    public static string GalleryRef(SiteGalleryImage image) => !string.IsNullOrEmpty(image.Id) ? image.Id : Sha1First12($"{image.Full}|{image.Alt}");
}
