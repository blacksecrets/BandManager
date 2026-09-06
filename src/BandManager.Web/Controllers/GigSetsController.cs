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
/// Gig setlists. Gigs themselves aren't stored here - they're read live
/// from the Band's own site via GigsSource (same source ScheduleItems'
/// GigRef already points at), so "the gig management page" lists whatever
/// that returns and a GigSet row only exists once someone starts building
/// that gig's set. Reading is open to the whole Band; building is
/// BandAdmin-only, same split as RepertoireController.
/// </summary>
[ApiController]
[Route("/api/gig-sets")]
[Authorize(Policy = "BandMember")]
public class GigSetsController(ApplicationDbContext db, IActiveBandAccessor activeBand, GigsSource gigsSource) : ControllerBase
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

        var gigs = await gigsSource.LoadGigsAsync(band);
        var setCounts = await db.GigSets.AsNoTracking()
            .Where(s => s.BandId == bandId)
            .Select(s => new { s.GigRef, Count = s.Songs.Count })
            .ToDictionaryAsync(x => x.GigRef, x => x.Count);

        var result = gigs.Select(g =>
        {
            var gigRef = SiteContentRef.GigRef(g);
            return new
            {
                gigRef,
                title = g.Title,
                venue = g.Venue,
                date = g.Date,
                time = g.Time,
                songCount = setCounts.GetValueOrDefault(gigRef, 0)
            };
        });
        return Ok(result);
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
