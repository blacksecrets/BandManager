using Microsoft.AspNetCore.Identity;

namespace BandManager.Data.Entities;

/// <summary>
/// SuperAdmin is a single global flag, not a per-band membership - it
/// bypasses band-scoping checks entirely and sees every Band without
/// needing a BandMembership row (see BandMemberRequirementHandler).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public bool IsSuperAdmin { get; set; }

    public ICollection<BandMembership> BandMemberships { get; set; } = new List<BandMembership>();
}
