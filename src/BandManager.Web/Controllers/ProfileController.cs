using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record AddBandUserRequest(string Username, string? Password, string Role);
public record UpdateEmailRequest(string? Email);

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
    IWebHostEnvironment env) : ControllerBase
{
    private static readonly string[] BrandTypes = ["logo", "background"];
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
        if (bandId is not null)
        {
            var membership = await db.BandMemberships.Include(m => m.Band)
                .FirstOrDefaultAsync(m => m.UserId == user.Id && m.BandId == bandId);
            if (membership is not null)
            {
                activeBandRole = membership.Role.ToString();
                activeBandName = membership.Band.Name;
            }
            else if (user.IsSuperAdmin)
            {
                var band = await db.Bands.FindAsync(bandId.Value);
                activeBandName = band?.Name;
                activeBandRole = "SuperAdmin";
            }
        }

        return Ok(new
        {
            username = user.UserName,
            email = user.Email,
            isSuperAdmin = user.IsSuperAdmin,
            activeBandRole,
            activeBandName,
            // isAdmin: true whenever the user can manage the active Band's
            // users - BandAdmin of it, or SuperAdmin regardless. Named to
            // match what wwwroot/assets/profile.js already checks.
            isAdmin = user.IsSuperAdmin || activeBandRole == "BandAdmin"
        });
    }

    [HttpPut("email")]
    [Authorize]
    public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailRequest request)
    {
        var email = request.Email?.Trim() ?? "";
        // Not required to use the app at all - only required for the
        // "forgot password" flow to have somewhere to send a reset link.
        // Clearing it back to blank is allowed (SetEmailAsync accepts null).
        if (email.Length > 0 && (!email.Contains('@') || email.Length > 256))
            return BadRequest(new { error = "That doesn't look like a valid email address." });

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        await userManager.SetEmailAsync(user, email.Length == 0 ? null : email);
        return Ok(new { ok = true, email = user.Email });
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
        if (string.IsNullOrEmpty(username) || username.Length > 100)
            return BadRequest(new { error = "Username is required (max 100 chars)." });
        if (!Enum.TryParse<BandRole>(request.Role, ignoreCase: true, out var role))
            return BadRequest(new { error = "Invalid role." });

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 8 or > 200)
                return BadRequest(new { error = "That user doesn't exist yet - a password (8-200 chars) is required to create it." });

            user = new ApplicationUser { UserName = username };
            var createResult = await userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return BadRequest(new { error = string.Join("; ", createResult.Errors.Select(e => e.Description)) });
        }
        else if (await db.BandMemberships.AnyAsync(m => m.UserId == user.Id && m.BandId == bandId))
        {
            return BadRequest(new { error = "That user is already a member of this band." });
        }

        db.BandMemberships.Add(new BandMembership { UserId = user.Id, BandId = bandId, Role = role });
        await db.SaveChangesAsync();
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
            backgroundUrl = settings.GetValueOrDefault("background_filename") is { } bg ? $"/branding/{bg}" : null
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
