using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record PlaceStagePlotItemRequest(Guid BandGearItemId, double X, double Y);
public record UpdateStagePlotItemRequest(double X, double Y, double Rotation);

/// <summary>
/// One Act's Stage Plot - see StagePlot.cs's doc comment. Read access is
/// broad (BandMember) but placing/moving/removing items is BandAdmin-only,
/// matching ActController's split. The StagePlot row itself is created
/// lazily on the first placed item (Add), never alongside the Act.
/// </summary>
[ApiController]
[Route("/api/acts/{actId:guid}/stage-plot")]
[Authorize(Policy = "BandAdmin")]
public class StagePlotController(ApplicationDbContext db, IActiveBandAccessor activeBand, IWebHostEnvironment env) : ControllerBase
{
    private string FontsRootPath => Path.Combine(env.WebRootPath, "fonts");

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private async Task<bool> ActExistsAsync(Guid bandId, Guid actId) =>
        await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId);

    private static object SerializeItem(StagePlotItem item) => new
    {
        id = item.Id,
        bandGearItemId = item.BandGearItemId,
        visibleId = item.VisibleId,
        x = item.X,
        y = item.Y,
        rotation = item.Rotation,
        type = item.BandGearItem.Type,
        make = item.BandGearItem.Make,
        model = item.BandGearItem.Model,
        lengthInches = item.BandGearItem.LengthInches,
        widthInches = item.BandGearItem.WidthInches,
        depthInches = item.BandGearItem.DepthInches,
        weightPounds = item.BandGearItem.WeightPounds
    };

    [HttpGet]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await ActExistsAsync(bandId, actId)) return NotFound(new { error = "Act not found" });

        var items = await db.StagePlotItems.AsNoTracking().Include(i => i.BandGearItem)
            .Where(i => i.StagePlot.ActId == actId)
            .OrderBy(i => i.VisibleId)
            .ToListAsync();
        return Ok(items.Select(SerializeItem));
    }

    // The server-rendered PNG - see StagePlotRenderer.cs. Computed fresh
    // on every request rather than cached: cheap to draw, and always
    // exactly matches the live item data (no staleness to manage), same
    // "derived data doesn't get its own table" reasoning as the legend.
    [HttpGet("render")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Render(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await ActExistsAsync(bandId, actId)) return NotFound(new { error = "Act not found" });

        var items = await db.StagePlotItems.AsNoTracking()
            .Where(i => i.StagePlot.ActId == actId)
            .Select(i => new StagePlotItemDef(i.VisibleId, i.X, i.Y, i.Rotation))
            .ToListAsync();

        var png = StagePlotRenderer.RenderStagePlot(items, FontsRootPath);
        return File(png, "image/png");
    }

    // A dropped item's position, assigned the next sequential VisibleId
    // (the badge shown on the diagram, and the number the Tech Rider's
    // gear legend references) - only gear already on this Act's Gear
    // List can be placed, so the plot can never reference something the
    // Act's own catalog selection doesn't include.
    [HttpPost("items")]
    public async Task<IActionResult> AddItem(Guid actId, [FromBody] PlaceStagePlotItemRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await ActExistsAsync(bandId, actId)) return NotFound(new { error = "Act not found" });

        var onGearList = await db.ActGearItems.AnyAsync(x => x.ActId == actId && x.BandGearItemId == request.BandGearItemId);
        if (!onGearList) return BadRequest(new { error = "That item isn't on this Act's Gear List." });

        var plot = await db.StagePlots.Include(p => p.Items).FirstOrDefaultAsync(p => p.ActId == actId);
        if (plot is null)
        {
            plot = new StagePlot { ActId = actId };
            db.StagePlots.Add(plot);
        }

        var nextVisibleId = plot.Items.Count == 0 ? 1 : plot.Items.Max(i => i.VisibleId) + 1;
        var item = new StagePlotItem
        {
            StagePlotId = plot.Id,
            StagePlot = plot,
            BandGearItemId = request.BandGearItemId,
            VisibleId = nextVisibleId,
            X = Math.Clamp(request.X, 0, 1),
            Y = Math.Clamp(request.Y, 0, 1)
        };
        db.StagePlotItems.Add(item);
        plot.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var saved = await db.StagePlotItems.Include(i => i.BandGearItem).FirstAsync(i => i.Id == item.Id);
        return Ok(SerializeItem(saved));
    }

    [HttpPut("items/{itemId:guid}")]
    public async Task<IActionResult> UpdateItem(Guid actId, Guid itemId, [FromBody] UpdateStagePlotItemRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await ActExistsAsync(bandId, actId)) return NotFound(new { error = "Act not found" });

        var item = await db.StagePlotItems.Include(i => i.BandGearItem).Include(i => i.StagePlot)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.StagePlot.ActId == actId);
        if (item is null) return NotFound(new { error = "Not found" });

        item.X = Math.Clamp(request.X, 0, 1);
        item.Y = Math.Clamp(request.Y, 0, 1);
        item.Rotation = request.Rotation;
        item.StagePlot.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(SerializeItem(item));
    }

    // Un-places the item (doesn't touch the Act's Gear List membership -
    // it's still available to drag back on).
    [HttpDelete("items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid actId, Guid itemId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await ActExistsAsync(bandId, actId)) return NotFound(new { error = "Act not found" });

        var item = await db.StagePlotItems.Include(i => i.StagePlot).FirstOrDefaultAsync(i => i.Id == itemId && i.StagePlot.ActId == actId);
        if (item is null) return Ok(new { ok = true });

        db.StagePlotItems.Remove(item);
        item.StagePlot.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
