using BandManager.Data;
using BandManager.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record CreateBandRequest(string Name, string Slug);

/// <summary>
/// The start of the SuperAdmin config screen's backend - band onboarding
/// only so far (enough to actually create a Band to test everything else
/// against). Creating that Band's first BandAdmin, platform-level
/// branding, and the rest of the SuperAdmin screen are still to come.
/// </summary>
[ApiController]
[Route("/api/superadmin")]
[Authorize(Policy = "SuperAdmin")]
public class SuperAdminController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("bands")]
    public async Task<IActionResult> ListBands()
    {
        var bands = await db.Bands.OrderBy(b => b.Name)
            .Select(b => new { id = b.Id, name = b.Name, slug = b.Slug, createdAt = b.CreatedAt })
            .ToListAsync();
        return Ok(bands);
    }

    [HttpPost("bands")]
    public async Task<IActionResult> CreateBand([FromBody] CreateBandRequest request)
    {
        var name = request.Name?.Trim();
        var slug = request.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Name is required" });
        if (string.IsNullOrEmpty(slug) || !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9-]+$"))
            return BadRequest(new { error = "Slug is required and must be lowercase letters/numbers/hyphens" });
        if (await db.Bands.AnyAsync(b => b.Slug == slug))
            return BadRequest(new { error = "That slug is already in use" });

        var band = new Band { Name = name, Slug = slug };
        db.Bands.Add(band);
        await db.SaveChangesAsync();
        return Ok(new { id = band.Id, name = band.Name, slug = band.Slug });
    }
}
