using System.Net.Http.Headers;
using System.Text.Json;
using BandManager.Data.Entities;
using BandManager.Data.Services;

namespace BandManager.Web.Publishers;

public record PublishResult(bool Ok, string? PostId, string? Url);

/// <summary>
/// Facebook Graph API - Page feed post / photo post / video post. Ported
/// from the old app's src/publishers/facebook.js (publish() only so far -
/// setCoverPhoto/verifyConnection/autoSetupFromToken are Setup-page
/// conveniences, not needed for "Fart it out" to actually work, so they're
/// deferred). Needs a credential stored under platform "facebook":
/// { pageId: "...", pageAccessToken: "..." } for the active Band.
/// </summary>
public class FacebookPublisher(HttpClient http, CredentialStore credentialStore, IWebHostEnvironment env)
{
    private const string GraphApi = "https://graph.facebook.com/v19.0";

    private async Task<Dictionary<string, string>> RequireCredsAsync(Guid bandId)
    {
        var creds = await credentialStore.GetCredentialAsync(bandId, "facebook");
        if (creds is null || !creds.TryGetValue("pageId", out var pageId) || string.IsNullOrEmpty(pageId)
            || !creds.TryGetValue("pageAccessToken", out var token) || string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Facebook is not configured yet - add a Page ID and Page access token in Settings.");
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

    public async Task<PublishResult> PublishAsync(Guid bandId, ScheduleItem item, List<Artifact> artifacts, string? caption, string? gigTitle)
    {
        var creds = await RequireCredsAsync(bandId);
        var pageId = creds["pageId"];
        var token = creds["pageAccessToken"];

        // caption is resolved by the caller from the item's own artifacts -
        // a plain /feed post never attaches a photo or video on its own,
        // only text; content types that require an image/video artifact
        // need the matching upload endpoint below instead, or the media
        // just silently never goes out (the exact bug this replaced in
        // the old app: reported success with only the caption, no image).
        var message = caption ?? gigTitle;
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("No caption text to post - add one to this item first.");

        var photo = artifacts.FirstOrDefault(a => (a.ArtifactType == "photo" || a.ArtifactType == "flyer") && a.FilePath is not null);
        var video = artifacts.FirstOrDefault(a => a.ArtifactType == "video" && a.FilePath is not null);

        if (photo is not null)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(env.ContentRootPath, photo.FilePath!));
            using var form = new MultipartFormDataContent
            {
                { new StringContent(message), "message" },
                { new StringContent(token), "access_token" }
            };
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(fileContent, "source", "photo.jpg");

            using var res = await http.PostAsync($"{GraphApi}/{pageId}/photos", form);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Facebook photo post failed: {await ExtractErrorAsync(res)}");
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            var postId = body.TryGetProperty("post_id", out var pid) ? pid.GetString() : body.GetProperty("id").GetString();
            return new PublishResult(true, postId, $"https://www.facebook.com/{postId}");
        }

        if (video is not null)
        {
            // Single-request upload, same shape as the photo case above -
            // fine for short clips (this app's own upload cap is small),
            // but Facebook's Resumable Upload API would be needed for
            // anything large enough to risk timing out on one request.
            var bytes = await File.ReadAllBytesAsync(Path.Combine(env.ContentRootPath, video.FilePath!));
            using var form = new MultipartFormDataContent
            {
                { new StringContent(message), "description" },
                { new StringContent(token), "access_token" }
            };
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            form.Add(fileContent, "source", "video.mp4");

            using var res = await http.PostAsync($"{GraphApi}/{pageId}/videos", form);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Facebook video post failed: {await ExtractErrorAsync(res)}");
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            var id = body.GetProperty("id").GetString();
            return new PublishResult(true, id, $"https://www.facebook.com/{id}");
        }

        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = message, ["access_token"] = token });
            using var res = await http.PostAsync($"{GraphApi}/{pageId}/feed", form);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Facebook post failed: {await ExtractErrorAsync(res)}");
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            var id = body.GetProperty("id").GetString();
            return new PublishResult(true, id, $"https://www.facebook.com/{id}");
        }
    }

    /// <summary>Uploads an already-formatted (820x312) cover image and
    /// sets it as the Page's cover photo. Two Graph API calls: upload the
    /// photo unpublished, then point the Page's `cover` at that photo's
    /// id. Ported from the old app's setCoverPhoto.</summary>
    public async Task<CoverPhotoResult> SetCoverPhotoAsync(Guid bandId, byte[] imageBytes)
    {
        var creds = await RequireCredsAsync(bandId);
        var pageId = creds["pageId"];
        var token = creds["pageAccessToken"];

        using var uploadForm = new MultipartFormDataContent
        {
            { new StringContent("false"), "published" },
            { new StringContent(token), "access_token" }
        };
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        uploadForm.Add(fileContent, "source", "cover.jpg");

        using var uploadRes = await http.PostAsync($"{GraphApi}/{pageId}/photos", uploadForm);
        if (!uploadRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Facebook cover photo upload failed: {await ExtractErrorAsync(uploadRes)}");
        var uploadBody = await uploadRes.Content.ReadFromJsonAsync<JsonElement>();
        var photoId = uploadBody.GetProperty("id").GetString()!;

        using var coverForm = new FormUrlEncodedContent(new Dictionary<string, string> { ["cover"] = photoId, ["access_token"] = token });
        using var coverRes = await http.PostAsync($"{GraphApi}/{pageId}", coverForm);
        if (!coverRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Setting Facebook cover photo failed: {await ExtractErrorAsync(coverRes)}");

        return new CoverPhotoResult(true, photoId);
    }
}

public record CoverPhotoResult(bool Ok, string PhotoId);
