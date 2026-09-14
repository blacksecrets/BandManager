using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Services;

public class ChatService(ApplicationDbContext db, CatalogStore catalogStore, string chatAttachmentsRootPath) : IChatService
{
    private const int MaxTextLength = 4000;

    private static string AvatarUrl(ApplicationUser u) => u.AvatarFileName is { } fn ? $"/avatars/{fn}" : "";

    private static ChatMemberDto ToMemberDto(ApplicationUser u) =>
        new(u.Id, u.DisplayName, u.AvatarFileName is { } fn ? $"/avatars/{fn}" : null);

    // The single lazy-create path for a Band's one BandWide thread - see
    // Chat.cs's doc comment. The filtered unique index on (BandId) where
    // Type=BandWide is the race-condition backstop; a genuinely concurrent
    // first-touch from two different members just means one insert loses
    // and this refetches the winner's row instead of erroring.
    private async Task<ChatThread> EnsureBandWideThreadAsync(Guid bandId)
    {
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.BandId == bandId && t.Type == ChatThreadType.BandWide);
        if (thread is not null) return thread;

        thread = new ChatThread { BandId = bandId, Type = ChatThreadType.BandWide };
        db.ChatThreads.Add(thread);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.Entry(thread).State = EntityState.Detached;
            thread = await db.ChatThreads.FirstAsync(t => t.BandId == bandId && t.Type == ChatThreadType.BandWide);
        }
        return thread;
    }

    // Every band member automatically "has" the BandWide thread open the
    // first time anything touches chat for them (matches "Full Band" being
    // the default, always-there tab - not something anyone has to join).
    // SideChat membership is never auto-created here - see CreateSideChatAsync/
    // AddMemberAsync for the explicit paths that actually add someone.
    private async Task<ChatThreadMember> EnsureMemberRowAsync(Guid threadId, Guid userId, bool defaultTabOpen = true)
    {
        var member = await db.ChatThreadMembers.FirstOrDefaultAsync(m => m.ThreadId == threadId && m.UserId == userId);
        if (member is not null) return member;

        var nextSort = await db.ChatThreadMembers.Where(m => m.UserId == userId)
            .Select(m => (int?)m.TabSortOrder).MaxAsync() ?? -1;
        member = new ChatThreadMember { ThreadId = threadId, UserId = userId, IsTabOpen = defaultTabOpen, TabSortOrder = nextSort + 1 };
        db.ChatThreadMembers.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    public async Task<bool> IsMemberAsync(Guid threadId, Guid userId) =>
        await db.ChatThreadMembers.AnyAsync(m => m.ThreadId == threadId && m.UserId == userId);

    private async Task<ChatThreadDto> BuildThreadDtoAsync(ChatThread thread, Guid forUserId)
    {
        var members = await db.ChatThreadMembers.AsNoTracking().Include(m => m.User)
            .Where(m => m.ThreadId == thread.Id).ToListAsync();
        var myMembership = members.FirstOrDefault(m => m.UserId == forUserId);

        var displayName = thread.Type == ChatThreadType.BandWide
            ? "Full Band"
            : thread.Name ?? (members.Count == 0 ? "(no members)" : string.Join(", ",
                members.Where(m => m.UserId != forUserId).Select(m => m.User.DisplayName)
                    .DefaultIfEmpty(members[0].User.DisplayName)));

        var lastMessage = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ThreadId == thread.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m.Text, m.IsDeleted, m.CreatedAt, HasAttachment = db.ChatMessageAttachments.Any(a => a.MessageId == m.Id) })
            .FirstOrDefaultAsync();

        var unread = 0;
        if (myMembership is not null)
        {
            unread = await db.ChatMessages.CountAsync(m => m.ThreadId == thread.Id && !m.IsDeleted
                && m.SenderId != forUserId
                && (myMembership.LastReadAt == null || m.CreatedAt > myMembership.LastReadAt));
        }

        string? preview = lastMessage is null ? null
            : lastMessage.IsDeleted ? "Message deleted"
            : !string.IsNullOrEmpty(lastMessage.Text) ? lastMessage.Text
            : lastMessage.HasAttachment ? "📎 Attachment" : null;

        return new ChatThreadDto(
            thread.Id, thread.Type.ToString(), thread.Name, displayName,
            members.Select(m => ToMemberDto(m.User)).ToList(),
            myMembership?.IsTabOpen ?? false, unread, preview, lastMessage?.CreatedAt);
    }

    public async Task<ChatThreadDto> GetOrCreateBandWideThreadAsync(Guid bandId, Guid userId)
    {
        var thread = await EnsureBandWideThreadAsync(bandId);
        await EnsureMemberRowAsync(thread.Id, userId);
        return await BuildThreadDtoAsync(thread, userId);
    }

    public async Task<List<ChatThreadDto>> GetMyThreadsAsync(Guid bandId, Guid userId, bool isTabOpen)
    {
        await GetOrCreateBandWideThreadAsync(bandId, userId);

        var threadIds = await db.ChatThreadMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.IsTabOpen == isTabOpen && m.Thread.BandId == bandId)
            .OrderBy(m => m.Thread.Type).ThenBy(m => m.TabSortOrder)
            .Select(m => m.ThreadId)
            .ToListAsync();

        var threads = await db.ChatThreads.AsNoTracking().Where(t => threadIds.Contains(t.Id)).ToListAsync();
        var byId = threads.ToDictionary(t => t.Id);
        var result = new List<ChatThreadDto>();
        foreach (var id in threadIds)
        {
            if (byId.TryGetValue(id, out var t)) result.Add(await BuildThreadDtoAsync(t, userId));
        }
        return result;
    }

    public async Task<ChatThreadDto?> GetThreadAsync(Guid threadId, Guid userId)
    {
        if (!await IsMemberAsync(threadId, userId)) return null;
        var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId);
        return thread is null ? null : await BuildThreadDtoAsync(thread, userId);
    }

    public async Task<ChatThreadDto> CreateSideChatAsync(Guid bandId, Guid creatorUserId, List<Guid> memberUserIds, string? name)
    {
        var thread = new ChatThread
        {
            BandId = bandId,
            Type = ChatThreadType.SideChat,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            CreatedByUserId = creatorUserId
        };
        db.ChatThreads.Add(thread);
        await db.SaveChangesAsync();

        // The creator is always a member, unconditionally - a SideChat you
        // started but aren't in would be an odd, hard-to-find state. This
        // has to bypass the BandMemberships check below: a SuperAdmin has
        // no membership row of their own (they pass the controller's
        // BandMember policy via its own bypass instead), so requiring one
        // here silently dropped the creator whenever they were a
        // SuperAdmin - caught live when a SuperAdmin's own new SideChat
        // never showed up in their own tab bar.
        await EnsureMemberRowAsync(thread.Id, creatorUserId);

        var otherIds = memberUserIds.Where(id => id != creatorUserId).Distinct();
        var validOtherIds = await db.BandMemberships.Where(m => m.BandId == bandId && otherIds.Contains(m.UserId))
            .Select(m => m.UserId).ToListAsync();
        foreach (var uid in validOtherIds) await EnsureMemberRowAsync(thread.Id, uid);

        return await BuildThreadDtoAsync(thread, creatorUserId);
    }

    public async Task<bool> RenameThreadAsync(Guid threadId, Guid userId, string name)
    {
        if (!await IsMemberAsync(threadId, userId)) return false;
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == threadId && t.Type == ChatThreadType.SideChat);
        if (thread is null) return false; // BandWide is never renamed - always "Full Band"

        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        thread.Name = trimmed;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<ChatThreadDto?> AddMemberAsync(Guid threadId, Guid actingUserId, Guid newMemberUserId)
    {
        if (!await IsMemberAsync(threadId, actingUserId)) return null;
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == threadId && t.Type == ChatThreadType.SideChat);
        if (thread is null) return null;

        var isRealBandMember = await db.BandMemberships.AnyAsync(m => m.BandId == thread.BandId && m.UserId == newMemberUserId);
        if (!isRealBandMember) return null;

        await EnsureMemberRowAsync(threadId, newMemberUserId);
        return await BuildThreadDtoAsync(thread, actingUserId);
    }

    public async Task<bool> CloseTabAsync(Guid threadId, Guid userId)
    {
        var member = await db.ChatThreadMembers.Include(m => m.Thread)
            .FirstOrDefaultAsync(m => m.ThreadId == threadId && m.UserId == userId);
        if (member is null || member.Thread.Type == ChatThreadType.BandWide) return false; // Full Band is never closeable

        member.IsTabOpen = false;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<ChatThreadDto?> ReopenTabAsync(Guid threadId, Guid userId)
    {
        var member = await db.ChatThreadMembers.Include(m => m.Thread)
            .FirstOrDefaultAsync(m => m.ThreadId == threadId && m.UserId == userId);
        if (member is null) return null;

        member.IsTabOpen = true;
        await db.SaveChangesAsync();
        return await BuildThreadDtoAsync(member.Thread, userId);
    }

    private static ChatMessageDto ToMessageDto(ChatMessage m, List<ChatMessageAttachment> attachments, List<ChatReaction> reactions, List<Guid> mentions) => new(
        m.Id, m.ThreadId, m.SenderId, m.Sender.DisplayName, AvatarUrl(m.Sender) is { Length: > 0 } url ? url : null,
        m.IsDeleted ? null : m.Text, m.CreatedAt, m.IsDeleted,
        m.IsDeleted ? [] : attachments.Select(a => new ChatAttachmentDto(a.Id, $"/chat-attachments/{a.FileName}", a.OriginalFileName, a.ContentType, a.SizeBytes, a.IsImage)).ToList(),
        m.IsDeleted ? [] : reactions.Select(r => new ChatReactionDto(r.UserId, r.User.DisplayName, r.Emoji)).ToList(),
        m.IsDeleted ? [] : mentions);

    public async Task<List<ChatMessageDto>> GetMessagesAsync(Guid threadId, Guid userId, Guid? beforeMessageId, int take)
    {
        if (!await IsMemberAsync(threadId, userId)) return [];
        take = Math.Clamp(take, 1, 200);

        var query = db.ChatMessages.AsNoTracking().Include(m => m.Sender).Where(m => m.ThreadId == threadId);
        if (beforeMessageId is { } beforeId)
        {
            var beforeCreatedAt = await db.ChatMessages.Where(m => m.Id == beforeId).Select(m => m.CreatedAt).FirstOrDefaultAsync();
            query = query.Where(m => m.CreatedAt < beforeCreatedAt);
        }

        var page = await query.OrderByDescending(m => m.CreatedAt).Take(take).ToListAsync();
        page.Reverse(); // oldest-first within the page, for straightforward rendering

        var ids = page.Select(m => m.Id).ToList();
        var attachmentsByMessage = (await db.ChatMessageAttachments.AsNoTracking().Where(a => ids.Contains(a.MessageId)).ToListAsync())
            .GroupBy(a => a.MessageId).ToDictionary(g => g.Key, g => g.ToList());
        var reactionsByMessage = (await db.ChatReactions.AsNoTracking().Include(r => r.User).Where(r => ids.Contains(r.MessageId)).ToListAsync())
            .GroupBy(r => r.MessageId).ToDictionary(g => g.Key, g => g.ToList());
        var mentionsByMessage = (await db.ChatMessageMentions.AsNoTracking().Where(m => ids.Contains(m.MessageId)).ToListAsync())
            .GroupBy(m => m.MessageId).ToDictionary(g => g.Key, g => g.Select(m => m.MentionedUserId).ToList());

        return page.Select(m => ToMessageDto(m,
            attachmentsByMessage.GetValueOrDefault(m.Id, []),
            reactionsByMessage.GetValueOrDefault(m.Id, []),
            mentionsByMessage.GetValueOrDefault(m.Id, []))).ToList();
    }

    public async Task<ChatMessageDto?> SendMessageAsync(Guid threadId, Guid senderId, string? text, List<Guid>? mentionedUserIds)
    {
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == threadId);
        if (thread is null || !await IsMemberAsync(threadId, senderId)) return null;

        var trimmedText = text?.Trim();
        if (string.IsNullOrEmpty(trimmedText)) trimmedText = null;
        if (trimmedText is { Length: > MaxTextLength }) trimmedText = trimmedText[..MaxTextLength];

        var message = new ChatMessage { ThreadId = threadId, SenderId = senderId, Text = trimmedText };
        db.ChatMessages.Add(message);

        // Sending advances your own read cursor too - you've necessarily
        // "seen" a message you just wrote, same as any chat app.
        var senderMember = await EnsureMemberRowAsync(threadId, senderId);
        senderMember.LastReadMessageId = message.Id;
        senderMember.LastReadAt = message.CreatedAt;

        var mentionRows = new List<Guid>();
        if (mentionedUserIds is { Count: > 0 })
        {
            var validMentionIds = await db.ChatThreadMembers
                .Where(m => m.ThreadId == threadId && mentionedUserIds.Contains(m.UserId) && m.UserId != senderId)
                .Select(m => m.UserId).ToListAsync();
            foreach (var uid in validMentionIds)
            {
                db.ChatMessageMentions.Add(new ChatMessageMention { MessageId = message.Id, MentionedUserId = uid });
                mentionRows.Add(uid);
            }
        }
        await db.SaveChangesAsync();

        if (mentionRows.Count > 0)
        {
            var senderName = await db.Users.Where(u => u.Id == senderId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Someone";
            var threadLabel = (await GetThreadAsync(threadId, senderId))?.DisplayName ?? "chat";
            var snippet = trimmedText is { Length: > 0 } ? (trimmedText.Length > 120 ? trimmedText[..120] + "…" : trimmedText) : "(attachment)";
            foreach (var uid in mentionRows)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = uid, BandId = thread.BandId, Kind = NotificationKind.ChatMention, ChatMessageId = message.Id,
                    Message = $"{senderName} mentioned you in {threadLabel}: {snippet}"
                });
            }
            await db.SaveChangesAsync();
        }

        await db.Entry(message).Reference(m => m.Sender).LoadAsync();
        return ToMessageDto(message, [], [], mentionRows);
    }

    public async Task<ChatMessageDto?> AddAttachmentAsync(Guid messageId, Guid userId, byte[] bytes, string contentType, string? originalFileName)
    {
        var message = await db.ChatMessages.Include(m => m.Sender).FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null || message.SenderId != userId) return null;

        var ext = originalFileName is { Length: > 0 } ? Path.GetExtension(originalFileName) : "";
        if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".bin";
        var fileName = $"{message.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";

        // The row was being created with zero bytes ever written to disk -
        // caught live via a 404 on the resulting /chat-attachments/... URL
        // the very first time an image was actually uploaded through the
        // UI (every earlier check exercised the DB/DTO side only).
        Directory.CreateDirectory(chatAttachmentsRootPath);
        await File.WriteAllBytesAsync(Path.Combine(chatAttachmentsRootPath, fileName), bytes);

        var attachment = new ChatMessageAttachment
        {
            MessageId = message.Id, FileName = fileName, OriginalFileName = originalFileName,
            ContentType = contentType, SizeBytes = bytes.Length,
            IsImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        };
        db.ChatMessageAttachments.Add(attachment);
        await db.SaveChangesAsync();

        var reactions = await db.ChatReactions.AsNoTracking().Include(r => r.User).Where(r => r.MessageId == messageId).ToListAsync();
        var mentions = await db.ChatMessageMentions.AsNoTracking().Where(m => m.MessageId == messageId).Select(m => m.MentionedUserId).ToListAsync();
        return ToMessageDto(message, [attachment], reactions, mentions);
    }

    public async Task<bool> DeleteMessageAsync(Guid messageId, Guid userId)
    {
        var message = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null || message.SenderId != userId) return false;

        message.IsDeleted = true;
        message.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<ChatMessageDto?> SetReactionAsync(Guid messageId, Guid userId, string? emoji)
    {
        var message = await db.ChatMessages.Include(m => m.Sender).FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null || !await IsMemberAsync(message.ThreadId, userId)) return null;

        var existing = await db.ChatReactions.FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId);
        if (string.IsNullOrEmpty(emoji))
        {
            if (existing is not null) db.ChatReactions.Remove(existing);
        }
        else if (existing is not null)
        {
            existing.Emoji = emoji;
        }
        else
        {
            db.ChatReactions.Add(new ChatReaction { MessageId = messageId, UserId = userId, Emoji = emoji });
        }
        await db.SaveChangesAsync();

        var attachments = await db.ChatMessageAttachments.AsNoTracking().Where(a => a.MessageId == messageId).ToListAsync();
        var reactions = await db.ChatReactions.AsNoTracking().Include(r => r.User).Where(r => r.MessageId == messageId).ToListAsync();
        var mentions = await db.ChatMessageMentions.AsNoTracking().Where(m => m.MessageId == messageId).Select(m => m.MentionedUserId).ToListAsync();
        return ToMessageDto(message, attachments, reactions, mentions);
    }

    public async Task MarkReadAsync(Guid threadId, Guid userId, Guid lastReadMessageId)
    {
        var messageInThread = await db.ChatMessages.AnyAsync(m => m.Id == lastReadMessageId && m.ThreadId == threadId);
        if (!messageInThread) return;

        var member = await EnsureMemberRowAsync(threadId, userId);
        var message = await db.ChatMessages.FirstAsync(m => m.Id == lastReadMessageId);
        // Never move the cursor backwards - a stale/out-of-order client
        // call shouldn't un-read something already marked read.
        if (member.LastReadAt is { } existing && existing >= message.CreatedAt) return;

        member.LastReadMessageId = lastReadMessageId;
        member.LastReadAt = message.CreatedAt;
        await db.SaveChangesAsync();
    }

    public async Task<List<ChatReadStateDto>> GetReadStateAsync(Guid threadId)
    {
        var members = await db.ChatThreadMembers.AsNoTracking().Include(m => m.User).Where(m => m.ThreadId == threadId).ToListAsync();
        return members.Select(m => new ChatReadStateDto(m.UserId, m.User.DisplayName, m.LastReadAt)).ToList();
    }

    public async Task<Guid?> SaveAttachmentToCatalogAsync(Guid attachmentId, Guid userId, string name, bool replaceExisting = false)
    {
        var attachment = await db.ChatMessageAttachments.Include(a => a.Message).ThenInclude(m => m.Thread)
            .FirstOrDefaultAsync(a => a.Id == attachmentId);
        if (attachment is null || !attachment.IsImage) return null;
        if (!await IsMemberAsync(attachment.Message.ThreadId, userId)) return null;

        var bandId = attachment.Message.Thread.BandId;
        var trimmedName = name.Trim();
        if (string.IsNullOrEmpty(trimmedName)) throw new InvalidOperationException("A name is required.");

        var filePath = Path.Combine(chatAttachmentsRootPath, attachment.FileName);
        if (!File.Exists(filePath)) return null;
        var bytes = await File.ReadAllBytesAsync(filePath);

        // Content match takes priority over a name match - telling someone
        // "this exact picture is already in here as X" is more useful than
        // a name collision they may not have meant, and the fix is
        // different too (rename the existing item, not replace its file -
        // there's nothing to replace when the bytes already match).
        var contentMatch = await catalogStore.FindByContentHashAsync(bandId, CatalogStore.ComputeContentHash(bytes));
        if (contentMatch is not null)
            throw new CatalogDuplicateContentException(contentMatch.Id, contentMatch.Label ?? contentMatch.OriginalFilename ?? "(unnamed)");

        var existingMatch = await db.CatalogItems.FirstOrDefaultAsync(c => c.BandId == bandId && c.Label != null && EF.Functions.ILike(c.Label, trimmedName));
        if (existingMatch is not null && !replaceExisting)
            throw new CatalogDuplicateNameException(trimmedName);

        var uploadedByName = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync();

        var item = existingMatch is not null
            ? await catalogStore.ReplaceCatalogItemFileAsync(existingMatch, bytes, attachment.ContentType, attachment.OriginalFileName, CatalogSource.ChatImage, uploadedByName)
            : await catalogStore.RegisterCatalogItemAsync(bandId, bytes, attachment.ContentType, attachment.OriginalFileName, CatalogSource.ChatImage, sourceUrl: null, uploadedBy: uploadedByName, label: trimmedName);
        return item.Id;
    }

    public async Task<int> GetUnreadCountAsync(Guid bandId, Guid userId)
    {
        await GetOrCreateBandWideThreadAsync(bandId, userId);

        var myMemberships = await db.ChatThreadMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Thread.BandId == bandId)
            .Select(m => new { m.ThreadId, m.LastReadAt })
            .ToListAsync();

        var total = 0;
        foreach (var m in myMemberships)
        {
            total += await db.ChatMessages.CountAsync(msg => msg.ThreadId == m.ThreadId && !msg.IsDeleted
                && msg.SenderId != userId && (m.LastReadAt == null || msg.CreatedAt > m.LastReadAt));
        }
        return total;
    }
}
