namespace BandManager.Web.Services;

public record ChatMemberDto(Guid UserId, string Name, string? AvatarUrl);

public record ChatThreadDto(
    Guid Id, string Type, string? Name, string DisplayName,
    List<ChatMemberDto> Members, bool IsTabOpen, int UnreadCount,
    string? LastMessagePreview, DateTime? LastMessageAt);

public record ChatAttachmentDto(Guid Id, string Url, string? OriginalFileName, string ContentType, long SizeBytes, bool IsImage);
public record ChatReactionDto(Guid UserId, string UserName, string Emoji);

public record ChatMessageDto(
    Guid Id, Guid ThreadId, Guid SenderId, string SenderName, string? SenderAvatarUrl,
    string? Text, DateTime CreatedAt, bool IsDeleted,
    List<ChatAttachmentDto> Attachments, List<ChatReactionDto> Reactions, List<Guid> MentionedUserIds);

public record ChatReadStateDto(Guid UserId, string UserName, DateTime? LastReadAt);

/// <summary>
/// Thrown by SaveAttachmentToCatalogAsync specifically for a name
/// collision (not any other InvalidOperationException) so the controller
/// can offer "Replace With This" instead of just surfacing a generic
/// error - distinguishing on exception type rather than parsing the
/// message string, which would break the moment the wording changes.
/// </summary>
public class CatalogDuplicateNameException(string name)
    : InvalidOperationException($"A catalog item named \"{name}\" already exists.")
{
    public string Name { get; } = name;
}


/// <summary>
/// All Band Chat business logic - membership/ownership checks, message
/// persistence, the read cursor, attachments, reactions, mentions - lives
/// here, not scattered across ChatController and ChatHub. This is the
/// "mobile-prep" boundary Richard asked for: a future lean mobile-facing
/// API surface (or a native app's own backend-for-frontend) can call this
/// exact interface and get identical behavior/enforcement to the web app,
/// with zero duplicated logic. ChatController is a thin HTTP translation
/// layer over this; ChatHub calls it too for its own membership checks
/// rather than re-implementing them. Every method takes the acting user's
/// id explicitly (never reads it from ambient session/HttpContext state)
/// so it works identically whether called from a controller or a Hub.
/// </summary>
public interface IChatService
{
    Task<ChatThreadDto> GetOrCreateBandWideThreadAsync(Guid bandId, Guid userId);
    // isTabOpen: true = this user's currently-open tabs (for the tab bar,
    // BandWide always included and always first); false = tabs this user
    // has closed but is still a member of (for the "Closed chats" picker).
    Task<List<ChatThreadDto>> GetMyThreadsAsync(Guid bandId, Guid userId, bool isTabOpen);
    Task<ChatThreadDto?> GetThreadAsync(Guid threadId, Guid userId);
    Task<ChatThreadDto> CreateSideChatAsync(Guid bandId, Guid creatorUserId, List<Guid> memberUserIds, string? name);
    Task<bool> RenameThreadAsync(Guid threadId, Guid userId, string name);
    Task<ChatThreadDto?> AddMemberAsync(Guid threadId, Guid actingUserId, Guid newMemberUserId);
    Task<bool> CloseTabAsync(Guid threadId, Guid userId);
    Task<ChatThreadDto?> ReopenTabAsync(Guid threadId, Guid userId);
    Task<bool> IsMemberAsync(Guid threadId, Guid userId);

    Task<List<ChatMessageDto>> GetMessagesAsync(Guid threadId, Guid userId, Guid? beforeMessageId, int take);
    Task<ChatMessageDto?> SendMessageAsync(Guid threadId, Guid senderId, string? text, List<Guid>? mentionedUserIds);
    Task<ChatMessageDto?> AddAttachmentAsync(Guid messageId, Guid userId, byte[] bytes, string contentType, string? originalFileName);
    Task<bool> DeleteMessageAsync(Guid messageId, Guid userId);

    Task<ChatMessageDto?> SetReactionAsync(Guid messageId, Guid userId, string? emoji);

    Task MarkReadAsync(Guid threadId, Guid userId, Guid lastReadMessageId);
    Task<List<ChatReadStateDto>> GetReadStateAsync(Guid threadId);
    Task<int> GetUnreadCountAsync(Guid bandId, Guid userId);

    // Right-click "Add to Band's Catalog" on a chat image - reads the
    // attachment's own bytes off disk and hands them to the existing
    // CatalogStore write path (no chat-specific storage duplication).
    // Returns null for "not found", throws InvalidOperationException for a
    // duplicate name (band-scoped, case-insensitive) UNLESS replaceExisting
    // is true, in which case the existing item's file content is swapped
    // in place instead (same Id, so anything already referencing it - e.g.
    // a Flyer's background - picks up the new image automatically).
    Task<Guid?> SaveAttachmentToCatalogAsync(Guid attachmentId, Guid userId, string name, bool replaceExisting = false);
}
