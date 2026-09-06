using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BandManager.Data.Entities;

namespace BandManager.Data.Services;

public record SiteFile(string Content, string Sha);

/// <summary>
/// Thin wrapper around GitHub's Contents API for pushing changes to a
/// Band's public site repo - ported from the old app's src/github.js.
/// OWNER/REPO were global constants there (single-tenant); here they're
/// Band.GitHubOwner/GitHubRepo, and the token comes from this Band's own
/// "website" platform credential (CredentialStore), so every Band pushes
/// to its own repo with its own token. No local git checkout anywhere -
/// works from any machine with just a token and internet access, same
/// principle GigsSource/MediaSource/GallerySource already use for reading.
/// </summary>
public class GitHubSiteClient(HttpClient http, CredentialStore credentialStore)
{
    private async Task<(string Token, string ApiBase)> RequireCredsAsync(Band band)
    {
        if (string.IsNullOrWhiteSpace(band.GitHubOwner) || string.IsNullOrWhiteSpace(band.GitHubRepo))
            throw new InvalidOperationException("This band's GitHub owner/repo isn't set yet - add them under Configure Web Presence's Website section.");

        var creds = await credentialStore.GetCredentialAsync(band.Id, "website");
        if (creds is null || !creds.TryGetValue("githubToken", out var token) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The website isn't connected yet - add a GitHub token under Configure Web Presence's Website section.");

        return (token, $"https://api.github.com/repos/{band.GitHubOwner}/{band.GitHubRepo}");
    }

    private async Task<JsonElement> GitHubRequestAsync(Band band, HttpMethod method, string path, object? body = null)
    {
        var (token, apiBase) = await RequireCredsAsync(band);
        var request = new HttpRequestMessage(method, $"{apiBase}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        // GitHub's API rejects any request with no User-Agent (403 "Request
        // forbidden by administrative rules"), regardless of auth validity.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BandManager", "1.0"));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        var res = await http.SendAsync(request);
        var text = await res.Content.ReadAsStringAsync();
        JsonDocument? doc = null;
        try { doc = string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text); } catch { /* non-JSON error body */ }

        if (!res.IsSuccessStatusCode)
        {
            var message = doc?.RootElement.TryGetProperty("message", out var m) == true ? m.GetString() : null;
            var docUrl = doc?.RootElement.TryGetProperty("documentation_url", out var d) == true ? d.GetString() : null;

            string detail;
            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden
                && res.Headers.TryGetValues("x-ratelimit-remaining", out var remaining)
                && remaining.FirstOrDefault() == "0")
            {
                detail = "GitHub rate-limited this token (0 requests remaining) - wait a few minutes and try again.";
            }
            else if (!string.IsNullOrWhiteSpace(message))
            {
                detail = message;
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                // No "message" field GitHub normally sends - surface the raw
                // body rather than a bare status code, so a 403 with an
                // unexpected shape (proxy/WAF page, SSO-enforcement HTML,
                // etc.) is still diagnosable from the error alone.
                detail = $"GitHub returned {(int)res.StatusCode} {res.ReasonPhrase}: {text.Trim()}";
            }
            else
            {
                detail = $"GitHub returned {(int)res.StatusCode} {res.ReasonPhrase} with no response body.";
            }
            if (!string.IsNullOrWhiteSpace(docUrl)) detail += $" ({docUrl})";

            throw new InvalidOperationException(detail);
        }
        return doc?.RootElement ?? default;
    }

    /// <summary>Reads a file's current content + the sha GitHub needs to
    /// update it (required by the Contents API to prevent silently
    /// clobbering a concurrent edit). Returns null if the file doesn't
    /// exist yet.</summary>
    public async Task<SiteFile?> GetFileAsync(Band band, string path)
    {
        try
        {
            var body = await GitHubRequestAsync(band, HttpMethod.Get, $"/contents/{path}");
            var contentB64 = body.GetProperty("content").GetString() ?? "";
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(contentB64.Replace("\n", "")));
            return new SiteFile(content, body.GetProperty("sha").GetString()!);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    /// <summary>Just the sha of a file, without decoding its content -
    /// needed before overwriting a binary file. Returns null if the file
    /// doesn't exist yet.</summary>
    public async Task<string?> GetFileShaAsync(Band band, string path)
    {
        try
        {
            var body = await GitHubRequestAsync(band, HttpMethod.Get, $"/contents/{path}");
            return body.GetProperty("sha").GetString();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    /// <summary>Writes (creates or updates) a text file in one commit.</summary>
    public Task PutTextFileAsync(Band band, string path, string content, string message, string? sha = null) =>
        GitHubRequestAsync(band, HttpMethod.Put, $"/contents/{path}", new
        {
            message,
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            sha
        });

    /// <summary>Writes (creates or updates) a binary file (e.g. a flyer image).</summary>
    public Task PutBinaryFileAsync(Band band, string path, byte[] bytes, string message, string? sha = null) =>
        GitHubRequestAsync(band, HttpMethod.Put, $"/contents/{path}", new
        {
            message,
            content = Convert.ToBase64String(bytes),
            sha
        });

    /// <summary>Sanity-checks the Band's saved owner/repo/token in one
    /// call: the repo-info endpoint both proves the token can authenticate
    /// against that exact repo (Ok) and, when authenticated, includes a
    /// permissions object that says whether it can actually push (CanPush)
    /// - covering everything this app's own writes need without making a
    /// real write to check it.</summary>
    public async Task<RepoAccessCheck> CheckRepoAccessAsync(Band band)
    {
        try
        {
            var body = await GitHubRequestAsync(band, HttpMethod.Get, "");
            var canPush = body.TryGetProperty("permissions", out var perms) && perms.TryGetProperty("push", out var push) && push.GetBoolean();
            return new RepoAccessCheck(true, canPush, canPush ? null : "The token can read this repo but doesn't have write (push) access - edits won't be able to go live.");
        }
        catch (InvalidOperationException ex)
        {
            return new RepoAccessCheck(false, false, ex.Message);
        }
    }
}

public record RepoAccessCheck(bool Ok, bool CanPush, string? Error);
