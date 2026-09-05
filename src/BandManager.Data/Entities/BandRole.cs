namespace BandManager.Data.Entities;

/// <summary>
/// Per-membership role - deliberately not a global flag on the user, since
/// the same person can be BandAdmin of one Band and a regular User (or
/// nothing) of another. SuperAdmin is separate entirely (ApplicationUser.IsSuperAdmin),
/// not a value here.
/// </summary>
public enum BandRole
{
    User = 0,
    BandAdmin = 1
}
