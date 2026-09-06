using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BandManager.Web.Controllers;

public record FirstAdminRequest(string Username, string Password);

/// <summary>
/// One-time bootstrap: with zero users in the system there's no other way
/// to create an account at all. Only ever usable while the user table is
/// empty - the very first account becomes SuperAdmin, since SuperAdmin is
/// what's needed to onboard the first real Band anyway. Mirrors the old
/// Node app's requireSetupIfNoUsers gate (src/auth.js).
/// </summary>
[ApiController]
[Route("/api/setup")]
public class SetupController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("needed")]
    public async Task<IActionResult> Needed()
    {
        var anyUsers = userManager.Users.Any();
        return Ok(new { needed = !anyUsers });
    }

    [HttpPost("first-admin")]
    public async Task<IActionResult> CreateFirstAdmin([FromBody] FirstAdminRequest request)
    {
        if (userManager.Users.Any())
        {
            return Conflict(new { error = "Setup has already been completed." });
        }

        var username = request.Username?.Trim() ?? "";
        if (!EmailValidation.LooksLikeEmail(username))
            return BadRequest(new { error = "Username must be a valid email address." });

        // Self-chosen password, right now, by the account's own owner - no
        // MustChangePassword needed (that's for a password someone *else*
        // assigned). EmailConfirmed=true for the same "who's vouching for
        // it" reason as ProfileController.AddUser's admin-created path.
        var user = new ApplicationUser { UserName = username, Email = username, EmailConfirmed = true, IsSuperAdmin = true };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });
        }

        return Ok(new { ok = true });
    }
}
