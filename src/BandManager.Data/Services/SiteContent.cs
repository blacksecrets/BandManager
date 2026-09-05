using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BandManager.Data.Services;

public record Gig(
    string? Id, string Title, string? Venue, string? VenueUrl, string Date, string? Time,
    string? Address, string? WithArtists, string? WithArtistsUrl, string? TicketsUrl,
    string? FlyerMain, bool FreeAdmission, string? CustomTicketsText, string? TicketMode);

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
