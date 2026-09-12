namespace BandManager.Data.Entities;

/// <summary>
/// A per-user inbox item - originally only created for a SongEditRequest's
/// approve/reject outcome (see SongEditRequestsController), now also used
/// by NotificationReminderService (gig/rehearsal/availability reminders)
/// and reassignment notices (Cadence task assignment). Kind plus a set of
/// nullable refs (one per notification-generating entity, exactly the
/// SongEditRequestId pattern this class already established) is how a new
/// notification type keeps not needing its own separate inbox/bell.
/// SuperAdmin's own "pending reviews" badge does NOT use this table -
/// that's a live COUNT over SongEditRequest instead, since it's a shared
/// work queue, not a per-recipient message (see
/// SongEditRequestsController.PendingCount).
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string Message { get; set; }

    public NotificationKind Kind { get; set; }

    // Null unless Kind is the matching type - see Kind's doc comment.
    public Guid? SongEditRequestId { get; set; }
    public SongEditRequest? SongEditRequest { get; set; }
    public Guid? GigId { get; set; }
    public Gig? Gig { get; set; }
    public Guid? RehearsalId { get; set; }
    public Rehearsal? Rehearsal { get; set; }

    // The Band this notification is about, when it's about one - every
    // Kind except SongEditReviewed (a SuperAdmin action, shown as
    // "SuperAdmin, Band Manager+" instead) sets this at creation time,
    // so the notification detail view can show "Band Manager+,
    // <Band Name>" rather than nothing. Set directly rather than derived
    // from Gig/Rehearsal at read time since ResponsibilityChanged has no
    // ref of its own to derive it from.
    public Guid? BandId { get; set; }
    public Band? Band { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
