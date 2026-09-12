namespace BandManager.Data.Entities;

// SongEditReviewed declared first (ordinal 0) deliberately - every
// existing Notification row predates this enum and gets Kind's CLR
// default (0) on migration, and SongEditReviewed is the only kind that's
// ever actually been written so far (SongEditRequestsController), so
// ordinal 0 correctly back-classifies every real row with no data fixup
// needed.
// OutboundEmailCopy appended, not reordered - see the ordinal note above,
// same reasoning applies to every value already in real use.
public enum NotificationKind { SongEditReviewed, GigReminder, RehearsalReminder, AvailabilityReminder, ResponsibilityChanged, OutboundEmailCopy }

/// <summary>
/// One user's notification settings for one NotificationKind - global per
/// user, not per band (a personal habit like "how far ahead do I want
/// reminded of a gig" is unlikely to differ meaningfully band-to-band for
/// the same person; easy to revisit if that assumption turns out wrong).
/// Self-service only (NotificationPreferencesController), mirrors
/// UpdateFirstName's self-only pattern - no BandAdmin-on-behalf editing of
/// someone else's preferences. Missing row for a (User, Kind) pair means
/// "use the default" (both channels on, a sensible default lead time) -
/// see NotificationPreferencesController for the exact defaults, not
/// duplicated here so there's one place to change them.
/// </summary>
public class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public NotificationKind Kind { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public bool InAppEnabled { get; set; } = true;

    // Null for a kind with no lead time - ResponsibilityChanged fires
    // immediately when it happens, there's nothing to count down to.
    public int? LeadTimeDays { get; set; }
}
