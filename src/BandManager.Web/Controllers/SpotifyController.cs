using System.Security.Cryptography;
using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record CreateSpotifyPlaylistRequest(string Name, string? Description, bool Public = true);
public record SpotifyTrackRequest(string TrackUri);

/// <summary>
/// Real Spotify automation: band members manage playlists of the band's
/// own songs (from the Repertoire) directly on Spotify - not text/photo
/// promotional posts, which Spotify has no API for at all (see
/// PlatformSeedData's NoApiNote history on this platform before this
/// controller existed). OAuth connect/disconnect is BandAdmin-only (it's
/// the band's own Spotify login); the playlist actions themselves are
/// BandMember-scoped, same as the Setlist builder, since any member
/// might reasonably curate a rehearsal or setlist-companion playlist.
///
/// Access tokens expire in ~1 hour - GetAccessTokenAsync transparently
/// refreshes via the stored refresh token before every call, unlike
/// Google Business Profile's known "paste a fresh token by hand" gap
/// (see GoogleBusinessPublisher's doc comment) - the whole point of
/// building a real OAuth flow here instead of a manual-paste field.
/// </summary>
[ApiController]
[Route("/api/spotify")]
[Authorize]
public class SpotifyController(
    ApplicationDbContext db,
    ICredentialCipher cipher,
    CredentialStore credentialStore,
    IActiveBandAccessor activeBand,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private const string PlatformId = "spotify";
    private const string OAuthAppKey = "spotify_oauth_credentials";
    private const string StateCookieName = "spotify-oauth-state";

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private string RedirectUri => $"{Request.Scheme}://{Request.Host}/api/spotify/callback";

    [HttpGet("configured")]
    public async Task<IActionResult> Configured()
    {
        var creds = await SongSearchService.GetCredentialAsync(db, cipher, OAuthAppKey);
        return Ok(new { configured = creds is not null });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var connected = await credentialStore.HasCredentialAsync(bandId, PlatformId);
        return Ok(new { connected });
    }

    [Authorize(Policy = "BandAdmin")]
    [HttpGet("connect")]
    public async Task<IActionResult> Connect()
    {
        if (RequireActiveBand(out _) is { } err) return err;

        var creds = await SongSearchService.GetCredentialAsync(db, cipher, OAuthAppKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || string.IsNullOrEmpty(clientId))
            return BadRequest(new { error = "Spotify posting isn't set up yet - ask a SuperAdmin to configure it first." });

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var scope = Uri.EscapeDataString("playlist-modify-public playlist-modify-private");
        var redirectUri = Uri.EscapeDataString(RedirectUri);
        var url = $"https://accounts.spotify.com/authorize?client_id={clientId}&response_type=code" +
                  $"&redirect_uri={redirectUri}&scope={scope}&state={state}";
        return Redirect(url);
    }

    [Authorize(Policy = "BandAdmin")]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var expectedState = Request.Cookies[StateCookieName];
        Response.Cookies.Delete(StateCookieName);

        if (!string.IsNullOrEmpty(error)) return BadRequest(new { error = $"Spotify declined the connection: {error}" });
        if (string.IsNullOrEmpty(code)) return BadRequest(new { error = "Missing authorization code." });
        if (string.IsNullOrEmpty(expectedState) || expectedState != state) return BadRequest(new { error = "Invalid or expired connection attempt - please try again." });

        var creds = await SongSearchService.GetCredentialAsync(db, cipher, OAuthAppKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || !creds.TryGetValue("clientSecret", out var clientSecret))
            return BadRequest(new { error = "Spotify posting isn't set up yet - ask a SuperAdmin to configure it first." });

        var http = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        using var tokenRes = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();
        if (!tokenRes.IsSuccessStatusCode)
            return BadRequest(new { error = $"Spotify token exchange failed: {tokenBody}" });

        using var tokenJson = JsonDocument.Parse(tokenBody);
        var root = tokenJson.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        if (refreshToken is null)
            return BadRequest(new { error = "Spotify didn't return a refresh token - try disconnecting and reconnecting." });

        await credentialStore.SetCredentialAsync(bandId, PlatformId, new()
        {
            ["accessToken"] = accessToken,
            ["refreshToken"] = refreshToken,
            ["expiresAt"] = DateTime.UtcNow.AddSeconds(expiresIn).ToString("O")
        });
        await credentialStore.SetOnboardedAsync(bandId, PlatformId, true);

        return Redirect("/repertoire.html?spotifyConnected=1");
    }

    [Authorize(Policy = "BandAdmin")]
    [HttpDelete("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        await credentialStore.ClearCredentialAsync(bandId, PlatformId);
        return Ok(new { ok = true });
    }

    // --- Access token refresh ---

    private async Task<string?> GetAccessTokenAsync(Guid bandId)
    {
        var creds = await credentialStore.GetCredentialAsync(bandId, PlatformId);
        if (creds is null || !creds.TryGetValue("refreshToken", out var refreshToken)) return null;

        if (creds.TryGetValue("accessToken", out var accessToken)
            && creds.TryGetValue("expiresAt", out var expiresAtRaw)
            && DateTime.TryParse(expiresAtRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt)
            && DateTime.UtcNow < expiresAt.AddSeconds(-60))
        {
            return accessToken;
        }

        var oauthCreds = await SongSearchService.GetCredentialAsync(db, cipher, OAuthAppKey);
        if (oauthCreds is null || !oauthCreds.TryGetValue("clientId", out var clientId) || !oauthCreds.TryGetValue("clientSecret", out var clientSecret))
            return null;

        var http = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        using var res = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var newAccessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var newExpiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        // Spotify's refresh response usually omits refresh_token (the old
        // one stays valid) - keep the existing one unless a new one is issued.
        var newRefreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refreshToken;

        await credentialStore.SetCredentialAsync(bandId, PlatformId, new()
        {
            ["accessToken"] = newAccessToken,
            ["refreshToken"] = newRefreshToken!,
            ["expiresAt"] = DateTime.UtcNow.AddSeconds(newExpiresIn).ToString("O")
        });
        return newAccessToken;
    }

    private static object? ParseSpotifyError(string body)
    {
        try
        {
            var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var err))
                return err.TryGetProperty("message", out var msg) ? msg.GetString() : err.GetString();
        }
        catch { /* not JSON */ }
        return null;
    }

    // --- Playlist CRUD (any Band member) ---

    [HttpGet("playlists")]
    public async Task<IActionResult> ListPlaylists()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "Spotify isn't connected for this band yet." });

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var res = await http.GetAsync("https://api.spotify.com/v1/me/playlists?limit=50");
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return BadRequest(new { error = ParseSpotifyError(body) ?? "Could not load playlists." });

        using var doc = JsonDocument.Parse(body);
        var playlists = doc.RootElement.GetProperty("items").EnumerateArray().Select(p => new
        {
            id = p.GetProperty("id").GetString(),
            name = p.GetProperty("name").GetString(),
            trackCount = p.GetProperty("tracks").GetProperty("total").GetInt32(),
            url = p.GetProperty("external_urls").GetProperty("spotify").GetString()
        });
        return Ok(playlists);
    }

    [HttpPost("playlists")]
    public async Task<IActionResult> CreatePlaylist([FromBody] CreateSpotifyPlaylistRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Playlist name is required." });

        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "Spotify isn't connected for this band yet." });

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var payload = JsonSerializer.Serialize(new { name, description = request.Description, @public = request.Public });
        using var res = await http.PostAsync("https://api.spotify.com/v1/me/playlists",
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return BadRequest(new { error = ParseSpotifyError(body) ?? "Could not create the playlist." });

        using var doc = JsonDocument.Parse(body);
        return Ok(new
        {
            id = doc.RootElement.GetProperty("id").GetString(),
            name = doc.RootElement.GetProperty("name").GetString(),
            url = doc.RootElement.GetProperty("external_urls").GetProperty("spotify").GetString()
        });
    }

    /// <summary>Creates a playlist named for one Gig and adds every song
    /// in that Gig's setlist that has a resolvable Spotify link - real
    /// Repertoire songs via Song.SpotifyUrl, manual/ad-hoc entries via
    /// GigSetSong.ManualSpotifyUrl (same "either source, same output
    /// field" shape GigSetsController.Serialize already uses). Songs with
    /// no Spotify link at all are silently skipped, not an error - a
    /// setlist commonly has at least one song nobody's bothered to link
    /// yet, and that shouldn't block everything else from going up.</summary>
    [HttpPost("playlists/from-gig/{gigRef}")]
    public async Task<IActionResult> CreatePlaylistFromGig(string gigRef)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var gig = await db.Gigs.AsNoTracking().Include(g => g.Band).FirstOrDefaultAsync(g => g.BandId == bandId && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var set = await db.GigSets.AsNoTracking()
            .Include(s => s.Songs.OrderBy(gs => gs.SortOrder)).ThenInclude(gs => gs.Song)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        if (set is null || set.Songs.Count == 0) return BadRequest(new { error = "This gig has no setlist to upload yet." });

        var trackUris = set.Songs.OrderBy(gs => gs.SortOrder)
            .Select(gs => SpotifyTrackUriFromUrl(gs.Song?.SpotifyUrl ?? gs.ManualSpotifyUrl))
            .Where(uri => uri is not null)
            .ToList();
        if (trackUris.Count == 0) return BadRequest(new { error = "None of the songs in this setlist have a Spotify link yet." });

        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "Spotify isn't connected for this band yet." });

        var namePart = string.Join(" - ", new[] { gig.Band.Name, gig.Title, GigDateTimeFormatting.FormatDate(gig.Date) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var createPayload = JsonSerializer.Serialize(new { name = namePart, description = $"Setlist for {gig.Title}", @public = true });
        using var createRes = await http.PostAsync("https://api.spotify.com/v1/me/playlists",
            new StringContent(createPayload, System.Text.Encoding.UTF8, "application/json"));
        var createBody = await createRes.Content.ReadAsStringAsync();
        if (!createRes.IsSuccessStatusCode) return BadRequest(new { error = ParseSpotifyError(createBody) ?? "Could not create the playlist." });

        using var createDoc = JsonDocument.Parse(createBody);
        var playlistId = createDoc.RootElement.GetProperty("id").GetString();
        var playlistUrl = createDoc.RootElement.GetProperty("external_urls").GetProperty("spotify").GetString();

        // One request, up to 100 URIs - a setlist is never remotely close
        // to that ceiling, so no need to page this.
        var addPayload = JsonSerializer.Serialize(new { uris = trackUris });
        using var addRes = await http.PostAsync($"https://api.spotify.com/v1/playlists/{playlistId}/tracks",
            new StringContent(addPayload, System.Text.Encoding.UTF8, "application/json"));
        if (!addRes.IsSuccessStatusCode)
        {
            var addBody = await addRes.Content.ReadAsStringAsync();
            return BadRequest(new { error = $"Playlist was created, but adding songs failed: {ParseSpotifyError(addBody) ?? addBody}" });
        }

        return Ok(new { id = playlistId, name = namePart, url = playlistUrl, trackCount = trackUris.Count, skipped = set.Songs.Count - trackUris.Count });
    }

    private static string? SpotifyTrackUriFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(url, @"open\.spotify\.com/track/([a-zA-Z0-9]+)");
        return match.Success ? $"spotify:track:{match.Groups[1].Value}" : null;
    }

    // Spotify has no true "delete playlist" for the API caller - the real
    // operation is unfollowing it (removes it from the connected account's
    // library, matching what a person clicking "Delete" in the Spotify
    // app actually does under the hood).
    [HttpDelete("playlists/{playlistId}")]
    public async Task<IActionResult> DeletePlaylist(string playlistId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "Spotify isn't connected for this band yet." });

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var res = await http.DeleteAsync($"https://api.spotify.com/v1/playlists/{playlistId}/followers");
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            return BadRequest(new { error = ParseSpotifyError(body) ?? "Could not remove the playlist." });
        }
        return Ok(new { ok = true });
    }

    [HttpPost("playlists/{playlistId}/tracks")]
    public async Task<IActionResult> AddTrack(string playlistId, [FromBody] SpotifyTrackRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.TrackUri)) return BadRequest(new { error = "trackUri is required." });

        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "Spotify isn't connected for this band yet." });

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var payload = JsonSerializer.Serialize(new { uris = new[] { request.TrackUri } });
        using var res = await http.PostAsync($"https://api.spotify.com/v1/playlists/{playlistId}/tracks",
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            return BadRequest(new { error = ParseSpotifyError(body) ?? "Could not add that track." });
        }
        return Ok(new { ok = true });
    }

    [HttpDelete("playlists/{playlistId}/tracks")]
    public async Task<IActionResult> RemoveTrack(string playlistId, [FromBody] SpotifyTrackRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.TrackUri)) return BadRequest(new { error = "trackUri is required." });

        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "Spotify isn't connected for this band yet." });

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var request2 = new HttpRequestMessage(HttpMethod.Delete, $"https://api.spotify.com/v1/playlists/{playlistId}/tracks")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { tracks = new[] { new { uri = request.TrackUri } } }),
                System.Text.Encoding.UTF8, "application/json")
        };
        using var res = await http.SendAsync(request2);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            return BadRequest(new { error = ParseSpotifyError(body) ?? "Could not remove that track." });
        }
        return Ok(new { ok = true });
    }
}
