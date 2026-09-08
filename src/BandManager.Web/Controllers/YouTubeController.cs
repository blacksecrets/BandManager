using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record UploadYouTubeVideoRequest(Guid CatalogItemId, string Title, string? Description, bool IsShort, string PrivacyStatus = "public");

/// <summary>
/// Real YouTube automation: uploading an actual video/Short from the
/// Catalog, not a text/photo promotional post - YouTube has no public API
/// for the latter (Community-tab posts), only for uploading real video
/// content (see this controller's originating commit message for the
/// research). OAuth connect/disconnect is BandAdmin-only (it's the
/// band's own YouTube channel login); uploading is BandMember-scoped,
/// same reasoning as Spotify's playlist actions.
///
/// "Short" isn't a distinct API flag - YouTube classifies a video as a
/// Short automatically from the file itself (vertical/square, <=3
/// minutes). IsShort here just controls whether "#Shorts" gets appended
/// to the description, a well-known signal that helps YouTube's own
/// classifier - it's a hint, not a guarantee, and never overrides what
/// the actual video file looks like.
/// </summary>
[ApiController]
[Route("/api/youtube")]
[Authorize]
public class YouTubeController(
    ApplicationDbContext db,
    ICredentialCipher cipher,
    CredentialStore credentialStore,
    IActiveBandAccessor activeBand,
    CatalogStore catalogStore,
    CadenceAutoLinkService cadenceAutoLink,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private const string PlatformId = "youtube";
    private const string OAuthAppKey = "youtube_oauth_credentials";
    private const string StateCookieName = "youtube-oauth-state";

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private string RedirectUri => $"{Request.Scheme}://{Request.Host}/api/youtube/callback";

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
            return BadRequest(new { error = "YouTube posting isn't set up yet - ask a SuperAdmin to configure it first." });

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var redirectUri = Uri.EscapeDataString(RedirectUri);
        // The broader "manage your YouTube account" scope, not just
        // youtube.upload - videos.delete (and listing what's posted)
        // needs this one; upload alone isn't enough for "add or remove".
        var scope = Uri.EscapeDataString("https://www.googleapis.com/auth/youtube");
        var url = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={redirectUri}" +
                  $"&response_type=code&access_type=offline&prompt=consent&scope={scope}&state={state}";
        return Redirect(url);
    }

    [Authorize(Policy = "BandAdmin")]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var expectedState = Request.Cookies[StateCookieName];
        Response.Cookies.Delete(StateCookieName);

        if (!string.IsNullOrEmpty(error)) return BadRequest(new { error = $"YouTube declined the connection: {error}" });
        if (string.IsNullOrEmpty(code)) return BadRequest(new { error = "Missing authorization code." });
        if (string.IsNullOrEmpty(expectedState) || expectedState != state) return BadRequest(new { error = "Invalid or expired connection attempt - please try again." });

        var creds = await SongSearchService.GetCredentialAsync(db, cipher, OAuthAppKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || !creds.TryGetValue("clientSecret", out var clientSecret))
            return BadRequest(new { error = "YouTube posting isn't set up yet - ask a SuperAdmin to configure it first." });

        var http = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri
        };
        using var tokenRes = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();
        if (!tokenRes.IsSuccessStatusCode)
            return BadRequest(new { error = $"YouTube token exchange failed: {tokenBody}" });

        using var tokenJson = JsonDocument.Parse(tokenBody);
        var root = tokenJson.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        if (refreshToken is null)
            return BadRequest(new { error = "Google didn't return a refresh token - try disconnecting and reconnecting (make sure you're not still signed in from a previous connection)." });

        await credentialStore.SetCredentialAsync(bandId, PlatformId, new()
        {
            ["accessToken"] = accessToken,
            ["refreshToken"] = refreshToken,
            ["expiresAt"] = DateTime.UtcNow.AddSeconds(expiresIn).ToString("O")
        });
        await credentialStore.SetOnboardedAsync(bandId, PlatformId, true);

        return Redirect("/catalog.html?youtubeConnected=1");
    }

    [Authorize(Policy = "BandAdmin")]
    [HttpDelete("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        await credentialStore.ClearCredentialAsync(bandId, PlatformId);
        return Ok(new { ok = true });
    }

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
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        using var res = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var newAccessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var newExpiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        await credentialStore.SetCredentialAsync(bandId, PlatformId, new()
        {
            ["accessToken"] = newAccessToken,
            ["refreshToken"] = refreshToken,
            ["expiresAt"] = DateTime.UtcNow.AddSeconds(newExpiresIn).ToString("O")
        });
        return newAccessToken;
    }

    /// <summary>Video/Shorts upload from an existing Catalog video - single-
    /// request resumable upload (initiate + one PUT of the whole buffer).
    /// Fine for the kind of promo/live-clip files a band posts; a fully
    /// chunked upload would only matter for much larger files than that.</summary>
    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] UploadYouTubeVideoRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return BadRequest(new { error = "Title is required." });

        var item = await db.CatalogItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CatalogItemId && c.BandId == bandId);
        if (item is null || item.MediaType != MediaType.Video) return BadRequest(new { error = "That's not a video in this band's Catalog." });

        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "YouTube isn't connected for this band yet." });

        byte[] bytes;
        try { bytes = await catalogStore.GetCatalogItemBufferAsync(bandId, item.Id); }
        catch (Exception ex) { return BadRequest(new { error = $"Could not read that video file: {ex.Message}" }); }

        var description = request.Description ?? "";
        if (request.IsShort && !description.Contains("#Shorts", StringComparison.OrdinalIgnoreCase))
            description = description.Length > 0 ? $"{description}\n#Shorts" : "#Shorts";

        var http = httpClientFactory.CreateClient();
        var initiateBody = JsonSerializer.Serialize(new
        {
            snippet = new { title, description },
            status = new { privacyStatus = request.PrivacyStatus }
        });
        using var initiateRequest = new HttpRequestMessage(HttpMethod.Post,
            "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status")
        {
            Content = new StringContent(initiateBody, Encoding.UTF8, "application/json")
        };
        initiateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        initiateRequest.Headers.Add("X-Upload-Content-Type", item.MimeType);
        initiateRequest.Headers.Add("X-Upload-Content-Length", bytes.Length.ToString());

        using var initiateRes = await http.SendAsync(initiateRequest);
        if (!initiateRes.IsSuccessStatusCode)
        {
            var errBody = await initiateRes.Content.ReadAsStringAsync();
            return BadRequest(new { error = $"YouTube rejected the upload request: {errBody}" });
        }
        var uploadUrl = initiateRes.Headers.Location?.ToString();
        if (uploadUrl is null) return BadRequest(new { error = "YouTube didn't return an upload URL." });

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(bytes)
        };
        uploadRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(item.MimeType);
        using var uploadRes = await http.SendAsync(uploadRequest);
        var uploadBody = await uploadRes.Content.ReadAsStringAsync();
        if (!uploadRes.IsSuccessStatusCode)
            return BadRequest(new { error = $"Upload failed: {uploadBody}" });

        using var doc = JsonDocument.Parse(uploadBody);
        var videoId = doc.RootElement.GetProperty("id").GetString();

        // Closes the loop with Cadence - see CadenceAutoLinkService's doc
        // comment. "Short" maps straight to the Short/Reel content type;
        // a full-length upload could be either "Full Video" or "Vlog/BTS"
        // (this form has no way to say which), so both are candidates and
        // whichever tile is more overdue wins.
        var candidateContentTypes = request.IsShort ? new[] { "Short/Reel" } : new[] { "Full Video", "Vlog/BTS" };
        var linkedTile = await cadenceAutoLink.AutoCompleteVideoTileAsync(bandId, PlatformId, candidateContentTypes);

        return Ok(new { ok = true, videoId, url = $"https://youtu.be/{videoId}", autoCompletedTile = linkedTile is not null });
    }

    /// <summary>Videos/Shorts on the connected channel - via the channel's
    /// uploads playlist (channels.list + playlistItems.list), not
    /// search.list("forMine") - same result, a fraction of the API quota
    /// cost (search.list is 100 quota units per call; playlistItems.list
    /// is 1).</summary>
    [HttpGet("videos")]
    public async Task<IActionResult> ListVideos()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "YouTube isn't connected for this band yet." });

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var channelRes = await http.GetAsync("https://www.googleapis.com/youtube/v3/channels?part=contentDetails&mine=true");
        var channelBody = await channelRes.Content.ReadAsStringAsync();
        if (!channelRes.IsSuccessStatusCode) return BadRequest(new { error = $"Could not read the connected channel: {channelBody}" });

        using var channelDoc = JsonDocument.Parse(channelBody);
        var items = channelDoc.RootElement.GetProperty("items");
        if (items.GetArrayLength() == 0) return Ok(Array.Empty<object>());
        var uploadsPlaylistId = items[0].GetProperty("contentDetails").GetProperty("relatedPlaylists").GetProperty("uploads").GetString();

        using var videosRes = await http.GetAsync($"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&maxResults=50&playlistId={uploadsPlaylistId}");
        var videosBody = await videosRes.Content.ReadAsStringAsync();
        if (!videosRes.IsSuccessStatusCode) return BadRequest(new { error = $"Could not list videos: {videosBody}" });

        using var videosDoc = JsonDocument.Parse(videosBody);
        var videos = videosDoc.RootElement.GetProperty("items").EnumerateArray().Select(v =>
        {
            var snippet = v.GetProperty("snippet");
            var vid = snippet.GetProperty("resourceId").GetProperty("videoId").GetString();
            var thumb = snippet.TryGetProperty("thumbnails", out var thumbs) && thumbs.TryGetProperty("default", out var def)
                ? def.GetProperty("url").GetString() : null;
            return new { videoId = vid, title = snippet.GetProperty("title").GetString(), url = $"https://youtu.be/{vid}", thumbnail = thumb };
        });
        return Ok(videos);
    }

    [HttpDelete("videos/{videoId}")]
    public async Task<IActionResult> DeleteVideo(string videoId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "YouTube isn't connected for this band yet." });

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var res = await http.DeleteAsync($"https://www.googleapis.com/youtube/v3/videos?id={Uri.EscapeDataString(videoId)}");
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            return BadRequest(new { error = $"Could not remove that video: {body}" });
        }
        return Ok(new { ok = true });
    }
}
