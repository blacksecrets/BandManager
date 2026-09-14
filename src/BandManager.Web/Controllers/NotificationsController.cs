using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record NotificationBulkIdsRequest(List<Guid> Ids);
public record SendBroadcastRequest(string Message);

/// <summary>
/// A generic per-user inbox - deliberately not song-specific, even though
/// today the only writer is SongEditRequestsController's approve/reject,
/// so a future notification type doesn't need its own separate bell/page.
/// SuperAdmin's "pending reviews" queue count is NOT read from here - see
/// SongEditRequestsController.PendingCount's doc comment for why.
/// </summary>
[ApiController]
[Route("/api/notifications")]
[Authorize]
public class NotificationsController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    // D14: a BandAdmin/SuperAdmin's "tell everyone at once" tool - every
    // other Notification here is system-generated off some other event
    // (a song reviewed, a gig reminder); this is the one kind a person
    // writes and sends directly. Reuses the exact same per-user inbox
    // every other notification already uses, rather than a separate
    // announcements table/UI - the whole point of Kind existing on
    // Notification in the first place (see the class doc comment).
    [HttpPost("broadcast")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SendBroadcast([FromBody] SendBroadcastRequest request)
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return BadRequest(new { error = "No active band selected." });
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "Message is required." });
        if (message.Length > 2000) return BadRequest(new { error = "Message is too long (2000 characters max)." });

        var senderId = User.GetUserId();
        if (senderId is null) return Unauthorized();
        var senderName = await db.Users.Where(u => u.Id == senderId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "A Band Admin";

        var memberIds = await db.BandMemberships.Where(m => m.BandId == bandId).Select(m => m.UserId).ToListAsync();
        foreach (var memberId in memberIds)
        {
            // Sent to the admin's own inbox too, same as everyone else's -
            // it's a record of what was announced and when, not just a
            // one-way push to other people. Sender name prefixed into the
            // message itself since Notification has no separate "from"
            // field to render one - every other Kind here is
            // system-generated and doesn't need one.
            db.Notifications.Add(new Notification
            {
                UserId = memberId, BandId = bandId, Kind = NotificationKind.Broadcast, Message = $"{senderName}: {message}"
            });
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, recipientCount = memberIds.Count });
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var notifications = await db.Notifications.AsNoTracking()
            .Include(n => n.SongEditRequest).ThenInclude(r => r!.Song)
            .Include(n => n.Band)
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                id = n.Id,
                message = n.Message,
                isRead = n.IsRead,
                createdAt = n.CreatedAt,
                songTitle = n.SongEditRequest != null ? n.SongEditRequest.Song.Title : null,
                // A SongEditReviewed notification is a SuperAdmin action,
                // not really "from" any one Band - every other Kind is
                // written with a BandId at creation time (see
                // NotificationReminderService/ScheduleItemsController),
                // so this only falls through to the bare "Band Manager+"
                // for one that somehow has neither.
                fromLabel = n.Kind == NotificationKind.SongEditReviewed
                    ? "SuperAdmin, Band Manager+"
                    : n.Kind == NotificationKind.ChatMention
                    ? "Band Chat" + (n.Band != null ? $", {n.Band.Name}" : "")
                    : n.Band != null ? $"Band Manager+, {n.Band.Name}" : "Band Manager+"
            })
            .ToListAsync();
        return Ok(notifications);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var count = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        return Ok(new { count });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notification is null) return NotFound(new { error = "Not found" });

        notification.IsRead = true;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("{id:guid}/unread")]
    public async Task<IActionResult> MarkUnread(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notification is null) return NotFound(new { error = "Not found" });

        notification.IsRead = false;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notification is null) return NotFound(new { error = "Not found" });

        db.Notifications.Remove(notification);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Bulk variants for the DataGrid-based notifications page's checkbox +
    // "do this to every checked row" pattern - same per-user scoping as the
    // single-id actions above (a mismatched id is silently skipped rather
    // than erroring the whole batch, since the grid can't have selected an
    // id it didn't load in the first place).
    [HttpPost("mark-read")]
    public async Task<IActionResult> BulkMarkRead([FromBody] NotificationBulkIdsRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var count = await db.Notifications.Where(n => request.Ids.Contains(n.Id) && n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Ok(new { ok = true, count });
    }

    [HttpPost("mark-unread")]
    public async Task<IActionResult> BulkMarkUnread([FromBody] NotificationBulkIdsRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var count = await db.Notifications.Where(n => request.Ids.Contains(n.Id) && n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, false));
        return Ok(new { ok = true, count });
    }

    [HttpPost("delete")]
    public async Task<IActionResult> BulkDelete([FromBody] NotificationBulkIdsRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var count = await db.Notifications.Where(n => request.Ids.Contains(n.Id) && n.UserId == userId).ExecuteDeleteAsync();
        return Ok(new { ok = true, count });
    }
}
