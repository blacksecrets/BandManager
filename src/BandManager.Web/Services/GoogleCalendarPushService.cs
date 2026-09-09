using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Services;

/// <summary>
/// Pushes Gig/Rehearsal changes to every connected user's own Google
/// Calendar (Calendar API v3, "primary" calendar) - Phase 8 scaffolding,
/// see ExternalCalendarController's doc comment for what's untested and
/// why. Refreshes an expired access token via the stored refresh token;
/// quietly removes the connection on an irrecoverable auth failure
/// (revoked/expired refresh token) rather than erroring the gig/rehearsal
/// write that triggered this - matches this app's established "the
/// primary write already succeeded, a push is best-effort" pattern from
/// GigsSiteEditor/FlyersController.
/// </summary>
public class GoogleCalendarPushService(ApplicationDbContext db, ICredentialCipher cipher, IHttpClientFactory httpClientFactory, ILogger<GoogleCalendarPushService> logger)
{
    private const string GoogleCalendarOAuthKey = "google_calendar_oauth_credentials";

    private sealed record StoredTokens(string? AccessToken, string? RefreshToken, DateTime ExpiresAt);

    private async Task<string?> GetValidAccessTokenAsync(UserExternalCalendarConnection connection)
    {
        var tokens = JsonSerializer.Deserialize<StoredTokens>(cipher.Decrypt(connection.EncryptedTokens));
        if (tokens is null) return null;
        if (tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(1)) return tokens.AccessToken;
        if (string.IsNullOrEmpty(tokens.RefreshToken)) return null;

        var creds = await BandManager.Web.Services.SongSearchService.GetCredentialAsync(db, cipher, GoogleCalendarOAuthKey);
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
        using var res = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        if (!res.IsSuccessStatusCode)
        {
            // Refresh token itself is dead (revoked by the user, or
            // expired) - the connection can't self-heal, so drop it
            // rather than retrying forever on every future gig change.
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
            .Where(c => memberIds.Contains(c.UserId) && c.Provider == ExternalCalendarProvider.Google)
            .ToListAsync();
    }

    private async Task UpsertEventAsync(UserExternalCalendarConnection connection, string uid, object body)
    {
        var accessToken = await GetValidAccessTokenAsync(connection);
        if (accessToken is null) return;

        var http = httpClientFactory.CreateClient();
        // Google's Events.insert doesn't take a caller-chosen id directly
        // in the body's "id" the way iCalUID does on lookup, but does
        // accept "id" on insert as long as it's a valid Google event id
        // shape - a lowercase-hex UID (see Gig.Id/Rehearsal.Id formatted
        // without dashes) satisfies that, so this doubles as our
        // idempotency key: retrying an insert for an id that already
        // exists 409s, which is treated as "already there" below.
        var payload = JsonSerializer.Serialize(body);
        using var insertReq = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/calendars/primary/events");
        insertReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        insertReq.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var insertRes = await http.SendAsync(insertReq);
        if (insertRes.IsSuccessStatusCode || insertRes.StatusCode == System.Net.HttpStatusCode.Conflict) return;

        // Already exists (a prior push created it) - patch instead.
        using var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"https://www.googleapis.com/calendar/v3/calendars/primary/events/{uid}");
        patchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        patchReq.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var patchRes = await http.SendAsync(patchReq);
        if (!patchRes.IsSuccessStatusCode)
            logger.LogWarning("Google Calendar push failed for user {UserId}: {Status}", connection.UserId, patchRes.StatusCode);
    }

    private async Task DeleteEventAsync(UserExternalCalendarConnection connection, string uid)
    {
        var accessToken = await GetValidAccessTokenAsync(connection);
        if (accessToken is null) return;

        var http = httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/calendar/v3/calendars/primary/events/{uid}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(req);
        // 404/410 both mean "already gone" - fine either way.
    }

    public async Task PushGigAsync(Gig gig)
    {
        var uid = $"gig{gig.Id:N}";
        // Google's all-day-event "date" field needs strict ISO 8601
        // (yyyy-MM-dd) - previously this passed Gig.Date's free-text
        // display string ("Friday, October 3, 2026") straight through,
        // which Google's API would have rejected; now that Date is a real
        // DateOnly, this is both the type-correct AND the first-time-
        // actually-valid format.
        var isoDate = gig.Date.ToString("yyyy-MM-dd");
        var body = new
        {
            id = uid,
            summary = gig.Title,
            location = gig.Venue,
            start = new { date = isoDate },
            end = new { date = isoDate }
        };
        foreach (var connection in await ConnectedMembersAsync(gig.BandId))
            await UpsertEventAsync(connection, uid, body);
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
        var body = new
        {
            id = uid,
            summary = rehearsal.Title ?? "Rehearsal",
            location = rehearsal.Location,
            start = new { dateTime = rehearsal.StartsAt.ToString("o"), timeZone = "UTC" },
            end = new { dateTime = rehearsal.EndsAt.ToString("o"), timeZone = "UTC" }
        };
        foreach (var connection in await ConnectedMembersAsync(rehearsal.BandId))
            await UpsertEventAsync(connection, uid, body);
    }

    public async Task DeleteRehearsalAsync(Guid bandId, Guid rehearsalId)
    {
        var uid = $"rehearsal{rehearsalId:N}";
        foreach (var connection in await ConnectedMembersAsync(bandId))
            await DeleteEventAsync(connection, uid);
    }
}
