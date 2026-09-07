using System.Security.Cryptography;
using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// OAuth connect/callback/disconnect for a band member's own Google
/// Calendar or Outlook calendar - Phase 8 scaffolding (see the Calendar
/// plan's Phase 8). Functional end to end once a SuperAdmin has saved the
/// corresponding OAuth app's Client ID/Secret (see
/// SuperAdminController.GetCalendarOAuthCredentials) and this instance is
/// reachable at a real public URL matching the redirect URI registered
/// with Google/Microsoft - untested against real Google/Microsoft
/// endpoints as of this writing, since both require setup only the
/// account owner can do (see this controller's originating commit
/// message for the full explanation).
///
/// The connect/callback split can't rely on the active-band session (an
/// OAuth provider's consent screen is a different origin), so identity
/// flows through the normal auth cookie, which survives the round trip
/// back to this same origin's callback URL - plus a short-lived state
/// cookie as a CSRF check, the standard pattern for this flow.
/// </summary>
[ApiController]
[Route("/api/external-calendar")]
[Authorize]
public class ExternalCalendarController(
    ApplicationDbContext db,
    ICredentialCipher cipher,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private const string GoogleCalendarOAuthKey = "google_calendar_oauth_credentials";
    private const string OutlookOAuthKey = "outlook_oauth_credentials";
    private const string StateCookiePrefix = "ext-cal-oauth-state-";

    private static bool TryParseProvider(string provider, out ExternalCalendarProvider parsed) =>
        Enum.TryParse(provider, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);

    private string RedirectUri(string provider) => $"{Request.Scheme}://{Request.Host}/api/external-calendar/{provider}/callback";

    /// <summary>Which providers a SuperAdmin has actually configured - lets
    /// the Calendar page show/hide "Connect Google/Outlook" without
    /// exposing the credentials themselves to a non-SuperAdmin.</summary>
    [HttpGet("providers")]
    public async Task<IActionResult> AvailableProviders()
    {
        var google = await SongSearchService.GetCredentialAsync(db, cipher, GoogleCalendarOAuthKey);
        var outlook = await SongSearchService.GetCredentialAsync(db, cipher, OutlookOAuthKey);
        return Ok(new { google = google is not null, outlook = outlook is not null });
    }

    [HttpGet("connections")]
    public async Task<IActionResult> ListConnections()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var connections = await db.UserExternalCalendarConnections.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { provider = c.Provider.ToString(), connectedAt = c.ConnectedAt, calendarId = c.ExternalCalendarId })
            .ToListAsync();
        return Ok(connections);
    }

    [HttpGet("{provider}/connect")]
    public async Task<IActionResult> Connect(string provider)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!TryParseProvider(provider, out var parsedProvider)) return NotFound();

        var creds = await SongSearchService.GetCredentialAsync(db, cipher,
            parsedProvider == ExternalCalendarProvider.Google ? GoogleCalendarOAuthKey : OutlookOAuthKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || string.IsNullOrEmpty(clientId))
            return BadRequest(new { error = $"{provider} calendar sync isn't set up yet - ask a SuperAdmin to configure it first." });

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Response.Cookies.Append($"{StateCookiePrefix}{provider}", state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var redirectUri = Uri.EscapeDataString(RedirectUri(provider));
        var authorizeUrl = parsedProvider == ExternalCalendarProvider.Google
            ? $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={redirectUri}" +
              "&response_type=code&access_type=offline&prompt=consent" +
              $"&scope={Uri.EscapeDataString("https://www.googleapis.com/auth/calendar.events")}&state={state}"
            : $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&redirect_uri={redirectUri}" +
              "&response_type=code&response_mode=query" +
              $"&scope={Uri.EscapeDataString("offline_access Calendars.ReadWrite")}&state={state}";

        return Redirect(authorizeUrl);
    }

    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!TryParseProvider(provider, out var parsedProvider)) return NotFound();

        var stateCookieName = $"{StateCookiePrefix}{provider}";
        var expectedState = Request.Cookies[stateCookieName];
        Response.Cookies.Delete(stateCookieName);

        if (!string.IsNullOrEmpty(error)) return BadRequest(new { error = $"{provider} declined the connection: {error}" });
        if (string.IsNullOrEmpty(code)) return BadRequest(new { error = "Missing authorization code." });
        if (string.IsNullOrEmpty(expectedState) || expectedState != state) return BadRequest(new { error = "Invalid or expired connection attempt - please try again." });

        var creds = await SongSearchService.GetCredentialAsync(db, cipher,
            parsedProvider == ExternalCalendarProvider.Google ? GoogleCalendarOAuthKey : OutlookOAuthKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || !creds.TryGetValue("clientSecret", out var clientSecret))
            return BadRequest(new { error = $"{provider} calendar sync isn't set up yet - ask a SuperAdmin to configure it first." });

        var tokenEndpoint = parsedProvider == ExternalCalendarProvider.Google
            ? "https://oauth2.googleapis.com/token"
            : "https://login.microsoftonline.com/common/oauth2/v2.0/token";

        var http = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri(provider)
        };
        using var tokenRes = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form));
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();
        if (!tokenRes.IsSuccessStatusCode)
            return BadRequest(new { error = $"{provider} token exchange failed: {tokenBody}" });

        using var tokenJson = JsonDocument.Parse(tokenBody);
        var root = tokenJson.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        var tokenPayload = JsonSerializer.Serialize(new
        {
            accessToken,
            refreshToken,
            expiresAt = DateTime.UtcNow.AddSeconds(expiresIn)
        });

        var existing = await db.UserExternalCalendarConnections.FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == parsedProvider);
        if (existing is null)
        {
            db.UserExternalCalendarConnections.Add(new UserExternalCalendarConnection
            {
                UserId = userId.Value,
                Provider = parsedProvider,
                EncryptedTokens = cipher.Encrypt(tokenPayload)
            });
        }
        else
        {
            existing.EncryptedTokens = cipher.Encrypt(tokenPayload);
            existing.ConnectedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        return Redirect("/calendar.html?calendarConnected=1");
    }

    [HttpDelete("{provider}")]
    public async Task<IActionResult> Disconnect(string provider)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!TryParseProvider(provider, out var parsedProvider)) return NotFound();

        var existing = await db.UserExternalCalendarConnections.FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == parsedProvider);
        if (existing is not null)
        {
            db.UserExternalCalendarConnections.Remove(existing);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
