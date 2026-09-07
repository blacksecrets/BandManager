namespace BandManager.Data.Entities;

public enum EditRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>One field's proposed value, snapshotted at request time - the
/// "old" side is what to show a reviewer without needing to separately
/// diff against the Song's current (possibly since-changed-elsewhere)
/// state, and it's never used to apply anything (only NewValue is).</summary>
public record SongFieldChange(string? OldValue, string? NewValue);

/// <summary>
/// A proposed edit to a shared (cross-Band) Song's fields, awaiting
/// SuperAdmin review - see SongsController.ProposeEdit and
/// SongEditRequestsController. Only Song's own scalar fields go through
/// this; RepertoireEntry.Status and Song.Tunings are Band-local/
/// intentionally-collaborative data and stay immediate, never routed here.
/// A Song can have at most one Pending request at a time (enforced both in
/// SongsController.ProposeEdit and by a partial unique index in
/// ApplicationDbContext).
/// </summary>
public class SongEditRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public Guid RequestedByUserId { get; set; }
    public ApplicationUser RequestedByUser { get; set; } = null!;

    // The requester's active Band at proposal time - not necessarily the
    // only Band using this Song, just who to credit/notify.
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public Dictionary<string, SongFieldChange> Changes { get; set; } = new();
    public EditRequestStatus Status { get; set; } = EditRequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public ApplicationUser? ResolvedByUser { get; set; }

    // The SuperAdmin's message to the requester either way - required on
    // both approve and reject (see SongEditRequestsController), so this is
    // only null while Pending.
    public string? ResolutionMessage { get; set; }
}
