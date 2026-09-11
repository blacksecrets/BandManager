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

    // Sticky "current gig" for this member, in this band - set whenever
    // they select a gig on Gig Management, or open the Flyer Editor for
    // one (either direction), then read to pre-populate a gig
    // selector/context elsewhere (Catalog's gig picker, a fresh Flyer
    // Editor open). Per-member, not band-wide, so one person's click
    // never silently changes what a bandmate sees. A stale ref (the gig
    // was since archived/deleted) is never an error - every reader just
    // treats "not found" as "no sticky selection."
    public string? LastSelectedGigRef { get; set; }

    // Touched whenever this membership's Band becomes the active one
    // (POST /api/bands/active, and login's auto-select) - lets login pick
    // the most-recently-used Band for a multi-band user instead of an
    // arbitrary one. Null until the first explicit selection.
    public DateTime? LastSelectedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
