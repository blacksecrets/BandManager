using BandManager.Data;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

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
}
