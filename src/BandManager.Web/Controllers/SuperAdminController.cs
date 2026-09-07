using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record CreateBandRequest(string Name, string Slug, string? ExistingAdminUsername, string? NewAdminUsername);
public record BandMembershipInput(Guid BandId, string Role);
public record CreateUserRequest(string Username, bool IsSuperAdmin, List<BandMembershipInput>? Memberships);
public record SetSuperAdminRequest(bool IsSuperAdmin);
public record SetBandRoleRequest(string Role);
public record AddBandMembershipRequest(Guid BandId, string Role);
public record SetYouTubeCredentialsRequest(string ApiKey);
public record SetSpotifyCredentialsRequest(string ClientId, string ClientSecret);
public record SetCalendarOAuthCredentialsRequest(string ClientId, string ClientSecret);
public record SetUspsCredentialsRequest(string ClientId, string ClientSecret);

/// <summary>
/// The SuperAdmin config screen's backend - band onboarding/archiving, and
/// full user management: SuperAdmin can add, change (level and band
/// memberships), and delete any user at any level. The per-band "Manage
/// this band's users" on the Band Admin page is a separate, narrower
/// surface for a BandAdmin managing only their own band - this one is the
/// global view.
/// </summary>
[ApiController]
[Route("/api/superadmin")]
[Authorize(Policy = "SuperAdmin")]
public class SuperAdminController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    UserProvisioningService userProvisioning,
    BandMembershipService membershipService,
    ICredentialCipher cipher) : ControllerBase
{
    private const string YouTubeCredentialsKey = "youtube_credentials";
    private const string SpotifyCredentialsKey = "spotify_credentials";

    // Platform-wide song search credentials (YouTube Data API key,
    // Spotify Client ID/Secret) - shared by every Band's repertoire
    // search the same way the Song catalog itself is shared, so this is
    // SuperAdmin-managed rather than living on each Band's own Setup
    // page. Encrypted at rest via the same cipher a Band's own platform
    // credentials use, just stored in PlatformSettings instead of an
    // Account row. Values are returned decrypted (not just a configured
    // flag) so the form can prefill for editing - same convention
    // CredentialsController.Values already uses for Band credentials.
    [HttpGet("song-search-credentials")]
    public async Task<IActionResult> GetSongSearchCredentials()
    {
        var youTube = await SongSearchService.GetCredentialAsync(db, cipher, YouTubeCredentialsKey);
        var spotify = await SongSearchService.GetCredentialAsync(db, cipher, SpotifyCredentialsKey);
        return Ok(new { youTube, spotify });
    }

    [HttpPut("song-search-credentials/youtube")]
    public async Task<IActionResult> SetYouTubeCredentials([FromBody] SetYouTubeCredentialsRequest request)
    {
        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey)) return BadRequest(new { error = "API key is required." });
        await SongSearchService.SetCredentialAsync(db, cipher, YouTubeCredentialsKey, new() { ["apiKey"] = apiKey });
        return Ok(new { ok = true });
    }

    [HttpDelete("song-search-credentials/youtube")]
    public async Task<IActionResult> ClearYouTubeCredentials()
    {
        await SongSearchService.ClearCredentialAsync(db, YouTubeCredentialsKey);
        return Ok(new { ok = true });
    }

    [HttpPut("song-search-credentials/spotify")]
    public async Task<IActionResult> SetSpotifyCredentials([FromBody] SetSpotifyCredentialsRequest request)
    {
        var clientId = request.ClientId?.Trim();
        var clientSecret = request.ClientSecret?.Trim();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequest(new { error = "Client ID and Client Secret are both required." });
        await SongSearchService.SetCredentialAsync(db, cipher, SpotifyCredentialsKey,
            new() { ["clientId"] = clientId, ["clientSecret"] = clientSecret });
        return Ok(new { ok = true });
    }

    [HttpDelete("song-search-credentials/spotify")]
    public async Task<IActionResult> ClearSpotifyCredentials()
    {
        await SongSearchService.ClearCredentialAsync(db, SpotifyCredentialsKey);
        return Ok(new { ok = true });
    }

    // --- External calendar sync OAuth apps (Google Calendar / Outlook) ---
    // One registered OAuth app per provider for this whole BandManager
    // instance (the redirect URI is fixed to this instance's own domain),
    // not per-Band - same "global, SuperAdmin-managed, PlatformSettings-
    // backed" shape as the song-search credentials above. Each band
    // member then connects their own calendar under this app via
    // ExternalCalendarController - see UserExternalCalendarConnection.
    private const string GoogleCalendarOAuthKey = "google_calendar_oauth_credentials";
    private const string OutlookOAuthKey = "outlook_oauth_credentials";

    [HttpGet("calendar-oauth-credentials")]
    public async Task<IActionResult> GetCalendarOAuthCredentials()
    {
        var google = await SongSearchService.GetCredentialAsync(db, cipher, GoogleCalendarOAuthKey);
        var outlook = await SongSearchService.GetCredentialAsync(db, cipher, OutlookOAuthKey);
        return Ok(new { google, outlook });
    }

    [HttpPut("calendar-oauth-credentials/google")]
    public async Task<IActionResult> SetGoogleCalendarOAuthCredentials([FromBody] SetCalendarOAuthCredentialsRequest request)
    {
        var clientId = request.ClientId?.Trim();
        var clientSecret = request.ClientSecret?.Trim();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequest(new { error = "Client ID and Client Secret are both required." });
        await SongSearchService.SetCredentialAsync(db, cipher, GoogleCalendarOAuthKey,
            new() { ["clientId"] = clientId, ["clientSecret"] = clientSecret });
        return Ok(new { ok = true });
    }

    [HttpDelete("calendar-oauth-credentials/google")]
    public async Task<IActionResult> ClearGoogleCalendarOAuthCredentials()
    {
        await SongSearchService.ClearCredentialAsync(db, GoogleCalendarOAuthKey);
        return Ok(new { ok = true });
    }

    [HttpPut("calendar-oauth-credentials/outlook")]
    public async Task<IActionResult> SetOutlookOAuthCredentials([FromBody] SetCalendarOAuthCredentialsRequest request)
    {
        var clientId = request.ClientId?.Trim();
        var clientSecret = request.ClientSecret?.Trim();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequest(new { error = "Client ID and Client Secret are both required." });
        await SongSearchService.SetCredentialAsync(db, cipher, OutlookOAuthKey,
            new() { ["clientId"] = clientId, ["clientSecret"] = clientSecret });
        return Ok(new { ok = true });
    }

    [HttpDelete("calendar-oauth-credentials/outlook")]
    public async Task<IActionResult> ClearOutlookOAuthCredentials()
    {
        await SongSearchService.ClearCredentialAsync(db, OutlookOAuthKey);
        return Ok(new { ok = true });
    }

    // --- USPS address validation (Profile page's address fields) ---
    // Same global/SuperAdmin-managed/PlatformSettings-backed shape as
    // everything else in this section - see AddressLookupService.

    [HttpGet("usps-credentials")]
    public async Task<IActionResult> GetUspsCredentials()
    {
        var creds = await SongSearchService.GetCredentialAsync(db, cipher, AddressLookupService.CredentialKey);
        return Ok(creds);
    }

    [HttpPut("usps-credentials")]
    public async Task<IActionResult> SetUspsCredentials([FromBody] SetUspsCredentialsRequest request)
    {
        var clientId = request.ClientId?.Trim();
        var clientSecret = request.ClientSecret?.Trim();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequest(new { error = "Consumer Key and Consumer Secret are both required." });
        await SongSearchService.SetCredentialAsync(db, cipher, AddressLookupService.CredentialKey,
            new() { ["clientId"] = clientId, ["clientSecret"] = clientSecret });
        return Ok(new { ok = true });
    }

    [HttpDelete("usps-credentials")]
    public async Task<IActionResult> ClearUspsCredentials()
    {
        await SongSearchService.ClearCredentialAsync(db, AddressLookupService.CredentialKey);
        return Ok(new { ok = true });
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var currentUserId = User.GetUserId();
        var users = await db.Users.AsNoTracking()
            .Include(u => u.BandMemberships).ThenInclude(m => m.Band)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var result = users.Select(u => new
        {
            id = u.Id,
            username = u.UserName,
            isSuperAdmin = u.IsSuperAdmin,
            emailConfirmed = u.EmailConfirmed,
            isSelf = u.Id == currentUserId,
            memberships = u.BandMemberships
                .OrderBy(m => m.Band.Name)
                .Select(m => new { bandId = m.BandId, bandName = m.Band.Name, isArchived = m.Band.IsArchived, role = m.Role.ToString() })
        });
        return Ok(result);
    }

    // Creates a new account at whatever level is picked: a bare SuperAdmin
    // (Memberships ignored - SuperAdmin already sees/manages every band
    // without one), or a BandAdmin/User of each band listed in
    // Memberships. Mirrors ProfileController.AddUser's "attach a
    // membership to an existing login" idea, but this is account creation
    // only - adding an *existing* user to more bands is the separate
    // AddBandMembership action below, reachable from the same grid.
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var username = request.Username?.Trim() ?? "";
        if (!EmailValidation.LooksLikeEmail(username))
            return BadRequest(new { error = "Username must be a valid email address." });
        if (await userManager.FindByNameAsync(username) is not null)
            return BadRequest(new { error = "That username is already taken." });

        var memberships = request.IsSuperAdmin ? [] : (request.Memberships ?? []);
        var parsedRoles = new List<(Guid BandId, BandRole Role)>();
        foreach (var m in memberships)
        {
            if (!Enum.TryParse<BandRole>(m.Role, ignoreCase: true, out var role))
                return BadRequest(new { error = $"Invalid role '{m.Role}'." });
            if (!await db.Bands.AnyAsync(b => b.Id == m.BandId && !b.IsArchived))
                return BadRequest(new { error = "One of the selected bands doesn't exist or is archived." });
            parsedRoles.Add((m.BandId, role));
        }

        var loginUrl = $"{Request.Scheme}://{Request.Host}/login.html";
        var (created, error) = await userProvisioning.CreateAsync(username, request.IsSuperAdmin, loginUrl);
        if (!created) return BadRequest(new { error });

        var user = (await userManager.FindByNameAsync(username))!;
        foreach (var (bandId, role) in parsedRoles)
        {
            db.BandMemberships.Add(new BandMembership { UserId = user.Id, BandId = bandId, Role = role });
        }
        if (parsedRoles.Count > 0) await db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    // Promote/demote. Self-demotion is blocked, the same protection
    // RemoveSuperAdmin used to provide on its own - since it always
    // targets someone else, at least one SuperAdmin (the acting one)
    // necessarily survives every call here, so there's no separate "last
    // SuperAdmin" guard needed on top of it.
    [HttpPut("users/{userId:guid}/superadmin")]
    public async Task<IActionResult> SetSuperAdmin(Guid userId, [FromBody] SetSuperAdminRequest request)
    {
        var currentUserId = User.GetUserId();
        if (userId == currentUserId && !request.IsSuperAdmin)
            return BadRequest(new { error = "You can't remove your own SuperAdmin access." });

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { error = "Not found" });

        user.IsSuperAdmin = request.IsSuperAdmin;
        await userManager.UpdateAsync(user);
        return Ok(new { ok = true });
    }

    [HttpPost("users/{userId:guid}/bands")]
    public async Task<IActionResult> AddBandMembership(Guid userId, [FromBody] AddBandMembershipRequest request)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { error = "Not found" });
        if (!Enum.TryParse<BandRole>(request.Role, ignoreCase: true, out var role))
            return BadRequest(new { error = "Invalid role." });

        var band = await db.Bands.FindAsync(request.BandId);
        if (band is null) return BadRequest(new { error = "That band doesn't exist." });
        if (band.IsArchived) return BadRequest(new { error = "That band is archived." });
        if (await db.BandMemberships.AnyAsync(m => m.UserId == userId && m.BandId == request.BandId))
            return BadRequest(new { error = "Already a member of that band." });

        db.BandMemberships.Add(new BandMembership { UserId = userId, BandId = request.BandId, Role = role });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("users/{userId:guid}/bands/{bandId:guid}")]
    public async Task<IActionResult> SetBandRole(Guid userId, Guid bandId, [FromBody] SetBandRoleRequest request)
    {
        if (!Enum.TryParse<BandRole>(request.Role, ignoreCase: true, out var role))
            return BadRequest(new { error = "Invalid role." });

        var membership = await db.BandMemberships.FirstOrDefaultAsync(m => m.UserId == userId && m.BandId == bandId);
        if (membership is null) return NotFound(new { error = "Not found" });

        if (membership.Role == BandRole.BandAdmin && role != BandRole.BandAdmin)
        {
            try { await membershipService.EnsureNotLastAdminAsync(bandId, userId); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        membership.Role = role;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("users/{userId:guid}/bands/{bandId:guid}")]
    public async Task<IActionResult> RemoveBandMembership(Guid userId, Guid bandId)
    {
        var membership = await db.BandMemberships.FirstOrDefaultAsync(m => m.UserId == userId && m.BandId == bandId);
        if (membership is null) return NotFound(new { error = "Not found" });

        if (membership.Role == BandRole.BandAdmin)
        {
            try { await membershipService.EnsureNotLastAdminAsync(bandId, userId); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        db.BandMemberships.Remove(membership);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Full account deletion - distinct from SetSuperAdmin(false) (demote
    // only) and RemoveBandMembership (leave one band only). Blocks
    // self-delete the same way self-demotion is blocked; separately
    // blocks deleting anyone who's the last Band Admin of any band they
    // belong to (same orphan-admin guard as everywhere else), naming which
    // band(s) so the caller knows what to fix first.
    [HttpDelete("users/{userId:guid}")]
    public async Task<IActionResult> DeleteUser(Guid userId)
    {
        var currentUserId = User.GetUserId();
        if (userId == currentUserId)
            return BadRequest(new { error = "You can't delete your own account." });

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { error = "Not found" });

        var adminMemberships = await db.BandMemberships.Include(m => m.Band)
            .Where(m => m.UserId == userId && m.Role == BandRole.BandAdmin)
            .ToListAsync();
        var blockedBandNames = new List<string>();
        foreach (var m in adminMemberships)
        {
            try { await membershipService.EnsureNotLastAdminAsync(m.BandId, userId); }
            catch (InvalidOperationException) { blockedBandNames.Add(m.Band.Name); }
        }
        if (blockedBandNames.Count > 0)
        {
            return BadRequest(new
            {
                error = $"Can't delete - they're the last Band Admin of: {string.Join(", ", blockedBandNames)}. Promote someone else there first."
            });
        }

        await userManager.DeleteAsync(user);
        return Ok(new { ok = true });
    }

    // IsOnboarded excludes with-band stubs (see Band.IsOnboarded) - real
    // tenant management has nothing to do with an unclaimed identity row
    // that exists purely so a Gig's With-acts can reference a real Band.
    [HttpGet("bands")]
    public async Task<IActionResult> ListBands()
    {
        var bands = await db.Bands.Where(b => b.IsOnboarded).OrderBy(b => b.Name)
            .Select(b => new { id = b.Id, name = b.Name, slug = b.Slug, createdAt = b.CreatedAt, isArchived = b.IsArchived, archivedAt = b.ArchivedAt })
            .ToListAsync();
        return Ok(bands);
    }

    // Soft-delete only - see Band.IsArchived's doc comment. Archiving
    // doesn't touch BandMemberships at all: a BandAdmin/User of this Band
    // simply can no longer select it (BandsController excludes archived
    // Bands from both listing and SetActive, and BandAccessCheck denies a
    // stale active-band session pointing at one), while any *other* Band
    // memberships they hold are completely untouched.
    [HttpPost("bands/{bandId:guid}/archive")]
    public async Task<IActionResult> ArchiveBand(Guid bandId)
    {
        var band = await db.Bands.FindAsync(bandId);
        if (band is null) return NotFound(new { error = "Not found" });
        if (!band.IsArchived)
        {
            band.IsArchived = true;
            band.ArchivedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    [HttpPost("bands/{bandId:guid}/unarchive")]
    public async Task<IActionResult> UnarchiveBand(Guid bandId)
    {
        var band = await db.Bands.FindAsync(bandId);
        if (band is null) return NotFound(new { error = "Not found" });
        if (band.IsArchived)
        {
            band.IsArchived = false;
            band.ArchivedAt = null;
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    // Every band needs at least one Band Admin from the moment it exists -
    // otherwise it'd be created orphaned (nobody able to manage its users
    // or Setup until a SuperAdmin steps in), the exact situation
    // BandMembershipService.EnsureNotLastAdminAsync exists to prevent later
    // in a band's life. Picking one is mandatory here too: either an
    // existing user (by username) or a brand-new one, not both, not
    // neither.
    [HttpPost("bands")]
    public async Task<IActionResult> CreateBand([FromBody] CreateBandRequest request)
    {
        var name = request.Name?.Trim();
        var slug = request.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required" });
        if (string.IsNullOrEmpty(slug) || !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9-]+$"))
            return BadRequest(new { error = "Slug is required and must be lowercase letters/numbers/hyphens" });
        if (await db.Bands.AnyAsync(b => b.Slug == slug))
            return BadRequest(new { error = "That slug is already in use" });

        var existingAdminUsername = request.ExistingAdminUsername?.Trim();
        var newAdminUsername = request.NewAdminUsername?.Trim();
        var pickedExisting = !string.IsNullOrEmpty(existingAdminUsername);
        var pickedNew = !string.IsNullOrEmpty(newAdminUsername);
        if (pickedExisting == pickedNew)
            return BadRequest(new { error = "Pick an existing user or enter a new username for this band's first Band Admin - not both, not neither." });

        ApplicationUser adminUser;
        if (pickedExisting)
        {
            var found = await userManager.FindByNameAsync(existingAdminUsername!);
            if (found is null) return BadRequest(new { error = "That user doesn't exist." });
            adminUser = found;
        }
        else
        {
            if (!EmailValidation.LooksLikeEmail(newAdminUsername!))
                return BadRequest(new { error = "Band Admin username must be a valid email address." });
            if (await userManager.FindByNameAsync(newAdminUsername!) is not null)
                return BadRequest(new { error = "That username is already taken." });

            var loginUrl = $"{Request.Scheme}://{Request.Host}/login.html";
            var (created, error) = await userProvisioning.CreateAsync(newAdminUsername!, isSuperAdmin: false, loginUrl);
            if (!created) return BadRequest(new { error });
            adminUser = (await userManager.FindByNameAsync(newAdminUsername!))!;
        }

        var band = new Band { Name = name, Slug = slug };
        db.Bands.Add(band);
        db.BandMemberships.Add(new BandMembership { UserId = adminUser.Id, BandId = band.Id, Role = BandRole.BandAdmin });
        await db.SaveChangesAsync();
        return Ok(new { id = band.Id, name = band.Name, slug = band.Slug });
    }

    // Powers the "pick an existing user" half of the Band Admin picker
    // above - every account on the instance, not just current BandAdmins/
    // SuperAdmins, since a brand-new band's first admin is often someone
    // who's only ever been a regular User elsewhere (or nowhere yet).
    [HttpGet("all-users")]
    public async Task<IActionResult> ListAllUsers()
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new { id = u.Id, username = u.UserName })
            .ToListAsync();
        return Ok(users);
    }
}
