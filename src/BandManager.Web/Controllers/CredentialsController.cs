using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// Band-scoped credential CRUD - BandAdmin only, matches the old app's
/// routes/settings.js /api/settings/credentials* shape closely (same
/// snake_case-free-JSON field names, since these were already camelCase
/// in the old app) so wwwroot/assets/settings.js needs minimal change.
/// Always scoped to the current active Band via IActiveBandAccessor -
/// never a client-supplied band id, so a BandAdmin of Band A can never
/// touch Band B's credentials even by editing a request.
/// </summary>
[ApiController]
[Route("/api/settings/credentials")]
[Authorize(Policy = "BandAdmin")]
public class CredentialsController(
    ApplicationDbContext db,
    CredentialStore credentialStore,
    IActiveBandAccessor activeBand,
    GitHubSiteClient gitHub,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(14);

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null)
        {
            bandId = default;
            return BadRequest(new { error = "No active band selected." });
        }
        bandId = id.Value;
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> Status()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var platforms = await db.Platforms.Where(p => p.CredentialFields != null).ToListAsync();
        var accounts = await db.Accounts.Where(a => a.BandId == bandId).ToListAsync();
        var byPlatform = accounts.ToDictionary(a => a.PlatformId);

        var now = DateTime.UtcNow;
        var result = new Dictionary<string, object>();
        foreach (var p in platforms)
        {
            byPlatform.TryGetValue(p.Id, out var account);
            var expiresAt = account?.TokenExpiresAt;
            var expiresAtUtc = expiresAt?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var expired = expiresAtUtc is not null && expiresAtUtc < now;
            var expiringSoon = expiresAtUtc is not null && expiresAtUtc >= now && (expiresAtUtc.Value - now) <= ExpiringSoonWindow;

            result[p.Id] = new
            {
                configured = account?.EncryptedCredentials is not null,
                label = account?.Label != p.DisplayName ? account?.Label : null,
                verified = account?.LastVerifiedOk,
                verification_error = account?.LastVerificationError,
                tokenExpiresAt = expiresAt?.ToString("yyyy-MM-dd"),
                expired,
                expiringSoon
            };
        }
        return Ok(result);
    }

    /// <summary>
    /// "Copy from another band" - lists every OTHER band this user is
    /// personally a BandAdmin of (a real membership row, not just a
    /// SuperAdmin bypass - the point is "credentials you set up
    /// yourself elsewhere") that has this platform actually configured,
    /// with the decrypted values ready to prefill the form. Not a shared/
    /// linked row: picking one here just prefills the client-side form -
    /// saving still writes a brand new, independent Account row under the
    /// active Band (same copy-not-reference principle as the Catalog).
    /// </summary>
    [HttpGet("{platform}/reusable")]
    public async Task<IActionResult> Reusable(string platform)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var otherBandIds = await db.BandMemberships
            .Where(m => m.UserId == userId && m.Role == Data.Entities.BandRole.BandAdmin && m.BandId != bandId)
            .Select(m => m.BandId)
            .ToListAsync();
        if (otherBandIds.Count == 0) return Ok(Array.Empty<object>());

        var candidates = await db.Accounts
            .Include(a => a.Band)
            .Where(a => otherBandIds.Contains(a.BandId) && a.PlatformId == platform && a.EncryptedCredentials != null)
            .ToListAsync();

        var result = new List<object>();
        foreach (var account in candidates)
        {
            var values = await credentialStore.GetCredentialAsync(account.BandId, platform);
            if (values is null) continue;
            result.Add(new { bandId = account.BandId, bandName = account.Band.Name, values });
        }
        return Ok(result);
    }

    [HttpGet("values")]
    public async Task<IActionResult> Values()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var platformIds = await db.Platforms.Where(p => p.CredentialFields != null).Select(p => p.Id).ToListAsync();
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var id in platformIds)
        {
            var value = await credentialStore.GetCredentialAsync(bandId, id);
            if (value is not null) result[id] = value;
        }

        // siteBaseUrl/githubOwner/githubRepo live on Band itself, not in
        // the encrypted credential store (see Save's "website" branch) -
        // merged in here so the Setup form can prefill them same as any
        // other saved field.
        if (platformIds.Contains("website"))
        {
            var band = await db.Bands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bandId);
            if (band is not null && (band.SiteBaseUrl is not null || band.GitHubOwner is not null || band.GitHubRepo is not null))
            {
                result.TryGetValue("website", out var existing);
                var merged = existing ?? new Dictionary<string, string>();
                if (band.SiteBaseUrl is not null) merged["siteBaseUrl"] = band.SiteBaseUrl;
                if (band.GitHubOwner is not null) merged["githubOwner"] = band.GitHubOwner;
                if (band.GitHubRepo is not null) merged["githubRepo"] = band.GitHubRepo;
                result["website"] = merged;
            }
        }
        return Ok(result);
    }

    [HttpPost("{platform}")]
    public async Task<IActionResult> Save(string platform, [FromBody] Dictionary<string, JsonElement> body)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var platformRow = await db.Platforms.FindAsync(platform);
        if (platformRow?.CredentialFields is null) return NotFound(new { error = "Unknown platform" });

        // Schema-driven: each field's Required/VisibleWhen/ForbidQuotes
        // comes from PlatformSeedData instead of being special-cased here
        // per platform (this used to hand-roll Instagram's mode/
        // igAccessToken logic inline - see CredentialField's doc comment).
        // VisibleWhen is evaluated against fieldValues built so far in this
        // same pass, so a field's dependency must be listed before it.
        var fieldValues = new Dictionary<string, string>();
        foreach (var field in platformRow.CredentialFields)
        {
            var isVisible = field.VisibleWhen is null
                || (fieldValues.TryGetValue(field.VisibleWhen.Field, out var dependency) && dependency == field.VisibleWhen.Value);
            if (!isVisible) continue;

            body.TryGetValue(field.Name, out var raw);
            var str = raw.ValueKind == JsonValueKind.String ? raw.GetString()?.Trim() : null;

            if (string.IsNullOrEmpty(str))
            {
                if (field.Required) return BadRequest(new { error = $"Missing field: {field.Name}" });
                continue;
            }
            if (str.Length > 2000) return BadRequest(new { error = $"{field.Name} is too long" });
            if (field.ForbidQuotes && str.Contains('"'))
                return BadRequest(new { error = $"{field.Name} can't contain a quote character" });
            if (field.Type == CredentialFieldType.Radio && field.Options is not null && !field.Options.Any(o => o.Value == str))
                return BadRequest(new { error = $"Invalid value for {field.Name}." });

            fieldValues[field.Name] = str;
        }

        var value = fieldValues.Where(kv => platformRow.CredentialFields.First(f => f.Name == kv.Key).StoreAsCredential)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Site base URL + GitHub owner/repo - per-band Website config that
        // GigsSource/MediaSource/GallerySource (site content) and the
        // GitHub push itself need, but which isn't part of the encrypted
        // credential store since it's not a secret. Optional: leaving a
        // field blank clears it rather than rejecting the save, so
        // githubToken alone can still be saved before the site details
        // are known.
        Band? band = null;
        if (platform == "website")
        {
            band = await db.Bands.FindAsync(bandId);
            if (band is not null)
            {
                band.SiteBaseUrl = fieldValues.GetValueOrDefault("siteBaseUrl");
                band.GitHubOwner = fieldValues.GetValueOrDefault("githubOwner");
                band.GitHubRepo = fieldValues.GetValueOrDefault("githubRepo");
                await db.SaveChangesAsync();
            }
        }

        await credentialStore.SetCredentialAsync(bandId, platform, value);

        // Optional, not part of credential_fields - a blank string clears
        // it, same as leaving the field empty means "I don't know."
        if (body.TryGetValue("tokenExpiresAt", out var expiresRaw) && expiresRaw.ValueKind == JsonValueKind.String)
        {
            var raw = expiresRaw.GetString()?.Trim() ?? "";
            if (raw.Length == 0)
            {
                await credentialStore.SetTokenExpiryAsync(bandId, platform, null);
            }
            else if (DateOnly.TryParse(raw, out var expires))
            {
                await credentialStore.SetTokenExpiryAsync(bandId, platform, expires);
            }
            else
            {
                return BadRequest(new { error = "tokenExpiresAt is not a valid date" });
            }
        }

        // Website's sanity check (unlike Facebook's, which needs a real
        // post+delete and isn't ported): two cheap, safe, read-only calls -
        // can the site itself be reached, and does GitHub actually accept
        // this token for this exact repo, with write access. Every other
        // platform still just saves, same as before, until each gets a
        // real equivalent.
        object? verification = null;
        if (platform == "website" && band is not null)
        {
            verification = await VerifyWebsiteConnectionAsync(band, value.GetValueOrDefault("githubToken"));
        }

        return Ok(new { ok = true, verification });
    }

    private async Task<object> VerifyWebsiteConnectionAsync(Band band, string? githubToken)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(band.SiteBaseUrl))
        {
            issues.Add("Site base URL isn't set.");
        }
        else
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var res = await client.GetAsync(band.SiteBaseUrl.TrimEnd('/') + "/js/calendar.js");
                if (!res.IsSuccessStatusCode)
                    issues.Add($"Could not fetch js/calendar.js from the site (HTTP {(int)res.StatusCode}) - check the Site base URL.");
            }
            catch (Exception ex)
            {
                issues.Add($"Could not reach the site: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(band.GitHubOwner) || string.IsNullOrWhiteSpace(band.GitHubRepo))
        {
            issues.Add("GitHub owner/repo isn't set.");
        }
        else if (string.IsNullOrWhiteSpace(githubToken))
        {
            issues.Add("No GitHub token on record.");
        }
        else
        {
            // CheckRepoAccessAsync alone isn't enough: its "can push" signal
            // comes from the repo-info endpoint's `permissions` object,
            // which reflects the *account's* collaborator role - not
            // whether this specific fine-grained token was actually
            // granted Contents access. A token can pass that check and
            // still 403 on the real Contents API calls this app makes
            // (discovered live: a real save reported healthy here, then
            // the next real edit 403'd on GET .../contents/js/calendar.js).
            // So this also exercises the literal call GigsSiteEditor makes.
            var access = await gitHub.CheckRepoAccessAsync(band);
            if (!access.Ok || !access.CanPush)
            {
                issues.Add(access.Error ?? "GitHub rejected the request.");
            }
            else
            {
                try
                {
                    await gitHub.GetFileAsync(band, "js/calendar.js");
                }
                catch (InvalidOperationException ex)
                {
                    issues.Add($"The repo check passed, but reading js/calendar.js failed: {ex.Message} This usually means the token's fine-grained permissions don't include Contents: Read and write for this repo.");
                }
            }
        }

        return new { ok = issues.Count == 0, error = issues.Count > 0 ? string.Join(" ", issues) : null };
    }

    [HttpDelete("{platform}")]
    public async Task<IActionResult> Delete(string platform)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (await db.Platforms.FindAsync(platform) is not { CredentialFields: not null })
            return NotFound(new { error = "Unknown platform" });

        await credentialStore.ClearCredentialAsync(bandId, platform);
        return Ok(new { ok = true });
    }
}
