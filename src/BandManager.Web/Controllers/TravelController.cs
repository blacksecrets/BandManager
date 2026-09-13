using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

// Enum fields come across the wire as strings, parsed with Enum.TryParse
// below - same convention AccountingController's PayoutType already uses,
// since System.Text.Json binds a bare enum property by its numeric value
// by default, not by name.
public record SetTravelCadenceRequest(string Cadence);
public record SetTravelVehicleRequest(string? VehicleMake, string? VehicleModel, int? VehicleYear, int? StartingMileage);
public record TripEndpointInput(bool IsHome, string? Name, string? AddressLine1, string? City, string? State, string? PostalCode);
public record SaveTripRequest(
    DateOnly Date, TripEndpointInput From, TripEndpointInput To, bool RoundTrip,
    string Reason, string? OtherReasonText, string? GigRef);

/// <summary>
/// Profile > My Expenses > Travel - a member's own mileage log. Cadence
/// and vehicle (UserTravelProfile) are global per-user, same as the rest
/// of Profile; Trips are scoped to the active band, same as every other
/// band-scoped write in this app, since a trip's location lookups and gig
/// selector only make sense against one band's data. See Travel.cs's doc
/// comments for why addresses are snapshotted rather than live-linked.
/// </summary>
[ApiController]
[Route("/api/travel")]
[Authorize]
public class TravelController(ApplicationDbContext db, IActiveBandAccessor activeBand, DrivingDistanceService drivingDistance) : ControllerBase
{
    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    // --- Cadence + vehicle (independently savable) ---

    [HttpGet("profile")]
    public async Task<IActionResult> GetTravelProfile()
    {
        var userId = User.GetUserId()!.Value;
        var profile = await db.UserTravelProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        return Ok(new
        {
            cadence = (profile?.Cadence ?? TravelCadence.Yearly).ToString(),
            vehicleMake = profile?.VehicleMake,
            vehicleModel = profile?.VehicleModel,
            vehicleYear = profile?.VehicleYear,
            startingMileage = profile?.StartingMileage
        });
    }

