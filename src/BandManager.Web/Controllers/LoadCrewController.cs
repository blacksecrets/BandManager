using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveLoadCrewDefaultRequest(LoadCrewListType ListType, string Text);
public record SaveLoadCrewItemRequest(LoadCrewListType ListType, string Text);
public record ReorderLoadCrewRequest(LoadCrewListType ListType, List<Guid> Ids);

/// <summary>
/// Load-in/load-out crew duties - who sets up what, in what order, who
/// talks to the sound engineer. Deliberately BAND-wide and shared, unlike
/// GigPrepController's per-user checklists - see LoadCrew.cs's own doc
/// comment for why. The default template (per Act) is BandAdmin-only to
/// edit, since it shapes every future gig; the per-gig checklist it seeds
/// is open to any member to adjust for an odd venue, same as the setlist
/// itself already is.
/// </summary>
[ApiController]
[Route("/api/load-crew")]
[Authorize(Policy = "BandMember")]
public class LoadCrewController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object SerializeDefault(LoadCrewDefaultItem i) => new { id = i.Id, actId = i.ActId, listType = i.ListType, text = i.Text };
    private static object SerializeItem(LoadCrewChecklistItem i) => new
    {
        id = i.Id,
        listType = i.ListType,
        text = i.Text,
        isChecked = i.IsChecked,
        assigneeUserId = i.AssigneeUserId,
        assigneeName = i.AssigneeUser?.DisplayName
    };

    // --- Default template (per Act, Band Admin > Acts) ---

    [HttpGet("defaults/{actId:guid}")]
    public async Task<IActionResult> ListDefaults(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var items = await db.LoadCrewDefaultItems.AsNoTracking()
            .Where(i => i.ActId == actId).OrderBy(i => i.SortOrder).ToListAsync();
        return Ok(items.Select(SerializeDefault));
    }

    [HttpPost("defaults/{actId:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> AddDefault(Guid actId, [FromBody] SaveLoadCrewDefaultRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(new { error = "Text is required." });

        var maxSort = await db.LoadCrewDefaultItems
            .Where(i => i.ActId == actId && i.ListType == request.ListType)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        var item = new LoadCrewDefaultItem { ActId = actId, ListType = request.ListType, Text = text, SortOrder = maxSort + 1 };
        db.LoadCrewDefaultItems.Add(item);
        await db.SaveChangesAsync();
        return Ok(SerializeDefault(item));
    }

    [HttpDelete("defaults/{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> DeleteDefault(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var item = await db.LoadCrewDefaultItems.Include(i => i.Act).FirstOrDefaultAsync(i => i.Id == id && i.Act.BandId == bandId);
        if (item is null) return Ok(new { ok = true });
        db.LoadCrewDefaultItems.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- Per-gig checklist ---

    private async Task<Gig?> RequireGigAsync(Guid bandId, string gigRef) =>
        await db.Gigs.FirstOrDefaultAsync(g => g.BandId == bandId && g.Ref == gigRef);

    [HttpGet("{gigRef}")]
    public async Task<IActionResult> GetChecklist(string gigRef)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var gig = await RequireGigAsync(bandId, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var hasAny = await db.LoadCrewChecklistItems.AnyAsync(i => i.GigId == gig.Id);
        if (!hasAny && gig.ActId is { } actId)
        {
            var defaults = await db.LoadCrewDefaultItems.AsNoTracking()
                .Where(i => i.ActId == actId).OrderBy(i => i.SortOrder).ToListAsync();
            foreach (var group in defaults.GroupBy(d => d.ListType))
            {
                var sort = 0;
                foreach (var d in group)
                    db.LoadCrewChecklistItems.Add(new LoadCrewChecklistItem { GigId = gig.Id, ListType = d.ListType, Text = d.Text, SortOrder = sort++ });
            }
            if (defaults.Count > 0) await db.SaveChangesAsync();
        }

        var items = await db.LoadCrewChecklistItems.AsNoTracking().Include(i => i.AssigneeUser)
            .Where(i => i.GigId == gig.Id).OrderBy(i => i.SortOrder).ToListAsync();
        return Ok(items.Select(SerializeItem));
    }

    [HttpPost("{gigRef}/items")]
    public async Task<IActionResult> AddItem(string gigRef, [FromBody] SaveLoadCrewItemRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var gig = await RequireGigAsync(bandId, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(new { error = "Text is required." });

        var maxSort = await db.LoadCrewChecklistItems
            .Where(i => i.GigId == gig.Id && i.ListType == request.ListType)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        var item = new LoadCrewChecklistItem { GigId = gig.Id, ListType = request.ListType, Text = text, SortOrder = maxSort + 1 };
        db.LoadCrewChecklistItems.Add(item);
        await db.SaveChangesAsync();
        return Ok(SerializeItem(item));
    }

    [HttpPut("{gigRef}/items/{id:guid}/check")]
    public async Task<IActionResult> ToggleCheck(string gigRef, Guid id, [FromBody] bool isChecked)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var gig = await RequireGigAsync(bandId, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var item = await db.LoadCrewChecklistItems.FirstOrDefaultAsync(i => i.Id == id && i.GigId == gig.Id);
        if (item is null) return NotFound(new { error = "Not found" });
        item.IsChecked = isChecked;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("{gigRef}/items/{id:guid}/assignee")]
    public async Task<IActionResult> SetAssignee(string gigRef, Guid id, [FromBody] Guid? assigneeUserId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var gig = await RequireGigAsync(bandId, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        if (assigneeUserId is { } uid && !await db.BandMemberships.AnyAsync(m => m.BandId == bandId && m.UserId == uid))
            return BadRequest(new { error = "That person isn't a member of this band." });

        var item = await db.LoadCrewChecklistItems.FirstOrDefaultAsync(i => i.Id == id && i.GigId == gig.Id);
        if (item is null) return NotFound(new { error = "Not found" });
        item.AssigneeUserId = assigneeUserId;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{gigRef}/items/{id:guid}")]
    public async Task<IActionResult> DeleteItem(string gigRef, Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var gig = await RequireGigAsync(bandId, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var item = await db.LoadCrewChecklistItems.FirstOrDefaultAsync(i => i.Id == id && i.GigId == gig.Id);
        if (item is null) return Ok(new { ok = true });
        db.LoadCrewChecklistItems.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("{gigRef}/reorder")]
    public async Task<IActionResult> Reorder(string gigRef, [FromBody] ReorderLoadCrewRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var gig = await RequireGigAsync(bandId, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var items = await db.LoadCrewChecklistItems
            .Where(i => i.GigId == gig.Id && i.ListType == request.ListType).ToListAsync();
        var currentIds = items.Select(i => i.Id).ToHashSet();
        if (request.Ids.Count != currentIds.Count || !request.Ids.All(currentIds.Contains))
            return BadRequest(new { error = "The list doesn't match - reload and try again." });

        for (var idx = 0; idx < request.Ids.Count; idx++)
            items.First(i => i.Id == request.Ids[idx]).SortOrder = idx;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
