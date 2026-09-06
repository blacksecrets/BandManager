using System.Text.Json;
using System.Text.RegularExpressions;
using BandManager.Data.Entities;
using static BandManager.Data.Services.SiteTextEditing;

namespace BandManager.Data.Services;

/// <summary>
/// Edits a Band's own public site's js/media.js - ported from the old
/// app's mediaEditor.js. Unlike calendar.js (where gigs are added by
/// hand), media items only ever come into existence through this
/// dashboard, so this does genuine add/delete of whole entries.
/// </summary>
public class MediaSiteEditor(GitHubSiteClient gitHub, MediaSource mediaSource, HttpClient http)
{
    private const string FilePath = "js/media.js";

    private static TextBlock? FindMediaBlock(string code, MediaItem item) =>
        !string.IsNullOrEmpty(item.Id) ? FindBlock(code, "id", item.Id) : FindBlock(code, "title", item.Title);

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
    private async Task<string?> DeriveAutoThumbnailAsync(string rawUrl)
    {
        var ytId = ExtractYouTubeId(rawUrl);
        if (ytId is not null) return $"https://img.youtube.com/vi/{ytId}/hqdefault.jpg";
        if (Regex.IsMatch(rawUrl, @"soundcloud\.com/")) return await FetchSoundCloudThumbnailAsync(rawUrl);
        return null;
    }

    /// <summary>fields: {title, url (raw link - converted to embed form
    /// here), thumbnail}. Only keys present are changed.</summary>
    public async Task UpdateMediaItemAsync(Band band, MediaItem item, Dictionary<string, string> fields)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read media.js from the site repo.");

        var block = FindMediaBlock(file.Content, item)
            ?? throw new InvalidOperationException($"Couldn't find \"{item.Id ?? item.Title}\" in media.js - it may have already changed on the site.");

        var newBlock = block.Text;
        foreach (var (field, rawValue) in fields)
        {
            if (field == "url")
            {
                var embed = ToEmbedUrl(rawValue) ?? throw new InvalidOperationException("That link doesn't look like a YouTube or SoundCloud URL.");
                newBlock = SetField(newBlock, "url", embed);
            }
            else
            {
                newBlock = SetField(newBlock, field, rawValue);
            }
        }

        var newContent = file.Content[..block.Start] + newBlock + file.Content[block.End..];
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Update \"{fields.GetValueOrDefault("title", item.Title)}\" media item", file.Sha);
        mediaSource.InvalidateCache(band.Id);
    }

    /// <summary>fields: {title, url} - both required. artBytes/artExt:
    /// the tile art image, optional - if omitted, the thumbnail is auto-
    /// derived from the link instead; a provided file always overrides
    /// that. Art is uploaded before the text edit, so a failure partway
    /// through never leaves an entry pointing at a nonexistent
    /// image.</summary>
    public async Task<(string Id, string Thumbnail)> AddMediaItemAsync(Band band, string title, string url, byte[]? artBytes, string? artExt)
    {
        var embed = ToEmbedUrl(url) ?? throw new InvalidOperationException("That link doesn't look like a YouTube or SoundCloud URL.");

        string? thumbnail = null;
        if (artBytes is null)
        {
            thumbnail = await DeriveAutoThumbnailAsync(url)
                ?? throw new InvalidOperationException("Couldn't automatically find a thumbnail for that link - please upload tile art.");
        }

        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read media.js from the site repo.");

        var bounds = FindArrayBounds(file.Content, "mediaItems")
            ?? throw new InvalidOperationException("Could not find the mediaItems array in media.js.");
        var arrayText = file.Content[bounds.Start..bounds.End];

        var id = UniqueId(file.Content, Slugify(title, "media-item"));
        if (artBytes is not null)
        {
            thumbnail = $"media/{id}.{artExt}";
            await gitHub.PutBinaryFileAsync(band, thumbnail, artBytes, $"Add tile art for \"{title}\"");
        }

        var blockText = string.Join('\n',
            "    {",
            $"        id: \"{id}\",",
            $"        title: \"{EscapeForQuotes(title)}\",",
            $"        url: \"{embed}\",",
            $"        thumbnail: \"{thumbnail}\"",
            "    }");

        var newArrayText = InsertBeforeClose(arrayText, ']', blockText)
            ?? throw new InvalidOperationException("Could not figure out where to insert the new media item.");

        var newContent = file.Content[..bounds.Start] + newArrayText + file.Content[bounds.End..];
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Add \"{title}\" media item", file.Sha);
        mediaSource.InvalidateCache(band.Id);
        return (id, thumbnail!);
    }

    /// <summary>Removes the whole entry, including whichever neighboring
    /// comma keeps the array valid.</summary>
    public async Task DeleteMediaItemAsync(Band band, MediaItem item)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read media.js from the site repo.");

        var block = FindMediaBlock(file.Content, item)
            ?? throw new InvalidOperationException($"Couldn't find \"{item.Id ?? item.Title}\" in media.js - it may have already changed on the site.");

        var newContent = RemoveBlock(file.Content, block);
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Remove \"{item.Title}\" media item", file.Sha);
        mediaSource.InvalidateCache(band.Id);
    }
}
