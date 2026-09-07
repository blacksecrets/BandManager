using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Services;

/// <summary>
/// Pushes Gig/Rehearsal changes to every connected user's own Outlook
/// calendar (Microsoft Graph v1.0, /me/events) - same Phase 8 scaffolding
/// status as GoogleCalendarPushService, see ExternalCalendarController's
/// doc comment. Unlike Google, Graph doesn't accept a caller-chosen event
/// id on create, so idempotency instead tags each event with a single-
/// value extended property carrying our own Gig/Rehearsal id, looked up
/// before deciding whether to create or patch.
/// </summary>
public class OutlookCalendarPushService(ApplicationDbContext db, ICredentialCipher cipher, IHttpClientFactory httpClientFactory, ILogger<OutlookCalendarPushService> logger)
{
    private const string OutlookOAuthKey = "outlook_oauth_credentials";
    // This app's own fixed namespace for Graph single-value extended
    // properties - arbitrary but stable, generated once for BandManager.
    private const string ExtendedPropertyId = "String {66f5a359-4659-4830-9070-00047ec6ac6e} Name BandManagerUid";

    private sealed record StoredTokens(string? AccessToken, string? RefreshToken, DateTime ExpiresAt);

    private async Task<string?> GetValidAccessTokenAsync(UserExternalCalendarConnection connection)
    {
        var tokens = JsonSerializer.Deserialize<StoredTokens>(cipher.Decrypt(connection.EncryptedTokens));
        if (tokens is null) return null;
        if (tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(1)) return tokens.AccessToken;
        if (string.IsNullOrEmpty(tokens.RefreshToken)) return null;

        var creds = await SongSearchService.GetCredentialAsync(db, cipher, OutlookOAuthKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || !creds.TryGetValue("clientSecret", out var clientSecret))
            return null;

        var http = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = tokens.RefreshToken,
            ["grant_type"] = "refresh_token"
        };
        using var res = await http.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", new FormUrlEncodedContent(form));
        if (!res.IsSuccessStatusCode)
        {
            db.UserExternalCalendarConnections.Remove(connection);
            await db.SaveChangesAsync();
            return null;
        }

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var newAccessToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        connection.EncryptedTokens = cipher.Encrypt(JsonSerializer.Serialize(new StoredTokens(newAccessToken, tokens.RefreshToken, DateTime.UtcNow.AddSeconds(expiresIn))));
        await db.SaveChangesAsync();
        return newAccessToken;
    }

    private async Task<List<UserExternalCalendarConnection>> ConnectedMembersAsync(Guid bandId)
    {
        var memberIds = await db.BandMemberships.Where(m => m.BandId == bandId).Select(m => m.UserId).ToListAsync();
        return await db.UserExternalCalendarConnections
            .Where(c => memberIds.Contains(c.UserId) && c.Provider == ExternalCalendarProvider.Outlook)
            .ToListAsync();
    }

    private async Task<string?> FindExistingEventIdAsync(HttpClient http, string accessToken, string uid)
    {
        var filter = Uri.EscapeDataString($"singleValueExtendedProperties/Any(ep: ep/id eq '{ExtendedPropertyId}' and ep/value eq '{uid}')");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0/me/events?$filter={filter}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("value", out var items) || items.GetArrayLength() == 0) return null;
        return items[0].GetProperty("id").GetString();
    }

    private async Task UpsertEventAsync(UserExternalCalendarConnection connection, string uid, object eventFields)
    {
        var accessToken = await GetValidAccessTokenAsync(connection);
        if (accessToken is null) return;

        var http = httpClientFactory.CreateClient();
        var existingId = await FindExistingEventIdAsync(http, accessToken, uid);

        var body = JsonSerializer.SerializeToNode(eventFields)!.AsObject();
        if (existingId is null)
        {
            body["singleValueExtendedProperties"] = new System.Text.Json.Nodes.JsonArray(
                System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new { id = ExtendedPropertyId, value = uid })));
        }
        var payload = body.ToJsonString();

        var url = existingId is null ? "https://graph.microsoft.com/v1.0/me/events" : $"https://graph.microsoft.com/v1.0/me/events/{existingId}";
        var method = existingId is null ? HttpMethod.Post : HttpMethod.Patch;
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            logger.LogWarning("Outlook Calendar push failed for user {UserId}: {Status}", connection.UserId, res.StatusCode);
    }

    private async Task DeleteEventAsync(UserExternalCalendarConnection connection, string uid)
    {
        var accessToken = await GetValidAccessTokenAsync(connection);
        if (accessToken is null) return;

        var http = httpClientFactory.CreateClient();
        var existingId = await FindExistingEventIdAsync(http, accessToken, uid);
        if (existingId is null) return;

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/me/events/{existingId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(req);
    }

    public async Task PushGigAsync(Gig gig)
    {
        var uid = $"gig{gig.Id:N}";
        var fields = new
        {
            subject = gig.Title,
            location = new { displayName = gig.Venue ?? "" },
            isAllDay = true,
            start = new { dateTime = $"{gig.Date}T00:00:00", timeZone = "UTC" },
            end = new { dateTime = $"{gig.Date}T00:00:00", timeZone = "UTC" }
        };
        foreach (var connection in await ConnectedMembersAsync(gig.BandId))
            await UpsertEventAsync(connection, uid, fields);
    }

    public async Task DeleteGigAsync(Guid bandId, Guid gigId)
    {
        var uid = $"gig{gigId:N}";
        foreach (var connection in await ConnectedMembersAsync(bandId))
            await DeleteEventAsync(connection, uid);
    }

    public async Task PushRehearsalAsync(Rehearsal rehearsal)
    {
        var uid = $"rehearsal{rehearsal.Id:N}";
        var fields = new
        {
            subject = rehearsal.Title ?? "Rehearsal",
            location = new { displayName = rehearsal.Location ?? "" },
            start = new { dateTime = rehearsal.StartsAt.ToString("o"), timeZone = "UTC" },
            end = new { dateTime = rehearsal.EndsAt.ToString("o"), timeZone = "UTC" }
        };
        foreach (var connection in await ConnectedMembersAsync(rehearsal.BandId))
            await UpsertEventAsync(connection, uid, fields);
    }

    public async Task DeleteRehearsalAsync(Guid bandId, Guid rehearsalId)
    {
        var uid = $"rehearsal{rehearsalId:N}";
        foreach (var connection in await ConnectedMembersAsync(bandId))
            await DeleteEventAsync(connection, uid);
    }
}
