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
        var bandId = activeBand.GetActiveBandId();

        // An archived Band is deactivated for everyone, SuperAdmin
        // included - checked before the SuperAdmin bypass below, not
        // after, so archiving actually revokes access rather than only
        // hiding the band from switchers/listings. A stale active-band
        // session pointing at a Band archived after it was selected is
        // denied here too, not just blocked from being newly selected
        // (BandsController.SetActive).
        if (bandId is not null)
        {
            var archived = await db.Bands.AsNoTracking()
                .Where(b => b.Id == bandId)
                .Select(b => (bool?)b.IsArchived)
                .FirstOrDefaultAsync();
            if (archived is null or true) return false;
        }

        if (user.IsSuperAdmin()) return true;

        var userId = user.GetUserId();
        if (userId is null) return false;

        if (bandId is null)
        {
            // The session holding "active band" is server-side in-memory
            // (see ActiveBandAccessor) - it doesn't survive an app
            // restart, even though the separate, persistent auth cookie
            // does. Without this, a user who was already logged in before
            // a restart looks logged in (nav renders, cookie's valid) but
            // every band-scoped call silently 403s forever, since nothing
            // ever re-prompts them to pick a band again. Re-resolve it
            // exactly like login already does when it's unambiguous (a
            // single membership) instead of failing every request until
            // they manually reselect from the band switcher. Archived
            // bands are excluded from this pool - deactivated means it's
            // never auto-picked back in, even as someone's only membership.
            var memberships = await db.BandMemberships.AsNoTracking()
                .Include(m => m.Band)
                .Where(m => m.UserId == userId && !m.Band.IsArchived)
                .Select(m => m.BandId).ToListAsync();
            if (memberships.Count != 1) return false;
            bandId = memberships[0];
            activeBand.SetActiveBandId(bandId.Value);
        }

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
