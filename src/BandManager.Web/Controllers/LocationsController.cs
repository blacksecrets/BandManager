using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveLocationRequest(string Name, string AddressLine1, string City, string State, string PostalCode, string Kind);

/// <summary>
/// The generalized version of BandLocation (Travel.cs) - a general band
/// address book (Venue/Studio/Rehearsal Space/Store/Other), not just
/// trip endpoints. Read open to any BandMember (booking/logistics info
/// isn't admin-only anywhere else in this app), write BandAdmin-only,
/// same split Venues already uses. Deliberately a separate book from
/// Venue itself - see BandLocation's own doc comment for why (Venue
/// carries booking-specific fields - capacity, stage dimensions, a
/// default promoter - that don't belong on a rehearsal space or a
/// member's second home).
/// </summary>
[ApiController]
[Route("/api/locations")]
[Authorize(Policy = "BandMember")]
public class LocationsController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static object Serialize(BandLocation l) => new
    {
        id = l.Id,
        name = l.Name,
        addressLine1 = l.AddressLine1,
        city = l.City,
        state = l.State,
        postalCode = l.PostalCode,
        kind = l.Kind.ToString(),
        mapsUrl = MapsUrl(l.AddressLine1, l.City, l.State, l.PostalCode)
    };

    // Plain Google Maps search-by-address link - no API key, no cost, no
    // stored coordinates to keep in sync with a hand-edited address.
    internal static string MapsUrl(string? line1, string? city, string? state, string? postalCode)
    {
        var query = string.Join(", ", new[] { line1, city, state, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return $"https://maps.google.com/?q={Uri.EscapeDataString(query)}";
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var locations = await db.BandLocations.AsNoTracking()
            .Where(l => l.BandId == bandId).OrderBy(l => l.Name).ToListAsync();
        return Ok(locations.Select(Serialize));
    }

    [HttpPost]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Create([FromBody] SaveLocationRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required." });
        if (!Enum.TryParse<LocationKind>(request.Kind, out var kind))
            return BadRequest(new { error = "Invalid location kind." });
        if (string.IsNullOrWhiteSpace(request.AddressLine1) || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.PostalCode))
            return BadRequest(new { error = "A complete address (street, city, state, ZIP) is required." });

        var location = new BandLocation
        {
            BandId = bandId,
            Name = name,
            AddressLine1 = Clean(request.AddressLine1)!,
            City = Clean(request.City)!,
            State = Clean(request.State)!,
            PostalCode = Clean(request.PostalCode)!,
            Kind = kind
        };
        db.BandLocations.Add(location);
        await db.SaveChangesAsync();
        return Ok(Serialize(location));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveLocationRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var location = await db.BandLocations.FirstOrDefaultAsync(l => l.Id == id && l.BandId == bandId);
        if (location is null) return NotFound(new { error = "Not found" });

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required." });
        if (!Enum.TryParse<LocationKind>(request.Kind, out var kind))
            return BadRequest(new { error = "Invalid location kind." });
        if (string.IsNullOrWhiteSpace(request.AddressLine1) || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.PostalCode))
            return BadRequest(new { error = "A complete address (street, city, state, ZIP) is required." });

        location.Name = name;
        location.AddressLine1 = Clean(request.AddressLine1)!;
        location.City = Clean(request.City)!;
        location.State = Clean(request.State)!;
        location.PostalCode = Clean(request.PostalCode)!;
        location.Kind = kind;
        await db.SaveChangesAsync();
        return Ok(Serialize(location));
    }

    // Safe to hard-delete unconditionally - Trip snapshots an address at
    // save time rather than linking live to a BandLocation row (see
    // BandLocation's own doc comment), so removing a location here can
    // never orphan or corrupt an existing trip log entry.
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var location = await db.BandLocations.FirstOrDefaultAsync(l => l.Id == id && l.BandId == bandId);
        if (location is null) return NotFound(new { error = "Not found" });
        db.BandLocations.Remove(location);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
