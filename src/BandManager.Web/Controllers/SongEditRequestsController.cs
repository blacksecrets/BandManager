using BandManager.Data;
using BandManager.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record ResolveSongEditRequestRequest(string Message);
public record BulkResolveSongEditRequestsRequest(List<Guid> Ids, string Message);

/// <summary>
/// The review queue for SongsController.ProposeEdit's staged edits. Reading
/// a single Song's own pending-request summary is open to any Band member
/// (read-only - just enough to explain the "under review" badge); the
/// queue itself and approve/reject are SuperAdmin-only, mirroring
/// RepertoireController's mixed class/action policy shape.
/// </summary>
[ApiController]
[Route("/api/song-edit-requests")]
[Authorize(Policy = "BandMember")]
public class SongEditRequestsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    private static object SerializeDetail(SongEditRequest r) => new
    {
        id = r.Id,
        songId = r.SongId,
        songTitle = r.Song.Title,
        requestedByFirstName = r.RequestedByUser.DisplayName,
        bandName = r.Band.Name,
        changedFields = r.Changes.Select(kv => new { field = kv.Key, oldValue = kv.Value.OldValue, newValue = kv.Value.NewValue }),
        createdAt = r.CreatedAt
    };

    // What a non-SuperAdmin sees when they click a catalog row's "under
    // review" badge - field names only, no old/new values and no
    // accept/reject controls, since only the reviewer needs the full diff.
    [HttpGet("{songId:guid}/summary")]
    public async Task<IActionResult> Summary(Guid songId)
    {
        var request = await db.SongEditRequests.AsNoTracking()
            .Include(r => r.RequestedByUser)
            .Include(r => r.Band)
            .FirstOrDefaultAsync(r => r.SongId == songId && r.Status == EditRequestStatus.Pending);
        if (request is null) return NotFound(new { error = "No edit is under review for this song." });

        return Ok(new
        {
            id = request.Id,
            requestedByFirstName = request.RequestedByUser.DisplayName,
            bandName = request.Band.Name,
            changedFields = request.Changes.Keys,
            createdAt = request.CreatedAt
        });
    }

    [HttpGet("pending")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Pending()
    {
        var requests = await db.SongEditRequests.AsNoTracking()
            .Include(r => r.Song)
            .Include(r => r.RequestedByUser)
            .Include(r => r.Band)
            .Where(r => r.Status == EditRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
        return Ok(requests.Select(SerializeDetail));
    }

    // Cheap payload for the topNav 60s poll - the full Pending() list is
    // only fetched when the notifications page itself is open.
    [HttpGet("pending-count")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> PendingCount()
    {
        var count = await db.SongEditRequests.CountAsync(r => r.Status == EditRequestStatus.Pending);
        return Ok(new { count });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Get(Guid id)
    {
        var request = await db.SongEditRequests.AsNoTracking()
            .Include(r => r.Song)
            .Include(r => r.RequestedByUser)
            .Include(r => r.Band)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound(new { error = "Not found" });
        return Ok(SerializeDetail(request));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ResolveSongEditRequestRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var (ok, error) = await ApproveOneAsync(id, message, user.Id);
        if (!ok) return BadRequest(new { error });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ResolveSongEditRequestRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });
        if (message.Length > 250) return BadRequest(new { error = "Message must be 250 characters or fewer." });

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var (ok, error) = await RejectOneAsync(id, message, user.Id);
        if (!ok) return BadRequest(new { error });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Bulk variants for the DataGrid-based pending-review queue's checkbox
    // selection ("Accept All Checked"/"Reject All Checked") - one shared
    // resolution message applies to every checked row, same requirement
    // (non-empty) as resolving one at a time. A row that's already been
    // resolved by the time this runs (e.g. two SuperAdmins reviewing at
    // once) is skipped rather than failing the whole batch - the response
    // reports how many actually went through.
    [HttpPost("bulk-approve")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> BulkApprove([FromBody] BulkResolveSongEditRequestsRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var count = 0;
        foreach (var id in request.Ids)
        {
            var (ok, _) = await ApproveOneAsync(id, message, user.Id);
            if (ok) count++;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, count });
    }

    [HttpPost("bulk-reject")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> BulkReject([FromBody] BulkResolveSongEditRequestsRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });
        if (message.Length > 250) return BadRequest(new { error = "Message must be 250 characters or fewer." });

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var count = 0;
        foreach (var id in request.Ids)
        {
            var (ok, _) = await RejectOneAsync(id, message, user.Id);
            if (ok) count++;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, count });
    }

    private async Task<(bool Ok, string? Error)> ApproveOneAsync(Guid id, string message, Guid resolvedByUserId)
    {
        var editRequest = await db.SongEditRequests.Include(r => r.Song).FirstOrDefaultAsync(r => r.Id == id);
        if (editRequest is null) return (false, "Not found");
        if (editRequest.Status != EditRequestStatus.Pending) return (false, "Already resolved.");

        var song = editRequest.Song;
        foreach (var (field, change) in editRequest.Changes)
        {
            switch (field)
            {
                case nameof(Song.Title): song.Title = change.NewValue ?? song.Title; break;
                case nameof(Song.OriginalArtist): song.OriginalArtist = change.NewValue; break;
                case nameof(Song.Album): song.Album = change.NewValue; break;
                case nameof(Song.Key): song.Key = change.NewValue; break;
                case nameof(Song.LengthSeconds): song.LengthSeconds = int.TryParse(change.NewValue, out var secs) ? secs : null; break;
                case nameof(Song.YouTubeUrl): song.YouTubeUrl = change.NewValue; break;
                case nameof(Song.SpotifyUrl): song.SpotifyUrl = change.NewValue; break;
                case nameof(Song.SongsterrUrl): song.SongsterrUrl = change.NewValue; break;
            }
        }

        editRequest.Status = EditRequestStatus.Approved;
        editRequest.ResolvedAt = DateTime.UtcNow;
        editRequest.ResolvedByUserId = resolvedByUserId;
        editRequest.ResolutionMessage = message;

        db.Notifications.Add(new Notification
        {
            UserId = editRequest.RequestedByUserId,
            Message = message,
            Kind = NotificationKind.SongEditReviewed,
            SongEditRequestId = editRequest.Id
        });

        return (true, null);
    }

    private async Task<(bool Ok, string? Error)> RejectOneAsync(Guid id, string message, Guid resolvedByUserId)
    {
        var editRequest = await db.SongEditRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (editRequest is null) return (false, "Not found");
        if (editRequest.Status != EditRequestStatus.Pending) return (false, "Already resolved.");

        // The Song itself is never touched - rejecting just clears the
        // under-review state and tells the requester why.
        editRequest.Status = EditRequestStatus.Rejected;
        editRequest.ResolvedAt = DateTime.UtcNow;
        editRequest.ResolvedByUserId = resolvedByUserId;
        editRequest.ResolutionMessage = message;

        db.Notifications.Add(new Notification
        {
            UserId = editRequest.RequestedByUserId,
            Message = message,
            Kind = NotificationKind.SongEditReviewed,
            SongEditRequestId = editRequest.Id
        });

        return (true, null);
    }
}
