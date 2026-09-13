using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SetMeetingPointRequest(string? MeetingPoint, string? MeetingTime);
public record SetRideOfferRequest(int? SeatsAvailable, string? Note);

/// <summary>
/// D11: trip/ride coordination for a gig - who's driving, and where/when
/// everyone's meeting. Deliberately separate from Trip (Travel.cs), which
/// stays a private per-member mileage log for tax purposes; this is
/// band-wide shared state instead, closer to how Gig itself works than
/// to Travel. Any BandMember can read and write here (no BandAdmin gate)
/// - coordinating a ride is exactly the kind of thing every member needs
/// to both see and update, not something to lock behind admin approval.
/// </summary>
[ApiController]
[Route("/api/ride-coordination")]
[Authorize(Policy = "BandMember")]
public class RideCoordinationController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private async Task<(Guid BandId, IActionResult? Error)> RequireActiveBandAndGigAsync(string gigRef)
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return (default, BadRequest(new { error = "No active band selected." }));
        var gigExists = await db.Gigs.AsNoTracking().AnyAsync(g => g.BandId == bandId && g.Ref == gigRef);
        if (!gigExists) return (default, NotFound(new { error = "Gig not found" }));
        return (bandId.Value, null);
    }

    [HttpGet("{gigRef}")]
    public async Task<IActionResult> Get(string gigRef)
    {
        var (bandId, err) = await RequireActiveBandAndGigAsync(gigRef);
        if (err is not null) return err;

        var meetingPoint = await db.GigMeetingPoints.AsNoTracking()
            .Where(m => m.BandId == bandId && m.GigRef == gigRef)
            .Select(m => new { m.MeetingPoint, m.MeetingTime })
            .FirstOrDefaultAsync();

        var drivers = await db.GigRideOffers.AsNoTracking()
            .Where(r => r.BandId == bandId && r.GigRef == gigRef)
            .Include(r => r.User)
            .OrderBy(r => r.UpdatedAt)
            .Select(r => new { userId = r.UserId, name = r.User.DisplayName, r.SeatsAvailable, r.Note })
            .ToListAsync();

        return Ok(new { meetingPoint = meetingPoint?.MeetingPoint, meetingTime = meetingPoint?.MeetingTime, drivers });
    }

    [HttpPut("{gigRef}/meeting-point")]
    public async Task<IActionResult> SetMeetingPoint(string gigRef, [FromBody] SetMeetingPointRequest request)
    {
        var (bandId, err) = await RequireActiveBandAndGigAsync(gigRef);
        if (err is not null) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var row = await db.GigMeetingPoints.FirstOrDefaultAsync(m => m.BandId == bandId && m.GigRef == gigRef);
        var point = string.IsNullOrWhiteSpace(request.MeetingPoint) ? null : request.MeetingPoint.Trim();
        var time = string.IsNullOrWhiteSpace(request.MeetingTime) ? null : request.MeetingTime.Trim();
        if (row is null)
        {
            db.GigMeetingPoints.Add(new GigMeetingPoint
            {
                BandId = bandId, GigRef = gigRef, MeetingPoint = point, MeetingTime = time, UpdatedByUserId = userId.Value
            });
        }
        else
        {
            row.MeetingPoint = point;
            row.MeetingTime = time;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = userId.Value;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, meetingPoint = point, meetingTime = time });
    }

    // Upserts the CALLER's own driving offer - there's no "set someone
    // else's" path, matching Availability's own "you can only touch your
    // own row" rule for a plain member (no BandAdmin override needed
    // here at all, since offering a ride is a personal choice, not
    // something a Band Admin should be setting on someone else's behalf).
    [HttpPut("{gigRef}/driving")]
    public async Task<IActionResult> SetDriving(string gigRef, [FromBody] SetRideOfferRequest request)
    {
        var (bandId, err) = await RequireActiveBandAndGigAsync(gigRef);
        if (err is not null) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (request.SeatsAvailable is < 0) return BadRequest(new { error = "Seats available can't be negative." });

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var offer = await db.GigRideOffers.FirstOrDefaultAsync(r => r.BandId == bandId && r.GigRef == gigRef && r.UserId == userId);
        if (offer is null)
        {
            db.GigRideOffers.Add(new GigRideOffer
            {
                BandId = bandId, GigRef = gigRef, UserId = userId.Value, SeatsAvailable = request.SeatsAvailable, Note = note
            });
        }
        else
        {
            offer.SeatsAvailable = request.SeatsAvailable;
            offer.Note = note;
            offer.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{gigRef}/driving")]
    public async Task<IActionResult> ClearDriving(string gigRef)
    {
        var (bandId, err) = await RequireActiveBandAndGigAsync(gigRef);
        if (err is not null) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var offer = await db.GigRideOffers.FirstOrDefaultAsync(r => r.BandId == bandId && r.GigRef == gigRef && r.UserId == userId);
        if (offer is not null)
        {
            db.GigRideOffers.Remove(offer);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
