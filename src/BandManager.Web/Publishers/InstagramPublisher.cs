using System.Text.Json;
using BandManager.Data.Services;

namespace BandManager.Web.Publishers;

/// <summary>
/// Instagram Graph API - feed post. Supports both of Meta's connection
/// models independently (Setup lets a Band pick per-connection, see
/// PlatformSeedData.InstagramInstructions): "linked" reuses the Facebook
/// Page's own saved Page Access Token at publish time rather than storing
/// a redundant copy ("API with Facebook Login" - the two can never drift
/// out of sync this way); "standalone" stores its own token from Meta's
/// separate "API with Instagram Login" flow, which needs no Facebook Page
/// at all and calls a different API host (graph.instagram.com, not
/// graph.facebook.com). Ported from the old app's src/publishers/instagram.js,
/// which only ever supported the "linked" model.
///
/// Needs a credential stored under platform "instagram":
/// { mode: "linked", igUserId: "..." } (Facebook's own credential supplies
/// the token), or { mode: "standalone", igUserId: "...", igAccessToken: "..." }.
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
    private const string FacebookGraphApi = "https://graph.facebook.com/v19.0";
    private const string InstagramGraphApi = "https://graph.instagram.com/v19.0";

    private async Task<(string IgUserId, string Token, string ApiBase)> RequireCredsAsync(Guid bandId)
    {
        var creds = await credentialStore.GetCredentialAsync(bandId, "instagram");
        if (creds is null || !creds.TryGetValue("igUserId", out var igUserId) || string.IsNullOrEmpty(igUserId))
            throw new InvalidOperationException("Instagram is not configured yet - add an Instagram account ID in Settings.");

        // Absent "mode" means a credential saved before this field existed
        // - those were always the Facebook-linked model, so that's the
        // correct default for backward compatibility, not just a guess.
        var mode = creds.GetValueOrDefault("mode", "linked");
        if (mode == "standalone")
        {
            if (!creds.TryGetValue("igAccessToken", out var igToken) || string.IsNullOrEmpty(igToken))
                throw new InvalidOperationException("Instagram is not fully configured - add an access token in Settings.");
            return (igUserId, igToken, InstagramGraphApi);
        }

        var fbCreds = await credentialStore.GetCredentialAsync(bandId, "facebook");
        if (fbCreds is null || !fbCreds.TryGetValue("pageAccessToken", out var pageToken) || string.IsNullOrEmpty(pageToken))
            throw new InvalidOperationException("Instagram is set to use the Facebook connection, but Facebook isn't connected yet - connect Facebook first, or switch Instagram to an independent connection in Settings.");
        return (igUserId, pageToken, FacebookGraphApi);
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
        var (igUserId, token, apiBase) = await RequireCredsAsync(bandId);

        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new InvalidOperationException("Instagram posts need a publicly reachable image URL (publish the flyer to the site first).");

        using var createForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["image_url"] = imageUrl,
            ["caption"] = caption ?? "",
            ["access_token"] = token
        });
        using var createRes = await http.PostAsync($"{apiBase}/{igUserId}/media", createForm);
        if (!createRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram media create failed: {await ExtractErrorAsync(createRes)}");
        var createBody = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var creationId = createBody.GetProperty("id").GetString();

        using var publishForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["creation_id"] = creationId!,
            ["access_token"] = token
        });
        using var publishRes = await http.PostAsync($"{apiBase}/{igUserId}/media_publish", publishForm);
        if (!publishRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram publish failed: {await ExtractErrorAsync(publishRes)}");
        var publishBody = await publishRes.Content.ReadFromJsonAsync<JsonElement>();
        var postId = publishBody.GetProperty("id").GetString();

        return new PublishResult(true, postId, null);
    }

    /// <summary>A real multi-photo Instagram post (a "Carousel", Instagram's
    /// own term for what the Cadence board calls "Photo Album" for
    /// naming consistency with Facebook's equivalent) - up to 10 images.
    /// Same three-step Graph API shape as a single post, just with an
    /// extra layer: each image first becomes its own unpublished
    /// "carousel item" container, then those get bundled into one
    /// CAROUSEL container, which is what actually gets published. Same
    /// publicly-reachable-URL requirement as PublishAsync above -
    /// Instagram's API only ever fetches images by URL, it doesn't accept
    /// a raw upload (see this class's doc comment for what that requires
    /// of this app's own hosting).</summary>
    public async Task<PublishResult> PublishCarouselAsync(Guid bandId, string? caption, List<string> imageUrls)
    {
        if (imageUrls.Count == 0) throw new InvalidOperationException("No photos to post.");
        if (imageUrls.Count > 10) throw new InvalidOperationException("Instagram carousels can only have up to 10 photos.");
        var (igUserId, token, apiBase) = await RequireCredsAsync(bandId);

        var childIds = new List<string>();
        foreach (var imageUrl in imageUrls)
        {
            using var childForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["image_url"] = imageUrl,
                ["is_carousel_item"] = "true",
                ["access_token"] = token
            });
            using var childRes = await http.PostAsync($"{apiBase}/{igUserId}/media", childForm);
            if (!childRes.IsSuccessStatusCode)
                throw new InvalidOperationException($"Instagram carousel item {childIds.Count + 1} of {imageUrls.Count} failed: {await ExtractErrorAsync(childRes)}");
            var childBody = await childRes.Content.ReadFromJsonAsync<JsonElement>();
            childIds.Add(childBody.GetProperty("id").GetString()!);
        }

        using var carouselForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["media_type"] = "CAROUSEL",
            ["children"] = string.Join(",", childIds),
            ["caption"] = caption ?? "",
            ["access_token"] = token
        });
        using var carouselRes = await http.PostAsync($"{apiBase}/{igUserId}/media", carouselForm);
        if (!carouselRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram carousel create failed: {await ExtractErrorAsync(carouselRes)}");
        var carouselBody = await carouselRes.Content.ReadFromJsonAsync<JsonElement>();
        var creationId = carouselBody.GetProperty("id").GetString();

        using var publishForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["creation_id"] = creationId!,
            ["access_token"] = token
        });
        using var publishRes = await http.PostAsync($"{apiBase}/{igUserId}/media_publish", publishForm);
        if (!publishRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram carousel publish failed: {await ExtractErrorAsync(publishRes)}");
        var publishBody = await publishRes.Content.ReadFromJsonAsync<JsonElement>();
        var postId = publishBody.GetProperty("id").GetString();

        return new PublishResult(true, postId, null);
    }
}
