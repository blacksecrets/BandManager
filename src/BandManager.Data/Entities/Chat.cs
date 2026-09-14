namespace BandManager.Data.Entities;

public enum ChatThreadType { BandWide, SideChat }

/// <summary>
/// One conversation. Every Band gets exactly one BandWide thread
/// (lazily created on first use - see IChatService.GetOrCreateBandWideThreadAsync),
/// plus any number of user-created SideChat threads with an explicit,
/// editable member list (ChatThreadMember). Name is null for a SideChat
/// until explicitly renamed - the UI auto-labels it from its members'
/// display names until then; a BandWide thread's Name is always null and
/// always shown as "Full Band" client-side.
/// </summary>
public class ChatThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public ChatThreadType Type { get; set; }
    public string? Name { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per (Thread, User) - doing triple duty on purpose, so the whole
/// "closed tabs are reopenable and persist across sessions" requirement and
/// the "Seen by ..." read receipt both fall out of one table instead of
/// two: real membership (can this user read/post here at all), this
/// specific user's own tab-open/closed UI state (IsTabOpen - closing a tab
/// never removes membership or message history access, it only hides the
/// tab), and this user's own read cursor (LastReadMessageId/LastReadAt).
/// For the BandWide thread, a member's row is lazily created the first
/// time they touch chat rather than hooking every band-join code path.
/// </summary>
public class ChatThreadMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public ChatThread Thread { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsTabOpen { get; set; } = true;
    public int TabSortOrder { get; set; }

    public Guid? LastReadMessageId { get; set; }
    public ChatMessage? LastReadMessage { get; set; }
    public DateTime? LastReadAt { get; set; }
}

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public ChatThread Thread { get; set; } = null!;
    public Guid SenderId { get; set; }
    public ApplicationUser Sender { get; set; } = null!;

    // Null when the message is attachment-only (no caption text).
    public string? Text { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete - others who already saw it get a "Message deleted"
    // tombstone instead of the row vanishing out from under a live thread.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class ChatMessageAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public ChatMessage Message { get; set; } = null!;

    public required string FileName { get; set; }
    public string? OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public bool IsImage { get; set; }
}

/// <summary>
/// One user's reaction to one message - at most one emoji per (message,
/// user), replaced on change, matching how Google Messages actually
/// behaves rather than allowing a free-for-all of stacked reactions.
/// </summary>
public class ChatReaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public ChatMessage Message { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public required string Emoji { get; set; }
}

/// <summary>
/// Records a real, structured @mention - created only from the compose
/// box's member-autocomplete picker (a real user id was actually
/// selected), never parsed from free-typed "@name" text. Drives both the
/// bubble's mention highlight and a Notification row (Kind.ChatMention)
/// for the mentioned user.
/// </summary>
public class ChatMessageMention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public ChatMessage Message { get; set; } = null!;
    public Guid MentionedUserId { get; set; }
    public ApplicationUser MentionedUser { get; set; } = null!;
}
