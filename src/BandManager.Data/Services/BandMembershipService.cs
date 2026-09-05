using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data.Services;

/// <summary>
/// The orphan-admin guard: the last BandAdmin of a Band can never be
/// removed or demoted, since that would leave the Band with no one able
/// to manage it, users, or Setup - only SuperAdmin could then fix it.
/// Lives in one shared place, called from every entry point that could
/// remove/demote a BandAdmin (Profile's user management now; any future
/// SuperAdmin-side membership editing too), so the rule can't be
/// bypassed by using a different route in.
/// </summary>
public class BandMembershipService(ApplicationDbContext db)
{
    /// <summary>Throws if removing/demoting the given membership would
    /// leave its Band with zero BandAdmins.</summary>
    public async Task EnsureNotLastAdminAsync(Guid bandId, Guid userIdBeingRemovedOrDemoted)
    {
        var otherAdminCount = await db.BandMemberships.CountAsync(m =>
            m.BandId == bandId && m.Role == BandRole.BandAdmin && m.UserId != userIdBeingRemovedOrDemoted);
        if (otherAdminCount == 0)
        {
            throw new InvalidOperationException(
                "Can't remove the last Band Admin - promote someone else first, or ask a SuperAdmin for help.");
        }
    }
}
