namespace BandManager.Data.Entities;

/// <summary>
/// A per-user inbox item - currently only created for a SongEditRequest's
/// approve/reject outcome (see SongEditRequestsController), but
/// deliberately generic (no song-specific required fields) so a future
/// notification type doesn't need its own separate inbox/bell. SuperAdmin's
/// own "pending reviews" badge does NOT use this table - that's a live
/// COUNT over SongEditRequest instead, since it's a shared work queue, not
/// a per-recipient message (see SongEditRequestsController.PendingCount).
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string Message { get; set; }

    // Null for any future non-song-related notification type.
    public Guid? SongEditRequestId { get; set; }
    public SongEditRequest? SongEditRequest { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
