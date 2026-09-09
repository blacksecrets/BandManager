using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveActRequest(
    string Name, string? IntroText, string? VideoNotes, string? GeneralNotes,
    string? TechContactName, string? TechContactPhone, string? TechContactEmail);

public record SetActGearRequest(List<Guid> BandGearItemIds);

/// <summary>
/// A band's performance configurations - see Act.cs's doc comment. Read
/// access is broad (BandMember - the Gig create/edit form's Act picker
/// needs the list) but every write is BandAdmin-only, matching how the
/// rest of a band's core configuration (Setup, Cadence, Band Roles) is
/// gated.
/// </summary>
[ApiController]
[Route("/api/acts")]
[Authorize(Policy = "BandAdmin")]
public class ActController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(Act a) => new
    {
        id = a.Id,
        name = a.Name,
        isDefault = a.IsDefault,
        introText = a.IntroText,
        videoNotes = a.VideoNotes,
        generalNotes = a.GeneralNotes,
        techContactName = a.TechContactName,
        techContactPhone = a.TechContactPhone,
        techContactEmail = a.TechContactEmail,
        createdAt = a.CreatedAt
    };

    [HttpGet]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var acts = await db.Acts.AsNoTracking().Where(a => a.BandId == bandId)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Name).ToListAsync();
        return Ok(acts.Select(Serialize));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var act = await db.Acts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });
        return Ok(Serialize(act));
    }

    private static void ApplyFields(Act act, SaveActRequest request)
    {
        act.Name = request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 200)];
        act.IntroText = string.IsNullOrWhiteSpace(request.IntroText) ? null : request.IntroText.Trim();
        act.VideoNotes = string.IsNullOrWhiteSpace(request.VideoNotes) ? null : request.VideoNotes.Trim();
        act.GeneralNotes = string.IsNullOrWhiteSpace(request.GeneralNotes) ? null : request.GeneralNotes.Trim();
        act.TechContactName = string.IsNullOrWhiteSpace(request.TechContactName) ? null : request.TechContactName.Trim();
        act.TechContactPhone = string.IsNullOrWhiteSpace(request.TechContactPhone) ? null : request.TechContactPhone.Trim();
        act.TechContactEmail = string.IsNullOrWhiteSpace(request.TechContactEmail) ? null : request.TechContactEmail.Trim();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveActRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });

        var act = new Act { BandId = bandId, Name = request.Name };
        ApplyFields(act, request);
        db.Acts.Add(act);
        await db.SaveChangesAsync();
        return Ok(Serialize(act));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveActRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });

        var act = await db.Acts.FirstOrDefaultAsync(a => a.Id == id && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });

        ApplyFields(act, request);
        await db.SaveChangesAsync();
        return Ok(Serialize(act));
    }

    // The one Act every Band is created with (see SuperAdminController.CreateBand)
    // is undeletable, guaranteeing a Band is never left with zero Acts.
    // Every other Act is deletable, unless a Gig still points at it -
    // reassign those first rather than silently orphaning them (the DB's
    // own Restrict FK on Gig.ActId backs this up either way).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var act = await db.Acts.FirstOrDefaultAsync(a => a.Id == id && a.BandId == bandId);
        if (act is null) return Ok(new { ok = true });
        if (act.IsDefault) return BadRequest(new { error = "This band's default Act can't be deleted." });

        var gigCount = await db.Gigs.CountAsync(g => g.BandId == bandId && g.ActId == id);
        if (gigCount > 0)
            return BadRequest(new { error = $"This Act is still assigned to {gigCount} gig{(gigCount == 1 ? "" : "s")} - reassign those first." });

        db.Acts.Remove(act);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static object SerializeGearItem(BandGearItem g) => new
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

    // This Act's Gear List - a selection from the band's full Gear
    // Catalog (BandGearController), in the order they were assigned.
    [HttpGet("{actId:guid}/gear")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetGear(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var items = await db.ActGearItems.AsNoTracking().Include(x => x.BandGearItem).ThenInclude(g => g.OwnerUser)
            .Where(x => x.ActId == actId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.BandGearItem)
            .ToListAsync();
        return Ok(items.Select(SerializeGearItem));
    }

    // Replace-the-set save, like a checklist - Band Admin picks from the
    // full Gear Catalog and this becomes the Act's Gear List exactly as
    // submitted (order preserved), same "just rewrite it" approach as
    // this session's other checklist-shaped saves.
    [HttpPut("{actId:guid}/gear")]
    public async Task<IActionResult> SetGear(Guid actId, [FromBody] SetActGearRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var act = await db.Acts.FirstOrDefaultAsync(a => a.Id == actId && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });

        var validIds = await db.BandGearItems.Where(g => g.BandId == bandId && request.BandGearItemIds.Contains(g.Id))
            .Select(g => g.Id).ToListAsync();

        db.ActGearItems.RemoveRange(db.ActGearItems.Where(x => x.ActId == actId));
        var sort = 0;
        foreach (var gearId in request.BandGearItemIds.Where(validIds.Contains))
            db.ActGearItems.Add(new ActGearItem { ActId = actId, BandGearItemId = gearId, SortOrder = sort++ });

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