    private async Task<UserTravelProfile> GetOrCreateProfileAsync(Guid userId)
    {
        var profile = await db.UserTravelProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserTravelProfile { UserId = userId };
            db.UserTravelProfiles.Add(profile);
        }
        return profile;
    }

    [HttpPut("cadence")]
    public async Task<IActionResult> SetCadence([FromBody] SetTravelCadenceRequest request)
    {
        if (!Enum.TryParse<TravelCadence>(request.Cadence, ignoreCase: true, out var cadence))
            return BadRequest(new { error = "Invalid cadence." });

        var profile = await GetOrCreateProfileAsync(User.GetUserId()!.Value);
        profile.Cadence = cadence;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("vehicle")]
    public async Task<IActionResult> SetVehicle([FromBody] SetTravelVehicleRequest request)
    {
        var profile = await GetOrCreateProfileAsync(User.GetUserId()!.Value);
        profile.VehicleMake = string.IsNullOrWhiteSpace(request.VehicleMake) ? null : request.VehicleMake.Trim();
        profile.VehicleModel = string.IsNullOrWhiteSpace(request.VehicleModel) ? null : request.VehicleModel.Trim();
        profile.VehicleYear = request.VehicleYear;
        profile.StartingMileage = request.StartingMileage;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- The band's list of locations (trip-address reuse lookup) ---

    // Case-insensitive exact-name match, for the "you've used this name
    // before, reuse its address?" prompt the Trip modal shows before
    // saving a manually-entered (non-home) endpoint.
    [HttpGet("locations/find")]
    public async Task<IActionResult> FindLocation([FromQuery] string name)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        if (string.IsNullOrWhiteSpace(name)) return Ok(new { found = false });

        var trimmed = name.Trim();
        var match = await db.BandLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.BandId == band.Id && l.Name.ToLower() == trimmed.ToLower());
        if (match is null) return Ok(new { found = false });

        return Ok(new
        {
            found = true,
            match.Name,
            match.AddressLine1,
            match.City,
            match.State,
            match.PostalCode
        });
    }

    // --- Trips grid ---

    private static string ComposeAddress(string? line1, string? city, string? state, string? postalCode) =>
        string.Join(", ", new[] { line1, city, state, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private object SerializeTrip(Trip t, Dictionary<string, string> gigTitles) => new
    {
        id = t.Id,
        date = t.Date.ToString("yyyy-MM-dd"),
        fromIsHome = t.FromIsHome,
        fromName = t.FromIsHome ? "Home" : t.FromName,
        fromAddressLine1 = t.FromAddressLine1,
        fromCity = t.FromCity,
        fromState = t.FromState,
        fromPostalCode = t.FromPostalCode,
        toIsHome = t.ToIsHome,
        toName = t.ToIsHome ? "Home" : t.ToName,
        toAddressLine1 = t.ToAddressLine1,
        toCity = t.ToCity,
        toState = t.ToState,
        toPostalCode = t.ToPostalCode,
        distanceMiles = t.DistanceMiles,
        roundTrip = t.RoundTrip,
        totalMiles = t.DistanceMiles is { } d ? Math.Round(d * (t.RoundTrip ? 2 : 1), 1) : (double?)null,
        reason = t.Reason.ToString(),
        otherReasonText = t.OtherReasonText,
        gigRef = t.GigRef,
        reasonDisplay = t.Reason switch
        {
            TripReason.Rehearsal => "Rehearsal",
            TripReason.Gig => t.GigRef is not null && gigTitles.TryGetValue(t.GigRef, out var title) ? title : "Gig",
            _ => t.OtherReasonText ?? "Other"
        }
    };

    [HttpGet("trips")]
    public async Task<IActionResult> ListTrips()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var userId = User.GetUserId()!.Value;

        var trips = await db.Trips.AsNoTracking()
            .Where(t => t.BandId == band.Id && t.UserId == userId)
            .OrderByDescending(t => t.Date).ToListAsync();

        var gigRefs = trips.Where(t => t.Reason == TripReason.Gig && t.GigRef is not null).Select(t => t.GigRef!).Distinct().ToList();
        var gigTitles = gigRefs.Count == 0 ? new Dictionary<string, string>()
            : await db.Gigs.AsNoTracking().Where(g => g.BandId == band.Id && gigRefs.Contains(g.Ref)).ToDictionaryAsync(g => g.Ref, g => g.Title);

        return Ok(trips.Select(t => SerializeTrip(t, gigTitles)));
    }

    // For the gig-reason selector in the Trip modal - active band's gigs,
    // same shape/source as GigSetsController.ListGigs.
    [HttpGet("gigs")]
    public async Task<IActionResult> ListGigsForSelector()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gigs = await db.Gigs.AsNoTracking().Where(g => g.BandId == band.Id && !g.IsArchived)
            .OrderByDescending(g => g.Date).Select(g => new { gigRef = g.Ref, title = g.Title, date = g.Date.ToString("yyyy-MM-dd") }).ToListAsync();
        return Ok(gigs);
    }

    public record HomeAddressIncompleteError(string Error, bool HomeAddressIncomplete);

    private async Task<IActionResult?> ApplyEndpointAsync(
        TripEndpointInput input, Guid bandId, Guid userId,
        Action<string?, string?, string?, string?, string?> assign)
    {
        if (input.IsHome)
        {
            var user = await db.Users.FindAsync(userId);
            if (user is null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(user.AddressLine1) || string.IsNullOrWhiteSpace(user.City) ||
                string.IsNullOrWhiteSpace(user.State) || string.IsNullOrWhiteSpace(user.PostalCode))
            {
                return BadRequest(new HomeAddressIncompleteError("Your home address isn't fully filled out in your Profile yet.", true));
            }
            assign(null, user.AddressLine1, user.City, user.State, user.PostalCode);
            return null;
        }

        var name = input.Name?.Trim();
        var line1 = input.AddressLine1?.Trim();
        var city = input.City?.Trim();
        var state = input.State?.Trim();
        var zip = input.PostalCode?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(line1) || string.IsNullOrEmpty(city) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(zip))
            return BadRequest(new { error = "Every location needs a name and a complete address (street, city, state, ZIP)." });

        // Seed the band's location book the first time this name is used,
        // so future trips can offer it back via FindLocation above. An
        // existing name is left as-is (see Travel.cs's doc comment) - the
        // Trip modal's reuse prompt is what already resolved any conflict
        // before this endpoint is ever called.
        var exists = await db.BandLocations.AnyAsync(l => l.BandId == bandId && l.Name.ToLower() == name.ToLower());
        if (!exists)
            db.BandLocations.Add(new BandLocation { BandId = bandId, Name = name, AddressLine1 = line1, City = city, State = state, PostalCode = zip });

        assign(name, line1, city, state, zip);
        return null;
    }

    [HttpPost("trips")]
    public async Task<IActionResult> CreateTrip([FromBody] SaveTripRequest request) => await SaveTripAsync(null, request);

    [HttpPut("trips/{id:guid}")]
    public async Task<IActionResult> UpdateTrip(Guid id, [FromBody] SaveTripRequest request) => await SaveTripAsync(id, request);

    private async Task<IActionResult> SaveTripAsync(Guid? id, SaveTripRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var userId = User.GetUserId()!.Value;

        Trip trip;
        if (id is { } tripId)
        {
            var existing = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId && t.BandId == band.Id && t.UserId == userId);
            if (existing is null) return NotFound(new { error = "Trip not found" });
            trip = existing;
        }
        else
        {
            trip = new Trip { BandId = band.Id, UserId = userId };
            db.Trips.Add(trip);
        }

        if (!Enum.TryParse<TripReason>(request.Reason, ignoreCase: true, out var reason))
            return BadRequest(new { error = "Invalid trip reason." });
        if (reason == TripReason.Other && string.IsNullOrWhiteSpace(request.OtherReasonText))
            return BadRequest(new { error = "Enter a short reason." });
        if (reason == TripReason.Gig && string.IsNullOrWhiteSpace(request.GigRef))
            return BadRequest(new { error = "Pick a gig." });

        trip.Date = request.Date;
        trip.FromIsHome = request.From.IsHome;
        var fromErr = await ApplyEndpointAsync(request.From, band.Id, userId,
            (name, l1, city, state, zip) => { trip.FromName = name; trip.FromAddressLine1 = l1; trip.FromCity = city; trip.FromState = state; trip.FromPostalCode = zip; });
        if (fromErr is not null) return fromErr;

        trip.ToIsHome = request.To.IsHome;
        var toErr = await ApplyEndpointAsync(request.To, band.Id, userId,
            (name, l1, city, state, zip) => { trip.ToName = name; trip.ToAddressLine1 = l1; trip.ToCity = city; trip.ToState = state; trip.ToPostalCode = zip; });
        if (toErr is not null) return toErr;

        trip.RoundTrip = request.RoundTrip;
        trip.Reason = reason;
        trip.OtherReasonText = reason == TripReason.Other ? request.OtherReasonText!.Trim() : null;
        trip.GigRef = reason == TripReason.Gig ? request.GigRef : null;
        trip.UpdatedAt = DateTime.UtcNow;

        var fromAddress = ComposeAddress(trip.FromAddressLine1, trip.FromCity, trip.FromState, trip.FromPostalCode);
        var toAddress = ComposeAddress(trip.ToAddressLine1, trip.ToCity, trip.ToState, trip.ToPostalCode);
        trip.DistanceMiles = await drivingDistance.ComputeMilesAsync(fromAddress, toAddress);

        await db.SaveChangesAsync();

        var gigTitles = new Dictionary<string, string>();
        if (trip.Reason == TripReason.Gig && trip.GigRef is not null)
        {
            var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == trip.GigRef);
            if (gig is not null) gigTitles[trip.GigRef] = gig.Title;
        }
        return Ok(SerializeTrip(trip, gigTitles));
    }
}
