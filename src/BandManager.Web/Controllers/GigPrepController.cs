using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveGigPrepItemRequest(GigPrepListType ListType, string Text, bool SaveAsDefault = false);
public record ReorderGigPrepRequest(GigPrepListType ListType, List<Guid> Ids);

/// <summary>
/// Gig Prep - three per-user checklists (Pre-gig, Packing, Post-gig) for a
/// specific Gig, plus the per-user "defaults" library (edited on the
/// Profile page) that seeds them. Entirely personal: each band member
/// preps their own gear/tasks, never shared with the rest of the band -
/// unlike GigSetsController's setlist, which is deliberately the opposite
/// (see feedback_setlist_is_shared_per_gig memory).
///
/// Bootstrap rule, both directions, per the "after the first is made for
/// a Gig, it becomes the editable default for future gigs" request:
///  - Opening a gig's checklist for the first time copies the user's
///    current defaults in as starting content (if any exist yet).
///  - Adding an item straight to a gig's checklist, while that user's
///    default set for that list type is still empty, also adds it to
///    their defaults - establishing that default set for the first time.
///    Once a default list has any items, only editing it directly on the
///    Profile page changes it - gig-checklist edits stop mirroring back.
/// </summary>
[ApiController]
[Route("/api/gig-prep")]
[Authorize]
public class GigPrepController(ApplicationDbContext db) : ControllerBase
{
    private static object SerializeDefault(GigPrepDefaultItem i) => new { id = i.Id, actId = i.ActId, listType = i.ListType, text = i.Text };
    private static object SerializeItem(GigPrepChecklistItem i) => new { id = i.Id, listType = i.ListType, text = i.Text, isChecked = i.IsChecked };

    // Per list type, an Act-scoped default set (if the user has any items
    // in it) wins over the global one edited on the Profile page - lets
    // "your default" differ between e.g. an electric set and an unplugged
    // one, while a list type nobody's customized per-Act still falls back
    // to the global set instead of coming up empty.
    private async Task<List<GigPrepDefaultItem>> GetEffectiveDefaultsAsync(Guid userId, Guid? actId)
    {
        var actScoped = actId is null
            ? new List<GigPrepDefaultItem>()
            : await db.GigPrepDefaultItems.AsNoTracking()
                .Where(i => i.UserId == userId && i.ActId == actId)
                .OrderBy(i => i.SortOrder).ToListAsync();
        var actTypesCovered = actScoped.Select(d => d.ListType).ToHashSet();
        var globalFallback = await db.GigPrepDefaultItems.AsNoTracking()
            .Where(i => i.UserId == userId && i.ActId == null && !actTypesCovered.Contains(i.ListType))
            .OrderBy(i => i.SortOrder).ToListAsync();
        return actScoped.Concat(globalFallback).ToList();
    }

    // --- Defaults library (Profile page) ---

