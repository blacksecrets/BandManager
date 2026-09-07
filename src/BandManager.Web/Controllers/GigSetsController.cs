using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record AddGigSetSongRequest(Guid SongId);
public record ReorderGigSetRequest(List<Guid> SongIds);

/// <summary>
/// Gig setlists. Gigs are real DB rows now (see Entities.Gig) - "the gig
/// management page" lists every Gig row for the band, and a GigSet row
/// only exists once someone starts building that gig's set. Reading is
/// open to the whole Band; building is BandAdmin-only, same split as
/// RepertoireController.
/// </summary>
[ApiController]
[Route("/api/gig-sets")]
[Authorize(Policy = "BandMember")]
public class GigSetsController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    // The gig management page's own list - every gig on the Band's site,
    // with whether a set already exists and how many songs are in it.
    [HttpGet("gigs")]
    public async Task<IActionResult> ListGigs()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bandId);
        if (band is null) return NotFound();

        var gigs = await db.Gigs.AsNoTracking().Where(g => g.BandId == bandId).ToListAsync();
        var setCounts = await db.GigSets.AsNoTracking()
            .Where(s => s.BandId == bandId)
            .Select(s => new { s.GigRef, Count = s.Songs.Count })
            .ToDictionaryAsync(x => x.GigRef, x => x.Count);

        var today = DateTime.Today;
        var result = gigs.Select(g =>
        {
            var isPast = DateTime.TryParse(g.Date, out var parsed) && parsed.Date < today;
            return new
            {
                gigRef = g.Ref,
                title = g.Title,
                venue = g.Venue,
                date = g.Date,
                time = g.Time,
                songCount = setCounts.GetValueOrDefault(g.Ref, 0),
                isPast
            };
        });
        return Ok(result);
    }

    // "Everything associated with this gig" for Gig Management's past-gig
    // viewer - every ScheduleItem+Artifacts tied to it, plus its most
    // recent Flyer (and which FlyerTemplate built it, if any). A new,
    // dedicated endpoint rather than a ?gigRef= filter bolted onto
    // ScheduleItemsController's general-purpose GET /api/items, which
    // serves the Dashboard's own heavily-used query path - keeping this
    // gig-scoped concern here, where GigSetsController already owns it.
    [HttpGet("{gigRef}/items")]
    public async Task<IActionResult> GetGigItems(string gigRef)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bandId);
        if (band is null) return NotFound();
        var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.BandId == bandId && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var items = await db.ScheduleItems.AsNoTracking()
            .Include(s => s.Artifacts)
            .Where(s => s.BandId == bandId && s.GigRef == gigRef)
            .ToListAsync();

        var flyer = await db.Flyers.AsNoTracking()
            .Include(f => f.FlyerTemplate)
            .Where(f => f.BandId == bandId && f.GigRef == gigRef)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            gig = new { title = gig.Title, venue = gig.Venue, date = gig.Date, flyerMain = gig.FlyerMain },
            items = items.Select(i => new
            {
                i.Id,
                i.ContentType,
                i.Category,
                i.PostedAt,
                artifacts = i.Artifacts.Select(a => new { a.ArtifactType, a.FilePath, a.TextValue })
            }),
            flyer = flyer is null ? null : new
            {
                flyer.Id,
                flyer.GeneratedCatalogItemId,
                flyerTemplateId = flyer.FlyerTemplateId,
                flyerTemplateName = flyer.FlyerTemplate?.Name
            }
        });
    }

    // Copies a past gig's setlist into the current one - matches by
    // SongId (never duplicates a song already in the target), appends
    // after the target's current max SortOrder, iterating the source in
    // its own order so relative order among the newly-imported songs is
    // preserved even though absolute SortOrder values get renumbered.
    [HttpPost("{gigRef}/import-from/{sourceGigRef}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> ImportFromGig(string gigRef, string sourceGigRef)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var sourceSet = await db.GigSets.AsNoTracking()
            .Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == sourceGigRef);
        if (sourceSet is null || sourceSet.Songs.Count == 0)
            return BadRequest(new { error = "That gig has no set to import." });

        var targetSet = await db.GigSets.Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        if (targetSet is null)
        {
            targetSet = new GigSet { BandId = bandId, GigRef = gigRef };
            db.GigSets.Add(targetSet);
            await db.SaveChangesAsync();
        }

        var existingSongIds = targetSet.Songs.Select(s => s.SongId).ToHashSet();
        var nextSort = targetSet.Songs.Count == 0 ? 0 : targetSet.Songs.Max(s => s.SortOrder) + 1;
        var imported = 0;
        foreach (var song in sourceSet.Songs.OrderBy(s => s.SortOrder))
        {
            if (existingSongIds.Contains(song.SongId)) continue;
            db.GigSetSongs.Add(new GigSetSong { GigSetId = targetSet.Id, SongId = song.SongId, SortOrder = nextSort++ });
            imported++;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, imported });
    }

    [HttpGet("{gigRef}")]
    public async Task<IActionResult> GetSet(string gigRef)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var set = await db.GigSets.AsNoTracking()
            .Include(s => s.Songs.OrderBy(gs => gs.SortOrder)).ThenInclude(gs => gs.Song)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);

        var songs = set?.Songs.OrderBy(gs => gs.SortOrder).Select(gs => new
        {
            songId = gs.SongId,
            title = gs.Song.Title,
            originalArtist = gs.Song.OriginalArtist,
            key = gs.Song.Key,
            lengthSeconds = gs.Song.LengthSeconds
        }) ?? [];

        return Ok(new { gigRef, songs });
    }

    [HttpPost("{gigRef}/songs")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> AddSong(string gigRef, [FromBody] AddGigSetSongRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        if (!await db.RepertoireEntries.AnyAsync(e => e.BandId == bandId && e.SongId == request.SongId))
            return BadRequest(new { error = "That song isn't in the repertoire yet." });

        var set = await db.GigSets.Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        if (set is null)
        {
            set = new GigSet { BandId = bandId, GigRef = gigRef };
            db.GigSets.Add(set);
        }
        else if (set.Songs.Any(s => s.SongId == request.SongId))
        {
            return BadRequest(new { error = "Already in this gig's set." });
        }

        var nextSort = set.Songs.Count == 0 ? 0 : set.Songs.Max(s => s.SortOrder) + 1;
        // Added directly via the DbSet (not set.Songs.Add(...)) - a new
        // entity reaching the change tracker only through a navigation-
        // collection add on an already-tracked parent, with its Guid key
        // already populated client-side (every entity's default), gets
        // misread as an existing row needing an UPDATE rather than an
        // INSERT. Explicit DbSet.Add avoids that ambiguity entirely.
        db.GigSetSongs.Add(new GigSetSong { GigSetId = set.Id, SongId = request.SongId, SortOrder = nextSort });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("{gigRef}/reorder")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Reorder(string gigRef, [FromBody] ReorderGigSetRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var set = await db.GigSets.Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        if (set is null) return NotFound(new { error = "Not found" });

        var currentIds = set.Songs.Select(s => s.SongId).ToHashSet();
        if (request.SongIds.Count != currentIds.Count || !request.SongIds.All(currentIds.Contains))
            return BadRequest(new { error = "The song list doesn't match this set - reload and try again." });

        for (var i = 0; i < request.SongIds.Count; i++)
        {
            set.Songs.First(s => s.SongId == request.SongIds[i]).SortOrder = i;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{gigRef}/songs/{songId:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> RemoveSong(string gigRef, Guid songId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var set = await db.GigSets.Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        var entry = set?.Songs.FirstOrDefault(s => s.SongId == songId);
        if (set is null || entry is null) return NotFound(new { error = "Not found" });

        set.Songs.Remove(entry);
        db.GigSetSongs.Remove(entry);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
