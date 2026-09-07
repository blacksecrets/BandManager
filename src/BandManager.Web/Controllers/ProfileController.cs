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
public record AddBandUserRequest(string Username, string Role);
public record UpdateUsernameRequest(string Username);
public record UpdateFirstNameRequest(string? FirstName);

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
            isSuperAdmin = user.IsSuperAdmin,
            activeBandRole,
            activeBandName,
            mustChangePassword = user.MustChangePassword,
            // isAdmin: true whenever the user can manage the active Band's
            // users - BandAdmin of it, or SuperAdmin regardless. Named to
            // match what wwwroot/assets/profile.js already checks.
            isAdmin = user.IsSuperAdmin || activeBandRole == "BandAdmin"
        });
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
    [HttpPut("first-name")]
    [Authorize]
    public async Task<IActionResult> UpdateFirstName([FromBody] UpdateFirstNameRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var firstName = request.FirstName?.Trim();
        user.FirstName = string.IsNullOrEmpty(firstName) ? null : firstName;
        await userManager.UpdateAsync(user);

        return Ok(new { ok = true, firstName = user.FirstName });
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
            .Select(m => new { id = m.UserId, firstName = m.User.FirstName ?? m.User.UserName!.Split('@')[0] })
            .ToListAsync();
        return Ok(members);
    }

    [HttpGet("users")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> ListUsers()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var members = await db.BandMemberships.Include(m => m.User)
            .Where(m => m.BandId == bandId)
            .OrderBy(m => m.User.UserName)
            .Select(m => new
            {
                id = m.UserId,
                username = m.User.UserName,
                is_admin = m.Role == BandRole.BandAdmin,
                email_confirmed = m.User.EmailConfirmed,
                created_at = m.CreatedAt
            })
            .ToListAsync();
        return Ok(members);
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

        db.BandMemberships.Add(new BandMembership { UserId = user!.Id, BandId = bandId, Role = role });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
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
