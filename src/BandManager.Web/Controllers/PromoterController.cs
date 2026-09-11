using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SavePromoterRequest(string Name, string? Company, string? Phone, string? Email);

/// <summary>
/// A band's reusable promoter/booking-contact roster - see Promoter.cs's
/// doc comment for why this is separate from VenueContact. Read access is
/// broad (BandMember - Venue and Gig pickers both need this) but writes
/// are BandAdmin-only, matching ActController's split.
/// </summary>
[ApiController]
[Route("/api/promoters")]
[Authorize(Policy = "BandAdmin")]
public class PromoterController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(Promoter p) => new
    {
        id = p.Id,
        name = p.Name,
        company = p.Company,
        phone = p.Phone,
        email = p.Email
    };

    [HttpGet]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var promoters = await db.Promoters.AsNoTracking().Where(p => p.BandId == bandId).OrderBy(p => p.Name).ToListAsync();
        return Ok(promoters.Select(Serialize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SavePromoterRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required." });

        var promoter = new Promoter
        {
            BandId = bandId,
            Name = name,
            Company = Clean(request.Company),
            Phone = Clean(request.Phone),
            Email = Clean(request.Email)
        };
        db.Promoters.Add(promoter);
        await db.SaveChangesAsync();
        return Ok(Serialize(promoter));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavePromoterRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required." });

        var promoter = await db.Promoters.FirstOrDefaultAsync(p => p.Id == id && p.BandId == bandId);
        if (promoter is null) return NotFound(new { error = "Not found" });

        promoter.Name = name;
        promoter.Company = Clean(request.Company);
        promoter.Phone = Clean(request.Phone);
        promoter.Email = Clean(request.Email);
        await db.SaveChangesAsync();
        return Ok(Serialize(promoter));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var promoter = await db.Promoters.FirstOrDefaultAsync(p => p.Id == id && p.BandId == bandId);
        if (promoter is null) return Ok(new { ok = true });

        db.Promoters.Remove(promoter); // Venue.DefaultPromoterId/Gig.PromoterId both SetNull on delete
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
