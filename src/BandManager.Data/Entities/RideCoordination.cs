namespace BandManager.Data.Entities;

/// <summary>
/// One shared meeting point/time for one gig - band-wide, not per-user,
/// unlike Trip (Travel.cs), which is deliberately private per-member for
/// tax mileage logging. This is the coordination layer Trip doesn't
/// cover: "where/when are we meeting" for a gig, visible to and editable
/// by any band member (last write wins, same as every other
/// shared-not-owned field in this codebase - e.g. Gig itself). Not a FK
/// to Gig, matching Trip/ScheduleItem/Flyer's existing BandId+GigRef
/// reference convention rather than a hard relationship.
/// </summary>
public class GigMeetingPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string GigRef { get; set; }

    public string? MeetingPoint { get; set; }
    public string? MeetingTime { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
    public ApplicationUser UpdatedByUser { get; set; } = null!;
}

/// <summary>
/// One member's "I'm driving to this gig" offer - band-wide visible, so
/// the rest of the band can see who's driving and coordinate riding
/// along, instead of everyone quietly driving separately. One row per
/// (BandId, GigRef, UserId) - a member can only have one active offer
/// per gig, re-saved in place rather than accumulating history, same
/// upsert pattern as Availability.
/// </summary>
public class GigRideOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string GigRef { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int? SeatsAvailable { get; set; }
    public string? Note { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
