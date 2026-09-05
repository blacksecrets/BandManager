using BandManager.Data;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SwitchBandRequest(Guid BandId);

/// <summary>
/// The band switcher's backend: list what this user can switch into, and
/// actually switch. SuperAdmin sees every Band; everyone else sees only
/// their own BandMemberships - same distinction as BandAccessCheck.
/// </summary>
[ApiController]
[Route("/api/bands")]
[Authorize]
public class BandsController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        if (User.IsSuperAdmin())
        {
            var all = await db.Bands.AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new { bandId = b.Id, bandName = b.Name, role = "SuperAdmin" })
                .ToListAsync();
            return Ok(all);
        }

        var mine = await db.BandMemberships.AsNoTracking()
            .Include(m => m.Band)
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Band.Name)
            .Select(m => new { bandId = m.BandId, bandName = m.Band.Name, role = m.Role.ToString() })
            .ToListAsync();
        return Ok(mine);
    }

    [HttpPost("active")]
    public async Task<IActionResult> SetActive([FromBody] SwitchBandRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var hasAccess = User.IsSuperAdmin() ||
            await db.BandMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId && m.BandId == request.BandId);

        if (!hasAccess) return Forbid();

        activeBand.SetActiveBandId(request.BandId);
        return Ok(new { ok = true, activeBandId = request.BandId });
    }
}
