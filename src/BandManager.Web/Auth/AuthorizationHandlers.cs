using System.Security.Claims;
using BandManager.Data;
using BandManager.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Auth;

internal static class ClaimsPrincipalExtensions
{
    public static bool IsSuperAdmin(this ClaimsPrincipal user) =>
        user.FindFirstValue(ApplicationUserClaimsPrincipalFactory.IsSuperAdminClaimType) == "true";

    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>
/// Shared by all three handlers below: SuperAdmin always passes without a
/// membership row (see Band data model plan - "sees all bands... without
/// needing a membership row"); everyone else needs a real BandMembership
/// for the current active Band, checked at the required role or above.
/// </summary>
internal static class BandAccessCheck
{
    public static async Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        BandRole? minimumRole,
        IActiveBandAccessor activeBand,
        ApplicationDbContext db)
    {
        if (user.IsSuperAdmin()) return true;

        var userId = user.GetUserId();
        var bandId = activeBand.GetActiveBandId();
        if (userId is null || bandId is null) return false;

        var membership = await db.BandMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.BandId == bandId);

        if (membership is null) return false;
        return minimumRole is null || membership.Role >= minimumRole;
    }
}

public class BandMemberRequirementHandler(IActiveBandAccessor activeBand, ApplicationDbContext db)
    : AuthorizationHandler<BandMemberRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, BandMemberRequirement requirement)
    {
        if (await BandAccessCheck.HasAccessAsync(context.User, null, activeBand, db))
            context.Succeed(requirement);
    }
}

public class BandAdminRequirementHandler(IActiveBandAccessor activeBand, ApplicationDbContext db)
    : AuthorizationHandler<BandAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, BandAdminRequirement requirement)
    {
        if (await BandAccessCheck.HasAccessAsync(context.User, BandRole.BandAdmin, activeBand, db))
            context.Succeed(requirement);
    }
}

public class SuperAdminRequirementHandler : AuthorizationHandler<SuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        if (context.User.IsSuperAdmin())
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
