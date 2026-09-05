using System.Text.Json;
using BandManager.Data.Services;

namespace BandManager.Web.Publishers;

/// <summary>
/// Instagram Graph API - feed post via the connected Business account
/// (shares Facebook's Page token model - "API with Facebook Login", not
/// the separate "API with Instagram Login" flow this app deliberately
/// doesn't use). Ported from the old app's src/publishers/instagram.js.
///
/// Needs a credential stored under platform "instagram":
/// { igUserId: "...", pageAccessToken: "..." }.
///
/// Known limitation, inherited as-is from the old app: unlike Facebook's
/// publisher (which uploads the file directly), Instagram's Graph API
/// requires the image at a *publicly reachable URL* - it fetches the
/// image itself rather than accepting a raw upload. The old app's
/// dashboard.js never actually collected/sent that URL on "Fart it out"
/// (its fetch body was always the literal '{}'), so this path has never
/// been exercised end-to-end even in the old app - it needs the flyer to
/// already be live on the Band's own site first (once Website/GitHub
/// publishing and gig-driven items are ported) and a frontend change to
/// actually prompt for/pass imageUrl. Porting the logic now so it's ready
/// once that's true, not pretending the gap doesn't exist.
/// </summary>
public class InstagramPublisher(HttpClient http, CredentialStore credentialStore)
{
    private const string GraphApi = "https://graph.facebook.com/v19.0";

    private async Task<Dictionary<string, string>> RequireCredsAsync(Guid bandId)
    {
        var creds = await credentialStore.GetCredentialAsync(bandId, "instagram");
        if (creds is null || !creds.TryGetValue("igUserId", out var igUserId) || string.IsNullOrEmpty(igUserId)
            || !creds.TryGetValue("pageAccessToken", out var token) || string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Instagram is not configured yet - add an IG Business account ID and access token in Settings.");
        }
        return creds;
    }

    private static async Task<string> ExtractErrorAsync(HttpResponseMessage res)
    {
        try
        {
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? res.ReasonPhrase ?? "unknown error";
        }
        catch { /* body wasn't JSON - fall through to the status line below */ }
        return res.ReasonPhrase ?? "unknown error";
    }

    public async Task<PublishResult> PublishAsync(Guid bandId, string? caption, string? imageUrl)
    {
        var creds = await RequireCredsAsync(bandId);
        var igUserId = creds["igUserId"];
        var token = creds["pageAccessToken"];

        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new InvalidOperationException("Instagram posts need a publicly reachable image URL (publish the flyer to the site first).");

        using var createForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["image_url"] = imageUrl,
            ["caption"] = caption ?? "",
            ["access_token"] = token
        });
        using var createRes = await http.PostAsync($"{GraphApi}/{igUserId}/media", createForm);
        if (!createRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram media create failed: {await ExtractErrorAsync(createRes)}");
        var createBody = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var creationId = createBody.GetProperty("id").GetString();

        using var publishForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["creation_id"] = creationId!,
            ["access_token"] = token
        });
        using var publishRes = await http.PostAsync($"{GraphApi}/{igUserId}/media_publish", publishForm);
        if (!publishRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram publish failed: {await ExtractErrorAsync(publishRes)}");
        var publishBody = await publishRes.Content.ReadFromJsonAsync<JsonElement>();
        var postId = publishBody.GetProperty("id").GetString();

        return new PublishResult(true, postId, null);
    }
}
