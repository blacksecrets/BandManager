using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record AddBandUserRequest(string Username, string Role, List<string>? Roles);
public record SetMemberRolesRequest(List<string> Roles);
public record UpdateUsernameRequest(string Username);
public record UpdateNameRequest(string? FirstName, string? LastName);
public record UpdateContactRequest(string? CellNumber, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode);
public record UpdateCatalogViewModeRequest(string ViewMode, string? Tab = null);
public record UpdateLastSelectedGigRequest(string? GigRef);

/// <summary>
/// Self-service profile (any logged-in user) + Band-scoped user
/// management (BandAdmin, for the active Band only) + platform branding
/// (SuperAdmin only) - ported from the old app's routes/profile.js, with
/// user management reshaped for multi-tenancy: the old app deleted a
/// whole account; here, "removing" someone removes their *membership*
/// from the active Band only (they may belong to others, or exist without
/// any), and "adding" someone attaches a membership to their existing
/// login if the username already exists, rather than always creating a
/// new account - same "onboard by attaching a membership" model the band
/// switcher's doc comment describes.
/// </summary>
[ApiController]
[Route("/api/profile")]
public class ProfileController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IActiveBandAccessor activeBand,
    BandMembershipService membershipService,
    UserProvisioningService userProvisioning,
    IEmailSender emailSender,
    IWebHostEnvironment env) : ControllerBase
{
    private static readonly string[] BrandTypes = ["logo", "background", "favicon"];
    private const long MaxBrandBytes = 20 * 1024 * 1024;

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var bandId = activeBand.GetActiveBandId();
        string? activeBandRole = null;
        string? activeBandName = null;
        string? lastSelectedGigRef = null;
        // A band archived while it was someone's active selection (or one
        // whose id is otherwise stale) reports back exactly like "no band
        // selected" - matches BandAccessCheck's own deny, so the nav
        // (which reads activeBandRole to decide what's enabled) doesn't
        // show a band as live when every real action against it 403s.
        var band = bandId is not null ? await db.Bands.FindAsync(bandId.Value) : null;
        if (band is not null && !band.IsArchived)
        {
            var membership = await db.BandMemberships
                .FirstOrDefaultAsync(m => m.UserId == user.Id && m.BandId == bandId);
            if (membership is not null)
            {
                activeBandRole = membership.Role.ToString();
                activeBandName = band.Name;
                lastSelectedGigRef = membership.LastSelectedGigRef;
            }
            else if (user.IsSuperAdmin)
            {
                activeBandName = band.Name;
                activeBandRole = "SuperAdmin";
            }
        }

        return Ok(new
        {
            id = user.Id,
            username = user.UserName,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            cellNumber = user.CellNumber,
            addressLine1 = user.AddressLine1,
            addressLine2 = user.AddressLine2,
            city = user.City,
            state = user.State,
            postalCode = user.PostalCode,
            catalogViewMode = user.CatalogViewMode ?? "thumbnails",
            catalogViewModeFlyers = user.CatalogViewModeFlyers ?? "thumbnails",
            isSuperAdmin = user.IsSuperAdmin,
            activeBandRole,
            activeBandName,
            lastSelectedGigRef,
            mustChangePassword = user.MustChangePassword,
            // isAdmin: true whenever the user can manage the active Band's
            // users - BandAdmin of it, or SuperAdmin regardless. Named to
            // match what wwwroot/assets/profile.js already checks.
            isAdmin = user.IsSuperAdmin || activeBandRole == "BandAdmin"
        });
    }

    // Images and Flyers' "View:" dropdown (Thumbnails/Details) - saved
    // server-side, not localStorage, so it follows the user across devices
    // like every other self-service preference here.
    [HttpPut("catalog-view-mode")]
    [Authorize]
    public async Task<IActionResult> UpdateCatalogViewMode([FromBody] UpdateCatalogViewModeRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (request.ViewMode is not ("thumbnails" or "details"))
            return BadRequest(new { error = "viewMode must be \"thumbnails\" or \"details\"." });

        if (request.Tab == "flyers") user.CatalogViewModeFlyers = request.ViewMode;
        else user.CatalogViewMode = request.ViewMode;
        await userManager.UpdateAsync(user);
        return Ok(new { ok = true, catalogViewMode = user.CatalogViewMode, catalogViewModeFlyers = user.CatalogViewModeFlyers });
    }

    // The sticky "current gig" for this member, in this band - see
    // BandMembership.LastSelectedGigRef's doc comment. Any band member
    // can set it (it's a personal convenience, not an admin action), and
    // the ref is never validated against the current gig list - a stale
    // value is harmless, every reader just falls back to "no selection."
    [HttpPut("last-selected-gig")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> UpdateLastSelectedGig([FromBody] UpdateLastSelectedGigRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var membership = await db.BandMemberships.FirstOrDefaultAsync(m => m.UserId == user.Id && m.BandId == bandId);
        if (membership is null) return Ok(new { ok = true }); // SuperAdmin-without-membership browsing - nothing to persist, not an error

        membership.LastSelectedGigRef = string.IsNullOrWhiteSpace(request.GigRef) ? null : request.GigRef.Trim();
        await db.SaveChangesAsync();
        return Ok(new { ok = true, lastSelectedGigRef = membership.LastSelectedGigRef });
    }

    // The account's UserName IS its email (required + validated at every
    // creation/change point - see EmailValidation) - there's no separate
    // "set your email" step anymore, which used to leave Profile showing a
    // confusingly blank email even though the username itself already
    // looked like one. Doesn't apply immediately: generates Identity's
    // built-in change-email token (the same mechanism as ForgotPassword's
    // reset token in AuthController) and emails a confirm link to the NEW
    // address - proving the requester actually controls it, not just typed
    // it. AuthController.ConfirmEmailChange (anonymous, like ResetPassword)
    // is what actually applies the change once that link is clicked. See
    // VerifyEmail below for a still-useful admin override, since real email
    // delivery is deferred (LoggingEmailSender) and an admin can't rely on
    // the user actually receiving this.
    [HttpPut("username")]
    [Authorize]
    public async Task<IActionResult> UpdateUsername([FromBody] UpdateUsernameRequest request)
    {
        var username = request.Username?.Trim() ?? "";
        if (!EmailValidation.LooksLikeEmail(username))
            return BadRequest(new { error = "Username must be a valid email address." });

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        if (string.Equals(user.UserName, username, StringComparison.OrdinalIgnoreCase))
            return Ok(new { ok = true, pending = false, username = user.UserName });

        var existing = await userManager.FindByNameAsync(username);
        if (existing is not null) return BadRequest(new { error = "That username is already taken." });

        var token = await userManager.GenerateChangeEmailTokenAsync(user, username);
        var link = $"{Request.Scheme}://{Request.Host}/confirm-email-change.html?userId={user.Id}&newEmail={Uri.EscapeDataString(username)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(username, "Confirm your new BandManager email",
            $"""
            <p>You (or someone signed in as "{user.UserName}") asked to change this BandManager account's email/username to this address.</p>
            <p><a href="{link}" style="display:inline-block;padding:10px 20px;background:#c0392b;color:#fff;text-decoration:none;border-radius:6px;font-weight:bold;">Confirm New Email</a></p>
            <p>This link expires in 24 hours. If you didn't request this, you can safely ignore this email - your account isn't affected until this link is clicked.</p>
            """);

        return Ok(new { ok = true, pending = true, pendingEmail = username });
    }

    // Self-service only - see ApplicationUser.FirstName's doc comment for
    // why it isn't collected at account creation. Shown to a SuperAdmin
    // reviewing a song edit request so they know who proposed it.
    [HttpPut("name")]
    [Authorize]
    public async Task<IActionResult> UpdateName([FromBody] UpdateNameRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        user.FirstName = string.IsNullOrEmpty(firstName) ? null : firstName;
        user.LastName = string.IsNullOrEmpty(lastName) ? null : lastName;
        await userManager.UpdateAsync(user);

        return Ok(new { ok = true, firstName = user.FirstName, lastName = user.LastName });
    }

    // Self-service contact info + mailing address - AddressLookupController
    // is the separate USPS-validation step a client can call before this,
    // but nothing here requires that a lookup ever ran (an unvalidated
    // address is still saved as typed).
    [HttpPut("contact")]
    [Authorize]
    public async Task<IActionResult> UpdateContact([FromBody] UpdateContactRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        user.CellNumber = Clean(request.CellNumber);
        user.AddressLine1 = Clean(request.AddressLine1);
        user.AddressLine2 = Clean(request.AddressLine2);
        user.City = Clean(request.City);
        user.State = Clean(request.State);
        user.PostalCode = Clean(request.PostalCode);
        await userManager.UpdateAsync(user);

        return Ok(new { ok = true });
    }

    [HttpPost("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
            return BadRequest(new { error = "Both current and new password are required." });
        if (request.NewPassword.Length is < 8 or > 200)
            return BadRequest(new { error = "New password must be 8-200 characters." });

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var incorrect = result.Errors.Any(e => e.Code == "PasswordMismatch");
            return incorrect
                ? Unauthorized(new { error = "Current password is incorrect." })
                : BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        // Re-sign-in so the auth cookie's security stamp stays valid -
        // ASP.NET Core Identity invalidates the old one on password change.
        await signInManager.RefreshSignInAsync(user);
        return Ok(new { ok = true });
    }

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    // Lightweight roster for the active band - any member, not just
    // BandAdmin (unlike ListUsers below, which carries admin-only fields
    // like email-confirmed status). Used by the Calendar's availability
    // roster and the assignee picker.
    [HttpGet("band-members")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> ListBandMembers()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var members = await db.BandMemberships.Include(m => m.User)
            .Where(m => m.BandId == bandId)
            .OrderBy(m => m.User.FirstName ?? m.User.UserName)
            .Select(m => new
            {
                id = m.UserId,
                firstName = m.User.FirstName ?? m.User.UserName!.Split('@')[0],
                username = m.User.UserName,
                role = m.Role.ToString()
            })
            .ToListAsync();
        return Ok(members);
    }

    // The seed picklist for "role(s) in the band" (Bassist, Sound
    // Engineer, ...) - see BandMemberRoles.cs - plus any custom role
    // string this specific band has already saved (via SetMemberRoles
    // below), so a typed-in role only has to be confirmed once and then
    // behaves like a normal checkbox for the rest of that band. Custom
    // entries never leak to another band - each BandMemberRole row
    // carries its own BandId, so this merge only ever looks at the
    // active band's own rows.
    [HttpGet("band-roles")]
    [Authorize]
    public async Task<IActionResult> BandRoles()
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return Ok(BandManager.Data.BandMemberRoles.All);

        var custom = await db.BandMemberRoles.Where(r => r.BandId == bandId.Value)
            .Select(r => r.Role).Distinct().ToListAsync();
        return Ok(BandManager.Data.BandMemberRoles.All.Concat(custom.Except(BandManager.Data.BandMemberRoles.All)));
    }

    // Custom roles are confirmed client-side (an "are you sure?" prompt)
    // before ever reaching here, so this only guards against garbage
    // (empty/absurdly long strings) - it no longer requires membership
    // in BandMemberRoles.All, which would defeat the whole point of a
    // custom entry.
    private static List<string> CleanRoles(IEnumerable<string>? roles) =>
        (roles ?? []).Select(r => r.Trim()).Where(r => r.Length is > 0 and <= 60).Distinct().ToList();

    // Read-only, self-service: every band the current user belongs to,
    // and their Band Role(s) in each - not scoped to the active band,
    // since the point is seeing all of them at once. A band the user
    // belongs to but holds no roles in still appears (empty list), so
    // "you're in this band but haven't been assigned a role yet" is
    // visible too.
    [HttpGet("my-roles")]
    [Authorize]
    public async Task<IActionResult> MyRoles()
    {
        var userId = userManager.GetUserId(User);
        if (userId is null) return Unauthorized();
        var uid = Guid.Parse(userId);

        var bands = await db.BandMemberships.Include(m => m.Band).AsNoTracking()
            .Where(m => m.UserId == uid && !m.Band.IsArchived)
            .OrderBy(m => m.Band.Name)
            .Select(m => new { m.BandId, BandName = m.Band.Name })
            .ToListAsync();

        var rolesByBand = await db.BandMemberRoles.AsNoTracking()
            .Where(r => r.UserId == uid)
            .GroupBy(r => r.BandId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(r => r.Role).OrderBy(r => r).ToList());

        return Ok(bands.Select(b => new { bandName = b.BandName, roles = rolesByBand.GetValueOrDefault(b.BandId, []) }));
    }

    [HttpGet("users")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> ListUsers()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var members = await db.BandMemberships.Include(m => m.User)
            .Where(m => m.BandId == bandId)
            .OrderBy(m => m.User.UserName)
            .ToListAsync();
        var rolesByUser = await db.BandMemberRoles.Where(r => r.BandId == bandId)
            .GroupBy(r => r.UserId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(r => r.Role).ToList());

        return Ok(members.Select(m => new
        {
            id = m.UserId,
            username = m.User.UserName,
            is_admin = m.Role == BandRole.BandAdmin,
            email_confirmed = m.User.EmailConfirmed,
            created_at = m.CreatedAt,
            roles = rolesByUser.GetValueOrDefault(m.UserId, [])
        }));
    }

    [HttpPost("users")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> AddUser([FromBody] AddBandUserRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var username = request.Username?.Trim();
        if (string.IsNullOrEmpty(username) || !EmailValidation.LooksLikeEmail(username))
            return BadRequest(new { error = "Username must be a valid email address." });
        if (!Enum.TryParse<BandRole>(request.Role, ignoreCase: true, out var role))
            return BadRequest(new { error = "Invalid role." });

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            // New account - temp password is generated and emailed, not
            // chosen by whoever's adding them (see UserProvisioningService).
            var loginUrl = $"{Request.Scheme}://{Request.Host}/login.html";
            var (created, error) = await userProvisioning.CreateAsync(username, isSuperAdmin: false, loginUrl);
            if (!created) return BadRequest(new { error });
            user = await userManager.FindByNameAsync(username);
        }
        else if (await db.BandMemberships.AnyAsync(m => m.UserId == user.Id && m.BandId == bandId))
        {
            return BadRequest(new { error = "That user is already a member of this band." });
        }

        var cleanRoles = CleanRoles(request.Roles);
        db.BandMemberships.Add(new BandMembership { UserId = user!.Id, BandId = bandId, Role = role });
        foreach (var r in cleanRoles)
        {
            db.BandMemberRoles.Add(new BandMemberRole { UserId = user.Id, BandId = bandId, Role = r });
        }
        await db.SaveChangesAsync();
        await AutoAddTuningInstrumentsAsync(bandId, cleanRoles);
        return Ok(new { ok = true });
    }

    // One-way convenience: a newly-assigned instrument-shaped Band Role
    // (see BandMemberRoles.TuningEligible) gets mirrored onto the
    // Repertoire tuning tracker (BandInstrument) automatically, so an
    // admin doesn't have to type "Lead Guitar" in two different places.
    // Never renames or removes an existing BandInstrument row, and never
    // duplicates one that's already there (by exact name) - the tuning
    // tracker stays independently editable either way.
    private async Task AutoAddTuningInstrumentsAsync(Guid bandId, IEnumerable<string> roles)
    {
        var eligible = roles.Where(r => BandManager.Data.BandMemberRoles.TuningEligible.Contains(r)).Distinct().ToList();
        if (eligible.Count == 0) return;

        var existingNames = await db.BandInstruments.Where(i => i.BandId == bandId).Select(i => i.Name).ToListAsync();
        var toAdd = eligible.Where(r => !existingNames.Contains(r)).ToList();
        if (toAdd.Count == 0) return;

        var nextSort = existingNames.Count == 0 ? 0 : await db.BandInstruments.Where(i => i.BandId == bandId).MaxAsync(i => i.SortOrder) + 1;
        foreach (var name in toAdd)
        {
            db.BandInstruments.Add(new BandInstrument { BandId = bandId, Name = name, SortOrder = nextSort++ });
        }
        await db.SaveChangesAsync();
    }

    // Replaces the full role set for an existing member - the picker UI
    // always sends the complete new selection (same "not a partial patch"
    // reasoning as CadenceController's assignee fields), so this is a
    // delete-and-reinsert rather than a diff.
    [HttpPut("users/{userId:guid}/roles")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetMemberRoles(Guid userId, [FromBody] SetMemberRolesRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.BandMemberships.AnyAsync(m => m.UserId == userId && m.BandId == bandId))
            return NotFound(new { error = "Not found" });

        var cleanRoles = CleanRoles(request.Roles);
        var existing = db.BandMemberRoles.Where(r => r.UserId == userId && r.BandId == bandId);
        db.BandMemberRoles.RemoveRange(existing);
        foreach (var r in cleanRoles)
        {
            db.BandMemberRoles.Add(new BandMemberRole { UserId = userId, BandId = bandId, Role = r });
        }
        await db.SaveChangesAsync();
        await AutoAddTuningInstrumentsAsync(bandId, cleanRoles);
        return Ok(new { ok = true, roles = cleanRoles });
    }

    // Manual override for "this email is actually good" while there's no
    // real email delivery to send a real confirmation link through yet
    // (see LoggingEmailSender) - lets a BandAdmin/SuperAdmin unblock
    // whatever eventually starts checking EmailConfirmed without waiting
    // on that. Scoped to the active Band's own members, same as every
    // other user-management action here.
    [HttpPost("users/{userId:guid}/verify-email")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> VerifyEmail(Guid userId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.BandMemberships.AnyAsync(m => m.UserId == userId && m.BandId == bandId))
            return NotFound(new { error = "Not found" });

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { error = "Not found" });
        if (string.IsNullOrWhiteSpace(user.Email))
            return BadRequest(new { error = "This user has no email/username to verify." });

        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);
        return Ok(new { ok = true });
    }

    [HttpDelete("users/{userId:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> RemoveUser(Guid userId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var membership = await db.BandMemberships.FirstOrDefaultAsync(m => m.UserId == userId && m.BandId == bandId);
        if (membership is null) return NotFound(new { error = "Not found" });

        if (membership.Role == BandRole.BandAdmin)
        {
            try
            {
                await membershipService.EnsureNotLastAdminAsync(bandId, userId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        db.BandMemberships.Remove(membership);
        db.BandMemberRoles.RemoveRange(db.BandMemberRoles.Where(r => r.UserId == userId && r.BandId == bandId));
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- Platform branding (SuperAdmin only, global - see PlatformSetting) ---

    [HttpGet("branding")]
    public async Task<IActionResult> GetBranding()
    {
        var settings = await db.PlatformSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(new
        {
            logoUrl = settings.GetValueOrDefault("logo_filename") is { } logo ? $"/branding/{logo}" : null,
            backgroundUrl = settings.GetValueOrDefault("background_filename") is { } bg ? $"/branding/{bg}" : null,
            faviconUrl = settings.GetValueOrDefault("favicon_filename") is { } fav ? $"/branding/{fav}" : null
        });
    }

    [HttpPost("branding/{type}")]
    [Authorize(Policy = "SuperAdmin")]
    [RequestSizeLimit(MaxBrandBytes)]
    public async Task<IActionResult> UploadBranding(string type)
    {
        if (!BrandTypes.Contains(type)) return BadRequest(new { error = "Invalid branding type" });
        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided" });
        if (!(file.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Expected an image file, got {file.ContentType}" });

        var brandingDir = Path.Combine(env.ContentRootPath, "data", "branding");
        Directory.CreateDirectory(brandingDir);

        var settingKey = $"{type}_filename";
        var existing = await db.PlatformSettings.FindAsync(settingKey);
        if (existing?.Value is not null)
        {
            try { System.IO.File.Delete(Path.Combine(brandingDir, existing.Value)); } catch { /* best-effort */ }
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".png";
        var fileName = $"{type}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(brandingDir, fileName)))
        {
            await file.CopyToAsync(stream);
        }

        if (existing is null)
        {
            db.PlatformSettings.Add(new PlatformSetting { Key = settingKey, Value = fileName });
        }
        else
        {
            existing.Value = fileName;
        }
        await db.SaveChangesAsync();

        return Ok(new { ok = true, url = $"/branding/{fileName}" });
    }

    [HttpDelete("branding/{type}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> DeleteBranding(string type)
    {
        if (!BrandTypes.Contains(type)) return BadRequest(new { error = "Invalid branding type" });

        var settingKey = $"{type}_filename";
        var existing = await db.PlatformSettings.FindAsync(settingKey);
        if (existing?.Value is not null)
        {
            var brandingDir = Path.Combine(env.ContentRootPath, "data", "branding");
            try { System.IO.File.Delete(Path.Combine(brandingDir, existing.Value)); } catch { /* best-effort */ }
            db.PlatformSettings.Remove(existing);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
