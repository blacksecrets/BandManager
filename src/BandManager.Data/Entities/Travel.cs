namespace BandManager.Data.Entities;

public enum TravelCadence { Yearly, Quarterly }
public enum TripReason { Rehearsal, Gig, Other }

/// <summary>
/// One user's mileage-tracking settings - tax return cadence and the
/// vehicle currently used for band-related travel. Global per-user, not
/// per-band (a member only files one tax return regardless of how many
/// bands they're in), which is why this lives off Profile rather than
/// Band Admin. Cadence and vehicle are saved independently (two separate
/// forms on Profile), so both live on one row rather than splitting into
/// two tables - there's nothing to gain from a join here.
/// </summary>
public class UserTravelProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public TravelCadence Cadence { get; set; } = TravelCadence.Yearly;

    public string? VehicleMake { get; set; }
    public string? VehicleModel { get; set; }
    public int? VehicleYear { get; set; }
    public int? StartingMileage { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A band's shared named-address book for trip endpoints - deliberately
/// separate from Venue, which is scoped to gig-booking outreach
/// (promoter defaults, campaigns, tech-rider dimensions) and would drag
/// unrelated semantics into a mileage log. A rehearsal space or a
/// member's second home belongs here, not in the venue book. Looked up
/// by Name when a member types a manually-entered trip endpoint that
/// matches one already used by the band (see TravelController's
/// location-check endpoint) - matched addresses are copied onto the Trip
/// at save time, not live-linked, matching every other address on Trip.
/// </summary>
public class BandLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string Name { get; set; }
    public required string AddressLine1 { get; set; }
    public required string City { get; set; }
    public required string State { get; set; }
    public required string PostalCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One band-related drive, for one member's personal mileage log. Scoped
/// to the active band at the time it's recorded (like every other
/// band-scoped write in this app) - a member in multiple bands gets a
/// separate log per band, switched the same way every other page
/// switches context. From/To are flat, not a shared owned type, because
/// they resolve completely differently (home snapshot vs. band-location
/// lookup vs. one-off manual entry) and a shared shape would just be
/// prefixed the same way anyway.
///
/// Every From/To address is a snapshot copied in at save time - including
/// "Home", which deliberately does NOT track the user's current Profile
/// address afterward. This is the mirror image of GigPayoutRecipient's
/// "always compute live" rule (see PayoutRecipient.cs): there, drift is
/// avoided by never storing a value that could go stale; here, drift is
/// avoided by freezing the value so a later address change can't quietly
/// rewrite a past tax year's mileage log.
/// </summary>
public class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public DateOnly Date { get; set; }

    public bool FromIsHome { get; set; } = true;
    public string? FromName { get; set; }
    public string? FromAddressLine1 { get; set; }
    public string? FromCity { get; set; }
    public string? FromState { get; set; }
    public string? FromPostalCode { get; set; }

    public bool ToIsHome { get; set; }
    public string? ToName { get; set; }
    public string? ToAddressLine1 { get; set; }
    public string? ToCity { get; set; }
    public string? ToState { get; set; }
    public string? ToPostalCode { get; set; }

    // One-way miles from the best-effort driving-distance lookup (see
    // DrivingDistanceService) - null when no provider is configured or
    // the lookup failed; the trip still saves either way, same
    // never-block convention as AddressLookupService's USPS validation.
    public double? DistanceMiles { get; set; }
    public bool RoundTrip { get; set; }

    public TripReason Reason { get; set; }
    public string? OtherReasonText { get; set; }

    // Not a FK to Gig - matches how ScheduleItem/Flyer already reference a
    // gig by BandId+Ref rather than a hard relationship (see GigsController).
    public string? GigRef { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
