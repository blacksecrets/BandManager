using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BandManager.Data.Services;

namespace BandManager.Web.Publishers;

/// <summary>
/// Google Business Profile - Posts API. Ported from the old app's
/// src/publishers/googleBusiness.js. Needs a credential stored under
/// platform "googleBusiness": { accountId, locationId, accessToken } -
/// an OAuth access token from a Google Cloud project with the Business
/// Profile API enabled. Same known limitation as the old app: these
/// tokens expire quickly (~1 hour, per the Setup instructions already
/// ported in PlatformSeedData) and there's no refresh-token flow yet -
/// posting stops working until a fresh token is pasted in.
/// </summary>
public class GoogleBusinessPublisher(HttpClient http, CredentialStore credentialStore)
{
    private async Task<Dictionary<string, string>> RequireCredsAsync(Guid bandId)
    {
        var creds = await credentialStore.GetCredentialAsync(bandId, "googleBusiness");
        if (creds is null
            || !creds.TryGetValue("accountId", out var accountId) || string.IsNullOrEmpty(accountId)
            || !creds.TryGetValue("locationId", out var locationId) || string.IsNullOrEmpty(locationId)
            || !creds.TryGetValue("accessToken", out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("Google Business Profile is not configured yet - add account/location IDs and an access token in Settings.");
        }
        return creds;
    }

    public async Task<GbpPublishResult> PublishAsync(Guid bandId, string? caption)
    {
        var creds = await RequireCredsAsync(bandId);
        var url = $"https://mybusiness.googleapis.com/v4/accounts/{creds["accountId"]}/locations/{creds["locationId"]}/localPosts";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds["accessToken"]);
        var payload = JsonSerializer.Serialize(new { languageCode = "en-US", summary = caption, topicType = "STANDARD" });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var res = await http.SendAsync(request);
        var bodyText = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            string message = res.ReasonPhrase ?? "unknown error";
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(bodyText);
                if (json.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                    message = msg.GetString() ?? message;
            }
            catch { /* body wasn't JSON */ }
            throw new InvalidOperationException($"Google Business Profile post failed: {message}");
        }

        var body = JsonSerializer.Deserialize<JsonElement>(bodyText);
        var postName = body.TryGetProperty("name", out var n) ? n.GetString() : null;
        return new GbpPublishResult(true, postName);
    }
}

public record GbpPublishResult(bool Ok, string? PostName);
