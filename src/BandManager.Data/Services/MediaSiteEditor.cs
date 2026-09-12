using System.Text.Json;
using System.Text.RegularExpressions;
using BandManager.Data.Entities;
using static BandManager.Data.Services.SiteTextEditing;

namespace BandManager.Data.Services;

/// <summary>
/// Publishes a DB MediaItem row (the source of truth - see MediaItem.cs)
/// to a Band's public site's js/media.js, best-effort and one-way -
/// mirrors GigsSiteEditor's PublishGigAsync exactly (always rewrites the
/// whole block from the DB row's current state; a band with no site
/// simply never calls this). ToEmbedUrl/thumbnail-derivation stay here as
/// media-specific business logic, independent of whether a site push
/// happens.
/// </summary>
public class MediaSiteEditor(GitHubSiteClient gitHub, HttpClient http) : IMediaSitePublisher
{
    private const string FilePath = "js/media.js";

    /// <summary>A YouTube watch/share link or a SoundCloud track link,
    /// turned into the embeddable URL media.js's modal iframe needs.
    /// Returns null if the URL doesn't look like either.</summary>
    public static string? ToEmbedUrl(string? rawUrl)
    {
        var url = (rawUrl ?? "").Trim();
        var yt = Regex.Match(url, @"(?:youtube\.com/(?:watch\?v=|embed/|shorts/)|youtu\.be/)([\w-]{11})");
        if (yt.Success) return $"https://www.youtube.com/embed/{yt.Groups[1].Value}";
        if (Regex.IsMatch(url, @"soundcloud\.com/"))
        {
            var encoded = Uri.EscapeDataString(url);
            return $"https://w.soundcloud.com/player/?url={encoded}&color=%23ff5500&auto_play=false&hide_related=false&show_comments=true&show_user=true&show_reposts=false&show_teaser=true&visual=true";
        }
        return null;
    }

    private static string? ExtractYouTubeId(string? rawUrl)
    {
        var m = Regex.Match(rawUrl ?? "", @"(?:youtube\.com/(?:watch\?v=|embed/|shorts/)|youtu\.be/)([\w-]{11})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>SoundCloud artwork isn't derivable from the track URL the
    /// way YouTube's is - oEmbed (public, no auth) is the standard way to
    /// resolve it. Best effort: any failure just means no auto
    /// thumbnail, not a hard error.</summary>
    private async Task<string?> FetchSoundCloudThumbnailAsync(string rawUrl)
    {
        try
        {
            var res = await http.GetAsync($"https://soundcloud.com/oembed?format=json&url={Uri.EscapeDataString(rawUrl)}");
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            return doc.RootElement.TryGetProperty("thumbnail_url", out var t) ? t.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>No upload needed for YouTube - it auto-generates a
    /// thumbnail at a predictable URL. SoundCloud needs the actual oEmbed
    /// lookup. Either way this is just an external URL, nothing to
    /// mirror into the site repo.</summary>
    public async Task<string?> DeriveAutoThumbnailAsync(string rawUrl)
    {
        var ytId = ExtractYouTubeId(rawUrl);
        if (ytId is not null) return $"https://img.youtube.com/vi/{ytId}/hqdefault.jpg";
        if (Regex.IsMatch(rawUrl, @"soundcloud\.com/")) return await FetchSoundCloudThumbnailAsync(rawUrl);
        return null;
    }

    /// <summary>Writes item's current full state to media.js - replacing
    /// its existing block (matched by id == item.Ref) if present, else
    /// appending a new one.</summary>
    public async Task PublishMediaItemAsync(Band band, MediaItem item)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read media.js from the site repo.");

        var blockText = string.Join('\n',
            "    {",
            $"        id: \"{EscapeForQuotes(item.Ref)}\",",
            $"        title: \"{EscapeForQuotes(item.Title)}\",",
            $"        url: \"{EscapeForQuotes(item.Url ?? "")}\",",
            $"        thumbnail: \"{EscapeForQuotes(item.Thumbnail ?? "")}\"",
            "    }");

        var existingBlock = FindBlock(file.Content, "id", item.Ref);
        string newContent;
        if (existingBlock is not null)
        {
            newContent = file.Content[..existingBlock.Start] + blockText + file.Content[existingBlock.End..];
        }
        else
        {
            var bounds = FindArrayBounds(file.Content, "mediaItems")
                ?? throw new InvalidOperationException("Could not find the mediaItems array in media.js.");
            var arrayText = file.Content[bounds.Start..bounds.End];
            var newArrayText = InsertBeforeClose(arrayText, ']', blockText)
                ?? throw new InvalidOperationException("Could not figure out where to insert the media item.");
            newContent = file.Content[..bounds.Start] + newArrayText + file.Content[bounds.End..];
        }

        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Update \"{item.Title}\" media item", file.Sha);
    }

    /// <summary>Removes item's block from media.js, if present - a no-op
    /// if the site never had it (e.g. created before this band had a
    /// site).</summary>
    public async Task UnpublishMediaItemAsync(Band band, string mediaRef, string title)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read media.js from the site repo.");

        var block = FindBlock(file.Content, "id", mediaRef);
        if (block is null) return;

        var newContent = RemoveBlock(file.Content, block);
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Remove \"{title}\" media item", file.Sha);
    }
}
