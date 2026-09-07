namespace BandManager.Data.Entities;

public enum AvailabilityStatus { Available, Unavailable, Tentative }

/// <summary>
/// One band member's availability for one day - per-day, not per-time-
/// range (a deliberate simplification: the request was "shows all band
/// members availability" for gig/rehearsal conflict-checking, which a
/// single daily status covers without the UI/DB complexity of tracking
/// arbitrary time windows). BandAdmin can create/edit/delete any member's
/// row; a member can only touch their own - enforced in
/// AvailabilityController, not here, same "table just holds data, the API
/// enforces the real rule" split used throughout this codebase (see
/// GigSetSong's doc comment for the precedent). Unique per (BandId,
/// UserId, Date) - one status per person per day, upserted in place
/// rather than accumulating history.
/// </summary>
public class Availability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public DateOnly Date { get; set; }
    public AvailabilityStatus Status { get; set; }
    public string? Note { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
