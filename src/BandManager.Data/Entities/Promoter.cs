namespace BandManager.Data.Entities;

/// <summary>
/// A band's reusable roster of promoters/booking contacts (e.g. "Wright
/// Live") - not owned by any one Venue, since a real promoter often books
/// gigs across many venues. A Venue can name one as its usual/default
/// (Venue.DefaultPromoterId); a Gig can independently pick the actual one
/// for that specific show (Gig.PromoterId), pre-filled from the venue's
/// default but freely overridable - this deliberately mirrors how the
/// venue's own booking contact rarely IS the promoter, per the user's own
/// correction during planning.
/// </summary>
public class Promoter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string Name { get; set; }
    public string? Company { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