    [HttpGet("defaults")]
    public async Task<IActionResult> ListDefaults()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var items = await db.GigPrepDefaultItems.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();
        return Ok(items.Select(SerializeDefault));
    }

    [HttpPost("defaults")]
    public async Task<IActionResult> AddDefault([FromBody] SaveGigPrepItemRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(new { error = "Text is required." });

        var maxSort = await db.GigPrepDefaultItems
            .Where(i => i.UserId == userId && i.ListType == request.ListType)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        var item = new GigPrepDefaultItem { UserId = userId.Value, ListType = request.ListType, Text = text, SortOrder = maxSort + 1 };
        db.GigPrepDefaultItems.Add(item);
        await db.SaveChangesAsync();
        return Ok(SerializeDefault(item));
    }

    [HttpPut("defaults/{id:guid}")]
    public async Task<IActionResult> UpdateDefault(Guid id, [FromBody] SaveGigPrepItemRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var item = await db.GigPrepDefaultItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
        if (item is null) return NotFound(new { error = "Not found" });

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(new { error = "Text is required." });
        item.Text = text;
        await db.SaveChangesAsync();
        return Ok(SerializeDefault(item));
    }

    [HttpDelete("defaults/{id:guid}")]
    public async Task<IActionResult> DeleteDefault(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var item = await db.GigPrepDefaultItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
        if (item is null) return Ok(new { ok = true });
        db.GigPrepDefaultItems.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("defaults/reorder")]
    public async Task<IActionResult> ReorderDefaults([FromBody] ReorderGigPrepRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var items = await db.GigPrepDefaultItems
            .Where(i => i.UserId == userId && i.ListType == request.ListType)
            .ToListAsync();
        var currentIds = items.Select(i => i.Id).ToHashSet();
        if (request.Ids.Count != currentIds.Count || !request.Ids.All(currentIds.Contains))
            return BadRequest(new { error = "The list doesn't match - reload and try again." });

        for (var idx = 0; idx < request.Ids.Count; idx++)
            items.First(i => i.Id == request.Ids[idx]).SortOrder = idx;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- Per-gig checklist ---

    [HttpGet("{gigRef}")]
    public async Task<IActionResult> GetChecklist(string gigRef)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var hasAny = await db.GigPrepChecklistItems.AnyAsync(i => i.GigId == gig.Id && i.UserId == userId);
        if (!hasAny)
        {
            var defaults = await GetEffectiveDefaultsAsync(userId.Value, gig.ActId);
            if (defaults.Count > 0)
            {
                foreach (var group in defaults.GroupBy(d => d.ListType))
                {
                    var sort = 0;
                    foreach (var d in group)
                        db.GigPrepChecklistItems.Add(new GigPrepChecklistItem { GigId = gig.Id, UserId = userId.Value, ListType = d.ListType, Text = d.Text, SortOrder = sort++ });
                }
                await db.SaveChangesAsync();
            }
        }

        var items = await db.GigPrepChecklistItems.AsNoTracking()
            .Where(i => i.GigId == gig.Id && i.UserId == userId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();
        return Ok(items.Select(SerializeItem));
    }

    [HttpPost("{gigRef}/items")]
    public async Task<IActionResult> AddItem(string gigRef, [FromBody] SaveGigPrepItemRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(new { error = "Text is required." });

        var maxSort = await db.GigPrepChecklistItems
            .Where(i => i.GigId == gig.Id && i.UserId == userId && i.ListType == request.ListType)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        var item = new GigPrepChecklistItem { GigId = gig.Id, UserId = userId.Value, ListType = request.ListType, Text = text, SortOrder = maxSort + 1 };
        db.GigPrepChecklistItems.Add(item);

        // Explicit opt-in via the "also save to your default checklist"
        // checkbox - scoped to this gig's Act, not the global Profile-page
        // set, so it can only ever refine the electric-vs-unplugged case
        // rather than silently overwriting the person's general defaults.
        if (request.SaveAsDefault)
        {
            var alreadyDefault = await db.GigPrepDefaultItems.AnyAsync(d =>
                d.UserId == userId && d.ActId == gig.ActId && d.ListType == request.ListType && d.Text == text);
            if (!alreadyDefault)
            {
                var defaultMaxSort = await db.GigPrepDefaultItems
                    .Where(d => d.UserId == userId && d.ActId == gig.ActId && d.ListType == request.ListType)
                    .Select(d => (int?)d.SortOrder).MaxAsync() ?? -1;
                db.GigPrepDefaultItems.Add(new GigPrepDefaultItem { UserId = userId.Value, ActId = gig.ActId, ListType = request.ListType, Text = text, SortOrder = defaultMaxSort + 1 });
            }
        }

        await db.SaveChangesAsync();
        return Ok(SerializeItem(item));
    }

    // Replaces this gig's ENTIRE checklist (all three list types) with
    // the effective defaults for either this gig's own Act (the plain
    // "Load your default" button), or - when fromActId is given - any
    // other Act from any Band the person belongs to (the "Copy from
    // another Act or Band" flow: ListCopySources/GetActDefault below
    // populate that picker and its read-only preview). Either way this
    // is an explicit, confirmed action from the UI, not something that
    // happens automatically after the first bootstrap (see GetChecklist).
    [HttpPost("{gigRef}/load-default")]
    public async Task<IActionResult> LoadDefault(string gigRef, [FromQuery] Guid? fromActId)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var sourceActId = gig.ActId;
        if (fromActId is { } requestedActId)
        {
            var sourceAct = await db.Acts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == requestedActId);
            if (sourceAct is null) return NotFound(new { error = "Source Act not found" });
            if (!await db.BandMemberships.AnyAsync(m => m.UserId == userId && m.BandId == sourceAct.BandId))
                return Forbid();
            sourceActId = requestedActId;
        }

        var existing = await db.GigPrepChecklistItems.Where(i => i.GigId == gig.Id && i.UserId == userId).ToListAsync();
        db.GigPrepChecklistItems.RemoveRange(existing);

        var defaults = await GetEffectiveDefaultsAsync(userId.Value, sourceActId);
        foreach (var group in defaults.GroupBy(d => d.ListType))
        {
            var sort = 0;
            foreach (var d in group)
                db.GigPrepChecklistItems.Add(new GigPrepChecklistItem { GigId = gig.Id, UserId = userId.Value, ListType = d.ListType, Text = d.Text, SortOrder = sort++ });
        }
        await db.SaveChangesAsync();

        var items = await db.GigPrepChecklistItems.AsNoTracking()
            .Where(i => i.GigId == gig.Id && i.UserId == userId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();
        return Ok(items.Select(SerializeItem));
    }

    // Every (Band, Act) pair the person belongs to - populates the "Copy
    // from another Act or Band" grid. Includes every Band they're a
    // member of, not just the currently active one, per that feature's
    // whole point (their Unplugged Act's Packing list living on a
    // different Band than the gig they're prepping right now).
    [HttpGet("copy-sources")]
    public async Task<IActionResult> ListCopySources()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var acts = await db.Acts.AsNoTracking()
            .Where(a => db.BandMemberships.Any(m => m.UserId == userId && m.BandId == a.BandId))
            .Include(a => a.Band)
            .OrderBy(a => a.Band.Name).ThenByDescending(a => a.IsDefault).ThenBy(a => a.Name)
            .ToListAsync();
        return Ok(acts.Select(a => new { bandId = a.BandId, bandName = a.Band.Name, actId = a.Id, actName = a.Name }));
    }

    // Read-only preview of one Act's effective default checklist (same
    // Act-scoped-with-global-fallback resolution as LoadDefault) - the
    // "Copy from another Act or Band" picker's row-click preview, before
    // committing via LoadDefault's fromActId. 403s for an Act on a Band
    // the person isn't actually a member of, same check LoadDefault
    // itself makes before using a foreign fromActId.
    [HttpGet("act-default/{actId:guid}")]
    public async Task<IActionResult> GetActDefault(Guid actId)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var act = await db.Acts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == actId);
        if (act is null) return NotFound(new { error = "Act not found" });
        if (!await db.BandMemberships.AnyAsync(m => m.UserId == userId && m.BandId == act.BandId))
            return Forbid();

        var defaults = await GetEffectiveDefaultsAsync(userId.Value, actId);
        return Ok(defaults.Select(SerializeDefault));
    }

    [HttpPut("{gigRef}/items/{id:guid}/check")]
    public async Task<IActionResult> SetChecked(string gigRef, Guid id, [FromBody] bool isChecked)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var item = await db.GigPrepChecklistItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
        if (item is null) return NotFound(new { error = "Not found" });
        item.IsChecked = isChecked;
        await db.SaveChangesAsync();
        return Ok(SerializeItem(item));
    }

    [HttpDelete("{gigRef}/items/{id:guid}")]
    public async Task<IActionResult> DeleteItem(string gigRef, Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var item = await db.GigPrepChecklistItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
        if (item is null) return Ok(new { ok = true });
        db.GigPrepChecklistItems.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("{gigRef}/reorder")]
    public async Task<IActionResult> ReorderItems(string gigRef, [FromBody] ReorderGigPrepRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var items = await db.GigPrepChecklistItems
            .Where(i => i.GigId == gig.Id && i.UserId == userId && i.ListType == request.ListType)
            .ToListAsync();
        var currentIds = items.Select(i => i.Id).ToHashSet();
        if (request.Ids.Count != currentIds.Count || !request.Ids.All(currentIds.Contains))
            return BadRequest(new { error = "The list doesn't match - reload and try again." });

        for (var idx = 0; idx < request.Ids.Count; idx++)
            items.First(i => i.Id == request.Ids[idx]).SortOrder = idx;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
