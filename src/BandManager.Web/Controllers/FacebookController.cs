using System.Text.Json;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandManager.Web.Controllers;

public record FacebookAutoSetupRequest(string AppId, string AppSecret, string ShortLivedToken, string? PageId, string? TokenExpiresAt);

/// <summary>
/// Settings' "Finish Setup Automatically" tool for Facebook/Instagram -
/// takes a Meta App ID/Secret and a short-lived user access token (both
/// copied by hand from Facebook's own Graph API Explorer/App Dashboard,
/// per MetaInstructions in PlatformSeedData.cs), and does the rest of what
/// those instructions otherwise ask a Band Admin to do manually: exchange
/// for a long-lived token, find the Page(s) it manages, save the Page's
/// own access token, detect a linked Instagram Business Account, and
/// verify the saved connection actually works.
///
/// Was previously frontend-only (settings.js:wireAutoSetupTool) with no
/// backend at all - FacebookPublisher.cs's own doc comment named this
/// exact method (autoSetupFromToken) as deferred since "Fart it out"
/// doesn't depend on it. This fills that in.
/// </summary>
[ApiController]
[Route("/api/facebook")]
[Authorize(Policy = "BandAdmin")]
public class FacebookController(IActiveBandAccessor activeBand, CredentialStore credentialStore, HttpClient http) : ControllerBase
{
    private const string GraphApi = "https://graph.facebook.com/v19.0";

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static async Task<(bool Ok, JsonElement Body, string? Error)> GetJsonAsync(HttpClient http, string url)
    {
        using var res = await http.GetAsync(url);
        JsonElement body;
        try { body = await res.Content.ReadFromJsonAsync<JsonElement>(); }
        catch { return (res.IsSuccessStatusCode, default, res.ReasonPhrase ?? "unknown error"); }

        if (res.IsSuccessStatusCode) return (true, body, null);
        var error = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg)
            ? msg.GetString() : res.ReasonPhrase;
        return (false, body, error ?? "unknown error");
    }

    // Pre-fills the tool's App ID/Secret fields from what was saved on a
    // previous successful run, saved as extra keys on the same "facebook"
    // credential auto-setup itself writes (below) - not a separate store.
    [HttpGet("app-config")]
    public async Task<IActionResult> AppConfig()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var creds = await credentialStore.GetCredentialAsync(bandId, "facebook");
        return Ok(new
        {
            appId = creds?.GetValueOrDefault("appId"),
            appSecret = creds?.GetValueOrDefault("appSecret")
        });
    }

    [HttpPost("auto-setup")]
    public async Task<IActionResult> AutoSetup([FromBody] FacebookAutoSetupRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var appId = request.AppId?.Trim();
        var appSecret = request.AppSecret?.Trim();
        var shortLivedToken = request.ShortLivedToken?.Trim();
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(shortLivedToken))
            return BadRequest(new { error = "App ID, App Secret, and a short-lived token are all required." });

        // Step 1: exchange the short-lived user token for a long-lived one
        // (~60 days, per Meta's own default) - a Page token derived from
        // this below effectively doesn't expire on its own as long as the
        // underlying permission grant stays valid, but we still record the
        // long-lived token's own expiry for the Settings status line.
        var exchangeUrl = $"{GraphApi}/oauth/access_token?grant_type=fb_exchange_token" +
            $"&client_id={Uri.EscapeDataString(appId)}&client_secret={Uri.EscapeDataString(appSecret)}" +
            $"&fb_exchange_token={Uri.EscapeDataString(shortLivedToken)}";
        var (exchangeOk, exchangeBody, exchangeError) = await GetJsonAsync(http, exchangeUrl);
        if (!exchangeOk) return BadRequest(new { error = $"Could not exchange that token - double check the App ID/Secret and that the token hasn't already expired: {exchangeError}" });
        if (!exchangeBody.TryGetProperty("access_token", out var longTokenEl))
            return BadRequest(new { error = "Facebook didn't return a long-lived token for that exchange." });
        var longLivedToken = longTokenEl.GetString()!;
        var expiresInSeconds = exchangeBody.TryGetProperty("expires_in", out var expiresEl) && expiresEl.TryGetInt64(out var secs) ? secs : (long?)null;

        // Step 2: list the Pages this token manages, each with its own
        // Page Access Token already included by the API - no separate
        // exchange needed per Page.
        var pagesUrl = $"{GraphApi}/me/accounts?access_token={Uri.EscapeDataString(longLivedToken)}";
        var (pagesOk, pagesBody, pagesError) = await GetJsonAsync(http, pagesUrl);
        if (!pagesOk) return BadRequest(new { error = $"Could not look up the Pages this token manages: {pagesError}" });
        if (!pagesBody.TryGetProperty("data", out var pagesData) || pagesData.ValueKind != JsonValueKind.Array)
            return BadRequest(new { error = "Facebook didn't return a Page list for that token." });

        var pages = pagesData.EnumerateArray()
            .Select(p => (Id: p.GetProperty("id").GetString()!, Name: p.GetProperty("name").GetString()!, Token: p.GetProperty("access_token").GetString()!))
            .ToList();
        if (pages.Count == 0)
            return BadRequest(new { error = "That token doesn't manage any Facebook Pages - it needs to belong to someone who's an admin/editor on this band's Page." });

        if (pages.Count > 1 && string.IsNullOrEmpty(request.PageId))
            return Ok(new { needsPageChoice = true, pages = pages.Select(p => new { id = p.Id, name = p.Name }) });

        var page = pages.Count == 1 ? pages[0] : pages.FirstOrDefault(p => p.Id == request.PageId);
        if (page.Id is null)
            return BadRequest(new { error = "That Page wasn't found among the ones this token manages." });

        // Step 3: check for a linked Instagram Business Account - the same
        // query MetaInstructions already walks an admin through by hand.
        var igUrl = $"{GraphApi}/{page.Id}?fields=instagram_business_account&access_token={Uri.EscapeDataString(page.Token)}";
        var (igOk, igBody, _) = await GetJsonAsync(http, igUrl);
        var igUserId = igOk && igBody.TryGetProperty("instagram_business_account", out var igAccount) && igAccount.TryGetProperty("id", out var igIdEl)
            ? igIdEl.GetString() : null;

        // Step 4: save. appId/appSecret ride along on the same "facebook"
        // credential purely so app-config (above) can pre-fill them next
        // time - FacebookPublisher itself only ever reads pageId/
        // pageAccessToken.
        await credentialStore.SetCredentialAsync(bandId, "facebook", new Dictionary<string, string>
        {
            ["pageId"] = page.Id,
            ["pageAccessToken"] = page.Token,
            ["appId"] = appId,
            ["appSecret"] = appSecret
        });
        if (expiresInSeconds is not null)
            await credentialStore.SetTokenExpiryAsync(bandId, "facebook", DateOnly.FromDateTime(DateTime.UtcNow.AddSeconds(expiresInSeconds.Value)));

        var instagramLinked = !string.IsNullOrEmpty(igUserId);
        if (instagramLinked)
        {
            await credentialStore.SetCredentialAsync(bandId, "instagram", new Dictionary<string, string>
            {
                ["mode"] = "linked",
                ["igUserId"] = igUserId!
            });
        }

        // Step 5: verify the saved Page token actually works, and record
        // that result the same place the Settings status line already
        // reads it from (CredentialsController.Status).
        var verifyUrl = $"{GraphApi}/{page.Id}?fields=id,name&access_token={Uri.EscapeDataString(page.Token)}";
        var (verifyOk, _, verifyError) = await GetJsonAsync(http, verifyUrl);
        await credentialStore.SetVerificationResultAsync(bandId, "facebook", verifyOk, verifyError);

        return Ok(new
        {
            pageName = page.Name,
            instagramLinked,
            verification = verifyOk ? new { ok = true, error = (string?)null } : new { ok = false, error = verifyError }
        });
    }
}
