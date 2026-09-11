namespace BandManager.Data.Entities;

/// <summary>
/// One venue this band has reached out to (or might). Band-scoped (not
/// shared globally like Song) - each band builds its own venue book.
/// Address fields mirror ApplicationUser's, same US-shaped/nullable
/// convention. A Venue's outreach history lives on its VenueCampaign,
/// not here - this is just the "who/where" record.
/// </summary>
public class Venue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }

    // For the Tech Rider - a venue's room capacity and stage footprint,
    // both purely informational (no validation against an Act's gear).
    public int? AudienceCapacity { get; set; }
    public decimal? StageWidthFeet { get; set; }
    public decimal? StageDepthFeet { get; set; }

    // This venue's usual promoter, if it has one - just a default; a Gig
    // at this venue can pick a different one (see Gig.PromoterId).
    public Guid? DefaultPromoterId { get; set; }
    public Promoter? DefaultPromoter { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VenueContact> Contacts { get; set; } = new List<VenueContact>();
    public VenueCampaign? Campaign { get; set; }
}

/// <summary>One person at a Venue - a venue often has several (booking
/// manager, general manager...), so this is its own table rather than
/// flat fields on Venue. IsPrimary marks which one a new communication
/// defaults its To/phone to.</summary>
public class VenueContact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
}
