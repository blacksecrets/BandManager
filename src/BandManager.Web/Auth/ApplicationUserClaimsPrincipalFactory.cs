using System.Security.Claims;
using BandManager.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BandManager.Web.Auth;

/// <summary>
/// Bakes IsSuperAdmin into the auth cookie's claims at sign-in, so
/// authorization handlers can check it without a DB round-trip on every
/// request. If a user is ever promoted/demoted from SuperAdmin, they need
/// to sign in again (or an admin action re-issues the cookie) to see it -
/// acceptable for how rarely that changes.
/// </summary>
public class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    public const string IsSuperAdminClaimType = "band_manager:is_super_admin";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(IsSuperAdminClaimType, user.IsSuperAdmin ? "true" : "false"));
        return identity;
    }
}
