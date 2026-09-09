using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveBandGearRequest(
    string Type, string? Make, string? Model,
    decimal? LengthInches, decimal? WidthInches, decimal? DepthInches, decimal? WeightPounds,
    Guid? OwnerUserId);

/// <summary>
/// A band's maintained gear catalog - see BandGearItem.cs's doc comment
/// for how this differs from a member's personal Gear (GearController).
/// Read access is broad (BandMember - an Act's Gear List picker and the
/// Stage Plot builder both need this) but every write is BandAdmin-only,
/// matching ActController's split.
/// </summary>
[ApiController]
[Route("/api/band-gear")]
[Authorize(Policy = "BandAdmin")]
public class BandGearController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(BandGearItem g) => new
    {
        id = g.Id,
        type = g.Type,
        make = g.Make,
        model = g.Model,
        lengthInches = g.LengthInches,
        widthInches = g.WidthInches,
        depthInches = g.DepthInches,
        weightPounds = g.WeightPounds,
        ownerUserId = g.OwnerUserId,
        ownerName = g.OwnerUser == null ? null : (g.OwnerUser.FirstName ?? g.OwnerUser.UserName!.Split('@')[0])
    };

    [HttpGet]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var items = await db.BandGearItems.AsNoTracking().Include(g => g.OwnerUser)
            .Where(g => g.BandId == bandId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Type)
            .ToListAsync();
        return Ok(items.Select(Serialize));
    }

    private static void ApplyFields(BandGearItem item, SaveBandGearRequest request)
    {
        item.Type = request.Type.Trim()[..Math.Min(request.Type.Trim().Length, 100)];
        item.Make = string.IsNullOrWhiteSpace(request.Make) ? null : request.Make.Trim();
        item.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        item.LengthInches = request.LengthInches;
        item.WidthInches = request.WidthInches;
        item.DepthInches = request.DepthInches;
        item.WeightPounds = request.WeightPounds;
        item.OwnerUserId = request.OwnerUserId;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveBandGearRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.Type)) return BadRequest(new { error = "Gear type is required." });

        // An owner pick must actually be a member of this band - otherwise
        // the "owner" label on the catalog would point at someone with no
        // relationship to it.
        if (request.OwnerUserId is { } ownerId && !await db.BandMemberships.AnyAsync(m => m.BandId == bandId && m.UserId == ownerId))
            return BadRequest(new { error = "That owner isn't a member of this band." });

        var maxSort = await db.BandGearItems.Where(g => g.BandId == bandId).Select(g => (int?)g.SortOrder).MaxAsync() ?? -1;
        var item = new BandGearItem { BandId = bandId, Type = request.Type, SortOrder = maxSort + 1 };
        ApplyFields(item, request);
        db.BandGearItems.Add(item);
        await db.SaveChangesAsync();

        var saved = await db.BandGearItems.Include(g => g.OwnerUser).FirstAsync(g => g.Id == item.Id);
        return Ok(Serialize(saved));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveBandGearRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.Type)) return BadRequest(new { error = "Gear type is required." });
        if (request.OwnerUserId is { } ownerId && !await db.BandMemberships.AnyAsync(m => m.BandId == bandId && m.UserId == ownerId))
            return BadRequest(new { error = "That owner isn't a member of this band." });

        var item = await db.BandGearItems.Include(g => g.OwnerUser).FirstOrDefaultAsync(g => g.Id == id && g.BandId == bandId);
        if (item is null) return NotFound(new { error = "Not found" });

        ApplyFields(item, request);
        await db.SaveChangesAsync();
        return Ok(Serialize(item));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.BandGearItems.FirstOrDefaultAsync(g => g.Id == id && g.BandId == bandId);
        if (item is null) return Ok(new { ok = true });

        db.BandGearItems.Remove(item); // cascades ActGearItems (Commit 4) once that FK exists
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // The "pick from a member's gear" flow's second step - a member's own
    // personal Gear list, restricted to members of the active band (a
    // Band Admin can't browse a stranger's inventory just by guessing a
    // user id).
    [HttpGet("member-gear/{userId:guid}")]
    public async Task<IActionResult> ListMemberGear(Guid userId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.BandMemberships.AnyAsync(m => m.BandId == bandId && m.UserId == userId))
            return BadRequest(new { error = "That user isn't a member of this band." });

        var gear = await db.Gear.AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.SortOrder).ToListAsync();
        return Ok(gear.Select(g => new
        {
            id = g.Id,
            type = g.Type,
            make = g.Make,
            model = g.Model,
            lengthInches = g.LengthInches,
            widthInches = g.WidthInches,
            depthInches = g.DepthInches,
            weightPounds = g.WeightPounds
        }));
    }

    // One-time snapshot of a band member's personal Gear row into this
    // band's catalog - not a live link (see BandGearItem.cs's doc
    // comment), so the member's own listing can keep changing
    // independently afterward. OwnerUserId is set to that member,
    // matching what "copied from my own gear" should mean.
    [HttpPost("from-member/{gearId:guid}")]
    public async Task<IActionResult> CreateFromMember(Guid gearId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var source = await db.Gear.FirstOrDefaultAsync(g => g.Id == gearId);
        if (source is null) return NotFound(new { error = "Gear item not found." });
        if (!await db.BandMemberships.AnyAsync(m => m.BandId == bandId && m.UserId == source.UserId))
            return BadRequest(new { error = "That gear belongs to someone who isn't a member of this band." });

        var maxSort = await db.BandGearItems.Where(g => g.BandId == bandId).Select(g => (int?)g.SortOrder).MaxAsync() ?? -1;
        var item = new BandGearItem
        {
            BandId = bandId,
            OwnerUserId = source.UserId,
            Type = source.Type,
            Make = source.Make,
            Model = source.Model,
            LengthInches = source.LengthInches,
            WidthInches = source.WidthInches,
            DepthInches = source.DepthInches,
            WeightPounds = source.WeightPounds,
            SortOrder = maxSort + 1
        };
        db.BandGearItems.Add(item);
        await db.SaveChangesAsync();

        var saved = await db.BandGearItems.Include(g => g.OwnerUser).FirstAsync(g => g.Id == item.Id);
        return Ok(Serialize(saved));
    }
}
