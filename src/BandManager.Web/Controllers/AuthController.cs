using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public class LoginFormModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public record ForgotPasswordRequest(string Username);
public record ResetPasswordRequest(string UserId, string Token, string NewPassword);

/// <summary>
/// Classic form-post routes (/login, /logout) at the root, matching the
/// existing wwwroot/login.html and the topbar's logout form exactly -
/// deliberately not [ApiController]/JSON-only here, so the old frontend
/// markup needs no change for the basic login/logout flow. /api/auth/me
/// is the one JSON endpoint, for the frontend to know who's signed in.
/// </summary>
public class AuthController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    IEmailSender emailSender) : Controller
{
    [HttpPost("/login")]
    public async Task<IActionResult> Login([FromForm] LoginFormModel form)
    {
        var result = await signInManager.PasswordSignInAsync(form.Username, form.Password, isPersistent: true, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return Redirect("/login.html?error=1");
        }

        var user = await userManager.FindByNameAsync(form.Username);
        if (user is not null && !user.IsSuperAdmin)
        {
            // Only auto-pick when unambiguous - more than one membership
            // means the frontend needs to show the band switcher before
            // anything band-scoped can be requested.
            var memberships = await db.BandMemberships.AsNoTracking()
                .Where(m => m.UserId == user.Id).ToListAsync();
            if (memberships.Count == 1)
            {
                activeBand.SetActiveBandId(memberships[0].BandId);
            }
        }

        // An admin-set password (new account, or a reset done on someone's
        // behalf) sends them straight to Profile with a message instead of
        // the dashboard, rather than leaving them to notice on their own.
        if (user?.MustChangePassword == true)
            return Redirect("/profile?mustChangePassword=1");

        return Redirect("/");
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        activeBand.Clear();
        return Redirect("/login.html");
    }

    /// <summary>
    /// Always returns the same generic response whether or not the
    /// username/email is real - a specific "no such user" or "no email on
    /// file" error here would let anyone probe which usernames exist.
    /// </summary>
    [HttpPost("/api/auth/forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest body)
    {
        var user = await userManager.FindByNameAsync(body.Username ?? "");
        if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var link = $"{Request.Scheme}://{Request.Host}/reset-password.html?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            await emailSender.SendAsync(user.Email, "Reset your BandManager password",
                $"""
                <p>Someone (hopefully you) asked to reset the password for the BandManager account "{user.UserName}".</p>
                <p><a href="{link}">Reset your password</a></p>
                <p>This link expires in 24 hours. If you didn't request this, you can safely ignore this email.</p>
                """);
        }
        return Ok(new { ok = true });
    }

    [HttpPost("/api/auth/reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest body)
    {
        if (!Guid.TryParse(body.UserId, out var userId))
            return BadRequest(new { error = "This reset link is invalid. Request a new one." });

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return BadRequest(new { error = "This reset link is invalid. Request a new one." });

        var result = await userManager.ResetPasswordAsync(user, body.Token, body.NewPassword);
        if (!result.Succeeded)
        {
            var badToken = result.Errors.Any(e => e.Code is "InvalidToken");
            return BadRequest(new
            {
                error = badToken
                    ? "This reset link is invalid or has expired. Request a new one."
                    : string.Join(" ", result.Errors.Select(e => e.Description))
            });
        }
        return Ok(new { ok = true });
    }

    [HttpGet("/api/auth/me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var memberships = await db.BandMemberships.AsNoTracking()
            .Include(m => m.Band)
            .Where(m => m.UserId == user.Id)
            .Select(m => new { bandId = m.BandId, bandName = m.Band.Name, role = m.Role.ToString() })
            .ToListAsync();

        return Ok(new
        {
            id = user.Id,
            username = user.UserName,
            isSuperAdmin = user.IsSuperAdmin,
            memberships,
            activeBandId = activeBand.GetActiveBandId()
        });
    }
}
