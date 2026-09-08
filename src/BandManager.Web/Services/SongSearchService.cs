using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Services;

public record SongSearchResult(string Source, string Title, string? Artist, string? Album, string Url, string? Thumbnail);

/// <summary>
/// Live song lookup against YouTube (Data API v3) and Spotify (Web API,
/// Client Credentials flow - no user login needed, just a search).
/// Credentials are SuperAdmin-managed platform-wide settings (see
/// SuperAdminController's song-search-credentials actions) - encrypted at
/// rest in PlatformSettings the same way a Band's own platform
/// credentials are encrypted in Accounts, just global instead of
/// per-Band, matching "every Band's song search shares one YouTube/
/// Spotify connection" the same way the shared Song catalog itself works.
/// With either unset, that half of the search just returns no results
/// rather than failing. Songsterr has no public API at all (not even a
/// free one), so it's never searched here - only ever a manually-pasted
/// link, same as every "no API access" platform elsewhere in this app.
/// </summary>
public class SongSearchService(IHttpClientFactory httpClientFactory, ApplicationDbContext db, ICredentialCipher cipher)
{
    private const string YouTubeKey = "youtube_credentials";
    private const string SpotifyKey = "spotify_credentials";

    private static string? _cachedSpotifyToken;
    private static DateTime _cachedSpotifyTokenExpiresAt;
    private static readonly SemaphoreSlim SpotifyTokenLock = new(1, 1);

    public static async Task<Dictionary<string, string>?> GetCredentialAsync(ApplicationDbContext db, ICredentialCipher cipher, string key)
    {
        var setting = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        if (setting?.Value is null) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(cipher.Decrypt(setting.Value));
        }
        catch
        {
            // A stored credential that no longer decrypts/parses shouldn't
            // crash the caller - same "just returns no results" degrade as
            // every other failure mode in this file.
            return null;
        }
    }

    public static async Task SetCredentialAsync(ApplicationDbContext db, ICredentialCipher cipher, string key, Dictionary<string, string> values)
    {
        var encrypted = cipher.Encrypt(JsonSerializer.Serialize(values));
        var setting = await db.PlatformSettings.FindAsync(key);
        if (setting is null) db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = encrypted });
        else setting.Value = encrypted;
        await db.SaveChangesAsync();
    }

    public static async Task ClearCredentialAsync(ApplicationDbContext db, string key)
    {
        var setting = await db.PlatformSettings.FindAsync(key);
        if (setting is not null)
        {
            db.PlatformSettings.Remove(setting);
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<SongSearchResult>> SearchYouTubeAsync(string query)
    {
        var creds = await GetCredentialAsync(db, cipher, YouTubeKey);
        if (creds is null || !creds.TryGetValue("apiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey)) return [];

        var client = httpClientFactory.CreateClient();
        var url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=6&type=video&q={Uri.EscapeDataString(query)}&key={apiKey}";
        try
        {
            var res = await client.GetAsync(url);
            if (!res.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());

            var results = new List<SongSearchResult>();
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var videoId = item.GetProperty("id").GetProperty("videoId").GetString();
                if (videoId is null) continue;
                var snippet = item.GetProperty("snippet");
                var thumbnail = snippet.TryGetProperty("thumbnails", out var thumbs) && thumbs.TryGetProperty("default", out var def)
                    ? def.GetProperty("url").GetString() : null;
                results.Add(new SongSearchResult(
                    "YouTube",
                    snippet.GetProperty("title").GetString() ?? "",
                    snippet.GetProperty("channelTitle").GetString(),
                    null,
                    $"https://www.youtube.com/watch?v={videoId}",
                    thumbnail));
            }
            return results;
        }
        catch
        {
            // A transient network/quota failure shouldn't break the whole
            // search - the caller still gets Spotify's half, if any.
            return [];
        }
    }

    public async Task<List<SongSearchResult>> SearchSpotifyAsync(string query)
    {
        var token = await GetSpotifyTokenAsync();
        if (token is null) return [];

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var url = $"https://api.spotify.com/v1/search?type=track&limit=6&q={Uri.EscapeDataString(query)}";
        try
        {
            var res = await client.GetAsync(url);
            if (!res.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());

            var results = new List<SongSearchResult>();
            foreach (var track in doc.RootElement.GetProperty("tracks").GetProperty("items").EnumerateArray())
            {
                var artists = string.Join(", ", track.GetProperty("artists").EnumerateArray()
                    .Select(a => a.GetProperty("name").GetString()));
                var album = track.GetProperty("album");
                var images = album.GetProperty("images");
                var thumbnail = images.GetArrayLength() > 0 ? images[images.GetArrayLength() - 1].GetProperty("url").GetString() : null;
                results.Add(new SongSearchResult(
                    "Spotify",
                    track.GetProperty("name").GetString() ?? "",
                    artists,
                    album.GetProperty("name").GetString(),
                    track.GetProperty("external_urls").GetProperty("spotify").GetString() ?? "",
                    thumbnail));
            }
            return results;
        }
        catch
        {
            return [];
        }
    }

    private async Task<string?> GetSpotifyTokenAsync()
    {
        var creds = await GetCredentialAsync(db, cipher, SpotifyKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || !creds.TryGetValue("clientSecret", out var clientSecret)
            || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        if (_cachedSpotifyToken is not null && DateTime.UtcNow < _cachedSpotifyTokenExpiresAt)
            return _cachedSpotifyToken;

        await SpotifyTokenLock.WaitAsync();
        try
        {
            // Re-check after acquiring the lock - another request may have
            // just refreshed it while this one was waiting.
            if (_cachedSpotifyToken is not null && DateTime.UtcNow < _cachedSpotifyTokenExpiresAt)
                return _cachedSpotifyToken;

            var client = httpClientFactory.CreateClient();
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
            {
                Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")])
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            var res = await client.SendAsync(request);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            var token = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
            _cachedSpotifyToken = token;
            _cachedSpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);
            return token;
        }
        catch
        {
            return null;
        }
        finally
        {
            SpotifyTokenLock.Release();
        }
    }
}
