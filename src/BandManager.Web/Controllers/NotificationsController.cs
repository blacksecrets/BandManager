using BandManager.Data;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record NotificationBulkIdsRequest(List<Guid> Ids);

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
public class NotificationsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var notifications = await db.Notifications.AsNoTracking()
            .Include(n => n.SongEditRequest).ThenInclude(r => r!.Song)
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                id = n.Id,
                message = n.Message,
                isRead = n.IsRead,
                createdAt = n.CreatedAt,
                songTitle = n.SongEditRequest != null ? n.SongEditRequest.Song.Title : null
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
