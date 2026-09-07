using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BandManager.Data.Services;

/// <summary>One supporting act: a repeatable replacement for the old
/// single WithArtists/WithArtistsUrl scalar pair - see Gig.With and
/// EffectiveWith below.</summary>
public record WithAct(string? Name, string? Url);

public record Gig(
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

public static class GigExtensions
{
    /// <summary>The normalized view every consumer (captions, ScheduleItem
    /// serialization, the Flyer Editor) should read from instead of
    /// checking With vs. the legacy scalar pair separately: the new list
    /// if it has anything, else a single-item list built from the legacy
    /// pair if either half is set, else empty - never both at once.</summary>
    public static List<WithAct> EffectiveWith(this Gig gig) =>
        gig.With is { Count: > 0 } ? gig.With
        : !string.IsNullOrEmpty(gig.WithArtists) || !string.IsNullOrEmpty(gig.WithArtistsUrl)
            ? [new WithAct(gig.WithArtists, gig.WithArtistsUrl)]
            : [];
}

public record MediaItem(string? Id, string Title, string? Url, string? Thumbnail);

public record GalleryImage(string? Id, string? Alt, string? Thumb, string? Full);

/// <summary>Stable per-record refs, ported byte-for-byte from the old
/// app's gigsSource.js/mediaSource.js/gallerySource.js hashing (SHA1,
/// first 12 hex chars) - matters for the eventual data migration, where
/// existing schedule_items rows referencing a hash-based ref (for gigs
/// that predate having a real `id`) need to resolve to the same gig here.</summary>
public static class SiteContentRef
{
    private static string Sha1First12(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..12];
    }

    public static string GigRef(Gig gig) => !string.IsNullOrEmpty(gig.Id) ? gig.Id : Sha1First12($"{gig.Date}|{gig.Title}");
    public static string MediaRef(MediaItem item) => !string.IsNullOrEmpty(item.Id) ? item.Id : Sha1First12($"{item.Title}|{item.Url}");
    public static string GalleryRef(GalleryImage image) => !string.IsNullOrEmpty(image.Id) ? image.Id : Sha1First12($"{image.Full}|{image.Alt}");
}
