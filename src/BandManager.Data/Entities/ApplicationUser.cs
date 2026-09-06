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

    // Set whenever an admin creates an account or resets someone's
    // password on their behalf (they know the temp password, the account
    // owner doesn't yet have their own) - checked at login to redirect
    // straight to Profile with a message instead of the dashboard, and
    // cleared the moment ChangePassword succeeds.
    public bool MustChangePassword { get; set; }

    public ICollection<BandMembership> BandMemberships { get; set; } = new List<BandMembership>();
}
