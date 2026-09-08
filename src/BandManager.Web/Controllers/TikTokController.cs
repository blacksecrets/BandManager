using System.Security.Cryptography;
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

public record UploadTikTokVideoRequest(Guid CatalogItemId, string Title, string PrivacyLevel = "SELF_ONLY");

/// <summary>
/// TikTok Content Posting API - same OAuth+upload shape as Spotify/
/// YouTube, but with a real caveat neither of those has: TikTok requires
/// the *app itself* (this whole BandManager instance) to pass their own
/// developer audit before anything posted through it is publicly visible.
/// Until that's done, every post through this controller lands as
/// private/SELF_ONLY regardless of what's requested - TikTok enforces
/// that server-side for unaudited apps, this code can't override it (see
/// this controller's originating commit message for the research this is
/// based on). Code-complete and ready to go the moment the audit clears;
/// the audit itself is a business/registration step only the account
/// owner can do, not something buildable here.
///
/// PrivacyLevel defaults to SELF_ONLY specifically so a band that tries
/// this before the audit clears doesn't get a confusing rejection - it
/// just quietly posts privately, same as what TikTok would force anyway.
/// </summary>
[ApiController]
[Route("/api/tiktok")]
[Authorize]
public class TikTokController(
    ApplicationDbContext db,
    ICredentialCipher cipher,
    CredentialStore credentialStore,
    IActiveBandAccessor activeBand,
    CatalogStore catalogStore,
    CadenceAutoLinkService cadenceAutoLink,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private const string PlatformId = "tiktok";
    private const string OAuthAppKey = "tiktok_oauth_credentials";
    private const string StateCookieName = "tiktok-oauth-state";

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private string RedirectUri => $"{Request.Scheme}://{Request.Host}/api/tiktok/callback";

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
        if (creds is null || !creds.TryGetValue("clientKey", out var clientKey) || string.IsNullOrEmpty(clientKey))
            return BadRequest(new { error = "TikTok posting isn't set up yet - ask a SuperAdmin to configure it first." });

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var redirectUri = Uri.EscapeDataString(RedirectUri);
        var url = $"https://www.tiktok.com/v2/auth/authorize/?client_key={clientKey}&response_type=code" +
                  $"&scope={Uri.EscapeDataString("video.publish")}&redirect_uri={redirectUri}&state={state}";
        return Redirect(url);
    }

    [Authorize(Policy = "BandAdmin")]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery(Name = "error")] string? error)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var expectedState = Request.Cookies[StateCookieName];
        Response.Cookies.Delete(StateCookieName);

        if (!string.IsNullOrEmpty(error)) return BadRequest(new { error = $"TikTok declined the connection: {error}" });
        if (string.IsNullOrEmpty(code)) return BadRequest(new { error = "Missing authorization code." });
        if (string.IsNullOrEmpty(expectedState) || expectedState != state) return BadRequest(new { error = "Invalid or expired connection attempt - please try again." });

        var creds = await SongSearchService.GetCredentialAsync(db, cipher, OAuthAppKey);
        if (creds is null || !creds.TryGetValue("clientKey", out var clientKey) || !creds.TryGetValue("clientSecret", out var clientSecret))
            return BadRequest(new { error = "TikTok posting isn't set up yet - ask a SuperAdmin to configure it first." });

        var http = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_key"] = clientKey,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri
        };
        using var tokenRes = await http.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", new FormUrlEncodedContent(form));
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();
        if (!tokenRes.IsSuccessStatusCode)
            return BadRequest(new { error = $"TikTok token exchange failed: {tokenBody}" });

        using var tokenJson = JsonDocument.Parse(tokenBody);
        var root = tokenJson.RootElement;
        if (!root.TryGetProperty("access_token", out var atEl))
            return BadRequest(new { error = $"TikTok token exchange failed: {tokenBody}" });
        var accessToken = atEl.GetString()!;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 86400;

        if (refreshToken is null)
            return BadRequest(new { error = "TikTok didn't return a refresh token - try disconnecting and reconnecting." });

        await credentialStore.SetCredentialAsync(bandId, PlatformId, new()
        {
            ["accessToken"] = accessToken,
            ["refreshToken"] = refreshToken,
            ["expiresAt"] = DateTime.UtcNow.AddSeconds(expiresIn).ToString("O")
        });
        await credentialStore.SetOnboardedAsync(bandId, PlatformId, true);

        return Redirect("/catalog.html?tiktokConnected=1");
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
        if (oauthCreds is null || !oauthCreds.TryGetValue("clientKey", out var clientKey) || !oauthCreds.TryGetValue("clientSecret", out var clientSecret))
            return null;

        var http = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_key"] = clientKey,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        };
        using var res = await http.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", new FormUrlEncodedContent(form));
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("access_token", out var newAtEl)) return null;
        var newAccessToken = newAtEl.GetString()!;
        var newExpiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 86400;
        var newRefreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refreshToken;

        await credentialStore.SetCredentialAsync(bandId, PlatformId, new()
        {
            ["accessToken"] = newAccessToken,
            ["refreshToken"] = newRefreshToken!,
            ["expiresAt"] = DateTime.UtcNow.AddSeconds(newExpiresIn).ToString("O")
        });
        return newAccessToken;
    }

    /// <summary>Direct Post, single-chunk upload (init + one PUT) - fine
    /// for the file sizes a band actually posts; TikTok's own chunking is
    /// only worth the extra complexity for much larger files than that.
    /// See this class's doc comment for the private-until-audited caveat -
    /// this code can't make a post public before that's done, and doesn't
    /// try to.</summary>
    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] UploadTikTokVideoRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return BadRequest(new { error = "Title is required." });

        var item = await db.CatalogItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CatalogItemId && c.BandId == bandId);
        if (item is null || item.MediaType != MediaType.Video) return BadRequest(new { error = "That's not a video in this band's Catalog." });

        var token = await GetAccessTokenAsync(bandId);
        if (token is null) return BadRequest(new { error = "TikTok isn't connected for this band yet." });

        byte[] bytes;
        try { bytes = await catalogStore.GetCatalogItemBufferAsync(bandId, item.Id); }
        catch (Exception ex) { return BadRequest(new { error = $"Could not read that video file: {ex.Message}" }); }

        var http = httpClientFactory.CreateClient();
        var initBody = JsonSerializer.Serialize(new
        {
            post_info = new { title, privacy_level = request.PrivacyLevel },
            source_info = new
            {
                source = "FILE_UPLOAD",
                video_size = bytes.Length,
                chunk_size = bytes.Length,
                total_chunk_count = 1
            }
        });
        using var initRequest = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/post/publish/video/init/")
        {
            Content = new StringContent(initBody, System.Text.Encoding.UTF8, "application/json")
        };
        initRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var initRes = await http.SendAsync(initRequest);
        var initResBody = await initRes.Content.ReadAsStringAsync();
        if (!initRes.IsSuccessStatusCode) return BadRequest(new { error = $"TikTok rejected the upload request: {initResBody}" });

        using var initDoc = JsonDocument.Parse(initResBody);
        var data = initDoc.RootElement.GetProperty("data");
        var uploadUrl = data.GetProperty("upload_url").GetString();
        var publishId = data.GetProperty("publish_id").GetString();
        if (uploadUrl is null) return BadRequest(new { error = "TikTok didn't return an upload URL." });

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(bytes) };
        uploadRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(item.MimeType);
        uploadRequest.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, bytes.Length - 1, bytes.Length);
        using var uploadRes = await http.SendAsync(uploadRequest);
        if (!uploadRes.IsSuccessStatusCode)
        {
            var uploadErr = await uploadRes.Content.ReadAsStringAsync();
            return BadRequest(new { error = $"Upload failed: {uploadErr}" });
        }

        var linkedTile = await cadenceAutoLink.AutoCompleteVideoTileAsync(bandId, PlatformId, ["Short Video"]);

        return Ok(new
        {
            ok = true,
            publishId,
            note = "Posting is queued - if this app hasn't completed TikTok's developer audit yet, it'll only be visible privately to the connected account regardless of the privacy level requested.",
            autoCompletedTile = linkedTile is not null
        });
    }
}
