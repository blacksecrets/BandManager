namespace BandManager.Data.Entities;

/// <summary>
/// One row per Band a user belongs to - composite key (UserId, BandId).
/// This is what lets one person be BandAdmin of Band A and a regular User
/// (or nothing) of Band B, and is the basis for the band switcher and the
/// orphan-admin guard (BandMembershipService.EnsureNotLastAdmin).
/// </summary>
public class BandMembership
{
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public BandRole Role { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
