using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Hubs;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BandManager.Web.Controllers;

public record CreateSideChatRequest(List<Guid> MemberUserIds, string? Name);
public record RenameThreadRequest(string Name);
public record AddMemberRequest(Guid UserId);
public record SendMessageRequest(string? Text, List<Guid>? MentionedUserIds);
public record SetReactionRequest(string? Emoji);
public record MarkReadRequest(Guid LastReadMessageId);
public record SaveToCatalogRequest(string Name, bool ReplaceExisting = false);

/// <summary>
/// Thin HTTP translation layer over IChatService (see its own doc comment
/// for why the actual logic lives there, not here) - every action here is
/// model-bind, call the service, push the result via IHubContext&lt;ChatHub&gt;,
/// return it. RequireActiveBand/User.GetUserId() follow the exact same
/// shape every other band-scoped controller (e.g. ExpensesController)
/// already uses.
/// </summary>
[ApiController]
[Route("/api/chat")]
[Authorize(Policy = "BandMember")]
public class ChatController(IChatService chat, IActiveBandAccessor activeBand, IHubContext<ChatHub> hub) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private async Task PushToThreadAsync(Guid threadId, string method, object arg) =>
        await hub.Clients.Group($"thread-{threadId}").SendAsync(method, arg);

    private async Task PushToUserAsync(Guid userId, string method, object arg) =>
        await hub.Clients.User(userId.ToString()).SendAsync(method, arg);

    [HttpGet("threads")]
    public async Task<IActionResult> GetOpenThreads()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await chat.GetMyThreadsAsync(bandId, userId.Value, isTabOpen: true));
    }

    [HttpGet("threads/closed")]
    public async Task<IActionResult> GetClosedThreads()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await chat.GetMyThreadsAsync(bandId, userId.Value, isTabOpen: false));
    }

    [HttpPost("threads")]
    public async Task<IActionResult> CreateSideChat([FromBody] CreateSideChatRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (request.MemberUserIds is not { Count: > 0 }) return BadRequest(new { error = "Pick at least one band member." });

        var thread = await chat.CreateSideChatAsync(bandId, userId.Value, request.MemberUserIds, request.Name);
        foreach (var m in thread.Members.Where(m => m.UserId != userId))
            await PushToUserAsync(m.UserId, "ThreadCreated", thread);
        return Ok(thread);
    }

    [HttpPut("threads/{id:guid}/name")]
    public async Task<IActionResult> RenameThread(Guid id, [FromBody] RenameThreadRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });

        var ok = await chat.RenameThreadAsync(id, userId.Value, request.Name);
        if (!ok) return NotFound(new { error = "Not found" });
        var thread = await chat.GetThreadAsync(id, userId.Value);
        await PushToThreadAsync(id, "ThreadUpdated", thread!);
        return Ok(thread);
    }

    [HttpPost("threads/{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var thread = await chat.AddMemberAsync(id, userId.Value, request.UserId);
        if (thread is null) return NotFound(new { error = "Not found" });
        await PushToThreadAsync(id, "ThreadUpdated", thread);
        await PushToUserAsync(request.UserId, "ThreadCreated", thread);
        return Ok(thread);
    }

    [HttpPost("threads/{id:guid}/close")]
    public async Task<IActionResult> CloseTab(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var ok = await chat.CloseTabAsync(id, userId.Value);
        if (!ok) return BadRequest(new { error = "That tab can't be closed." });
        return Ok(new { ok = true });
    }

    [HttpPost("threads/{id:guid}/reopen")]
    public async Task<IActionResult> ReopenTab(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var thread = await chat.ReopenTabAsync(id, userId.Value);
        if (thread is null) return NotFound(new { error = "Not found" });
        return Ok(thread);
    }

    [HttpGet("threads/{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, [FromQuery] Guid? before, [FromQuery] int take = 50)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await chat.GetMessagesAsync(id, userId.Value, before, take));
    }

    [HttpPost("threads/{id:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var message = await chat.SendMessageAsync(id, userId.Value, request.Text, request.MentionedUserIds);
        if (message is null) return NotFound(new { error = "Not found" });
        await PushToThreadAsync(id, "MessageReceived", message);
        return Ok(message);
    }

    [HttpPost("messages/{id:guid}/attachments")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> AddAttachment(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var message = await chat.AddAttachmentAsync(id, userId.Value, ms.ToArray(), file.ContentType, file.FileName);
        if (message is null) return NotFound(new { error = "Not found" });
        await PushToThreadAsync(message.ThreadId, "MessageReceived", message);
        return Ok(message);
    }

    [HttpDelete("messages/{id:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var ok = await chat.DeleteMessageAsync(id, userId.Value);
        if (!ok) return NotFound(new { error = "Not found" });
        return Ok(new { ok = true });
    }

    [HttpPut("messages/{id:guid}/reactions")]
    public async Task<IActionResult> SetReaction(Guid id, [FromBody] SetReactionRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var message = await chat.SetReactionAsync(id, userId.Value, request.Emoji);
        if (message is null) return NotFound(new { error = "Not found" });
        await PushToThreadAsync(message.ThreadId, "ReactionChanged", message);
        return Ok(message);
    }

    [HttpDelete("messages/{id:guid}/reactions")]
    public async Task<IActionResult> ClearReaction(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var message = await chat.SetReactionAsync(id, userId.Value, null);
        if (message is null) return NotFound(new { error = "Not found" });
        await PushToThreadAsync(message.ThreadId, "ReactionChanged", message);
        return Ok(message);
    }

    [HttpPut("threads/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, [FromBody] MarkReadRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        await chat.MarkReadAsync(id, userId.Value, request.LastReadMessageId);
        await PushToThreadAsync(id, "MessageRead", new { threadId = id, userId = userId.Value, lastReadMessageId = request.LastReadMessageId });
        return Ok(new { ok = true });
    }

    [HttpGet("threads/{id:guid}/read-state")]
    public async Task<IActionResult> GetReadState(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!await chat.IsMemberAsync(id, userId.Value)) return NotFound(new { error = "Not found" });
        return Ok(await chat.GetReadStateAsync(id));
    }

    [HttpPost("attachments/{id:guid}/save-to-catalog")]
    public async Task<IActionResult> SaveToCatalog(Guid id, [FromBody] SaveToCatalogRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "A name is required." });

        try
        {
            var catalogItemId = await chat.SaveAttachmentToCatalogAsync(id, userId.Value, request.Name, request.ReplaceExisting);
            if (catalogItemId is null) return NotFound(new { error = "Not found" });
            return Ok(new { ok = true, catalogItemId });
        }
        catch (CatalogDuplicateContentException ex)
        {
            return BadRequest(new { error = ex.Message, duplicateContent = true, existingId = ex.ExistingId, existingName = ex.ExistingName });
        }
        catch (CatalogDuplicateNameException ex)
        {
            return BadRequest(new { error = ex.Message, duplicateName = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(new { count = await chat.GetUnreadCountAsync(bandId, userId.Value) });
    }
}
