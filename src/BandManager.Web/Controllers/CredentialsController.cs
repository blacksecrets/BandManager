using System.Text.Json;
using BandManager.Data;
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
public class CredentialsController(ApplicationDbContext db, CredentialStore credentialStore, IActiveBandAccessor activeBand) : ControllerBase
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
        return Ok(result);
    }

    [HttpPost("{platform}")]
    public async Task<IActionResult> Save(string platform, [FromBody] Dictionary<string, JsonElement> body)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var platformRow = await db.Platforms.FindAsync(platform);
        if (platformRow?.CredentialFields is null) return NotFound(new { error = "Unknown platform" });

        var value = new Dictionary<string, string>();
        foreach (var field in platformRow.CredentialFields)
        {
            if (!body.TryGetValue(field, out var raw) || raw.ValueKind != JsonValueKind.String)
                return BadRequest(new { error = $"Missing field: {field}" });
            var str = raw.GetString()?.Trim();
            if (string.IsNullOrEmpty(str)) return BadRequest(new { error = $"Missing field: {field}" });
            if (str.Length > 2000) return BadRequest(new { error = $"{field} is too long" });
            if (platform == "bandsintown" && str.Contains('"'))
                return BadRequest(new { error = $"{field} can't contain a quote character" });
            value[field] = str;
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

        // Facebook's real-connection verification (posts+deletes a test
        // post) needs the Facebook publisher, not yet ported - so saving
        // Facebook credentials here just saves them, same as every other
        // platform, until that lands.
        return Ok(new { ok = true });
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
