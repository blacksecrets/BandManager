using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record AddGigSetSongRequest(Guid SongId);
public record AddManualGigSetSongRequest(string Title, string? Artist, int? LengthSeconds, string? YouTubeUrl, string? SpotifyUrl);
public record ReorderGigSetRequest(List<Guid> Ids);
public record CreateFloatingGigSetRequest(string Name);

/// <summary>
/// Gig setlists. Gigs are real DB rows now (see Entities.Gig) - "the gig
/// management page" lists every Gig row for the band, and a GigSet row
/// only exists once someone starts building that gig's set. Any band
/// member can read AND build/edit a set - unlike most of this app's other
/// write paths, setlist-building is deliberately not BandAdmin-gated,
/// per the explicit "Any Band Member should be able to construct or edit
/// a setlist" request.
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

    // Mirrors gig-sets.js's renderSet() h/m/s formatting - the one place
    // this app already computes a setlist duration, just client-side and
    // per-page. Grids that list several setlists at once (which may not
    // have loaded any one of them yet) need the number server-side.
    private static string FormatDuration(int totalSeconds)
    {
        var h = totalSeconds / 3600;
        var m = totalSeconds % 3600 / 60;
        var s = totalSeconds % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
    }

    private static int SetDurationSeconds(GigSet set) =>
        set.Songs.Sum(s => s.Song?.LengthSeconds ?? s.ManualLengthSeconds ?? 0);

    private static object Serialize(GigSetSong gs) => new
    {
        id = gs.Id,
        songId = gs.SongId,
        title = gs.Song?.Title ?? gs.ManualTitle,
        originalArtist = gs.Song?.OriginalArtist ?? gs.ManualArtist,
        key = gs.Song?.Key,
        lengthSeconds = gs.Song?.LengthSeconds ?? gs.ManualLengthSeconds,
        youTubeUrl = gs.Song?.YouTubeUrl ?? gs.ManualYouTubeUrl,
        spotifyUrl = gs.Song?.SpotifyUrl ?? gs.ManualSpotifyUrl,
        isManual = gs.SongId is null
    };

    // The gig management page's own list - every gig on the Band's site,
    // with whether a set already exists and how many songs are in it.
    [HttpGet("gigs")]
    public async Task<IActionResult> ListGigs()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bandId);
        if (band is null) return NotFound();

        var gigs = await db.Gigs.AsNoTracking().Where(g => g.BandId == bandId && !g.IsArchived).ToListAsync();
        var sets = await db.GigSets.AsNoTracking().Include(s => s.Songs).ThenInclude(gs => gs.Song)
            .Where(s => s.BandId == bandId && !s.IsFloating)
            .ToListAsync();
        var setCounts = sets.ToDictionary(s => s.GigRef, s => s.Songs.Count);
        var setDurations = sets.ToDictionary(s => s.GigRef, SetDurationSeconds);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = gigs.Select(g => new
        {
            gigRef = g.Ref,
            title = g.Title,
            venue = g.Venue,
            date = GigDateTimeFormatting.FormatDate(g.Date),
            // Real ISO date alongside the display string above - lets the
            // Copy Setlist grid sort by date correctly without parsing
            // the display text back apart client-side.
            sortDate = g.Date.ToString("yyyy-MM-dd"),
            time = g.Time,
            songCount = setCounts.GetValueOrDefault(g.Ref, 0),
            durationSeconds = setDurations.GetValueOrDefault(g.Ref, 0),
            duration = FormatDuration(setDurations.GetValueOrDefault(g.Ref, 0)),
            isPast = g.Date < today
        });
        return Ok(result);
    }

    // Same shape as ListGigs above, but archived gigs only - for the
    // "View Archived Gigs" grid.
    [HttpGet("gigs/archived")]
    public async Task<IActionResult> ListArchivedGigs()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bandId);
        if (band is null) return NotFound();

        var gigs = await db.Gigs.AsNoTracking().Where(g => g.BandId == bandId && g.IsArchived).ToListAsync();
        var setCounts = await db.GigSets.AsNoTracking()
            .Where(s => s.BandId == bandId)
            .Select(s => new { s.GigRef, Count = s.Songs.Count })
            .ToDictionaryAsync(x => x.GigRef, x => x.Count);

        var result = gigs.Select(g => new
        {
            gigRef = g.Ref,
            title = g.Title,
            venue = g.Venue,
            date = GigDateTimeFormatting.FormatDate(g.Date),
            sortDate = g.Date.ToString("yyyy-MM-dd"),
            time = g.Time,
            songCount = setCounts.GetValueOrDefault(g.Ref, 0),
            archivedAt = g.ArchivedAt
        });
        return Ok(result);
    }

    // Every floating setlist for this band - a real GigSet not tied to a
    // gig (see GigSet.IsFloating) - for Gig Management's Copy/Assign
    // Setlist grid and the Rehearsal modal's setlist picker.
    [HttpGet("floating")]
    public async Task<IActionResult> ListFloating()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var sets = await db.GigSets.AsNoTracking().Include(s => s.Songs).ThenInclude(gs => gs.Song)
            .Where(s => s.BandId == bandId && s.IsFloating)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(sets.Select(s => new
        {
            gigRef = s.GigRef,
            id = s.Id,
            name = s.Name ?? "Untitled setlist",
            songCount = s.Songs.Count,
            durationSeconds = SetDurationSeconds(s),
            duration = FormatDuration(SetDurationSeconds(s))
        }));
    }

    // Starts a new floating setlist, empty - the caller (Rehearsal modal,
    // or eventually anywhere else) opens the shared setlist editor on the
    // returned gigRef right after, same as building a fresh gig's set.
    [HttpPost("floating")]
    public async Task<IActionResult> CreateFloating([FromBody] CreateFloatingGigSetRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "A name is required." });

        var set = new GigSet
        {
            BandId = bandId,
            GigRef = $"floating-{Guid.NewGuid()}",
            IsFloating = true,
            Name = name[..Math.Min(name.Length, 200)]
        };
        db.GigSets.Add(set);
        await db.SaveChangesAsync();
        return Ok(new { gigRef = set.GigRef, id = set.Id, name = set.Name, songCount = 0, durationSeconds = 0, duration = FormatDuration(0) });
    }

    // Hands a floating setlist over to a gig outright - "no longer
    // floating." If the gig already has its own GigSet, that one is
    // renamed to a fresh synthetic ref and flipped to floating itself
    // (auto-named after the gig) rather than being overwritten or
    // deleted - nothing is ever lost, it just goes back into the floating
    // pool. Two SaveChanges calls, in this order, so the unique
    // (BandId, GigRef) index is never hit mid-operation: the old set's
    // GigRef is freed before the floating one claims it.
    [HttpPost("{gigRef}/assign-floating/{floatingRef}")]
    public async Task<IActionResult> AssignFloating(string gigRef, string floatingRef)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var floating = await db.GigSets.FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == floatingRef && s.IsFloating);
        if (floating is null) return NotFound(new { error = "Floating setlist not found." });

        var existing = await db.GigSets.FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        if (existing is not null)
        {
            var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.BandId == bandId && g.Ref == gigRef);
            existing.GigRef = $"floating-{Guid.NewGuid()}";
            existing.IsFloating = true;
            existing.Name = gig is not null ? $"{gig.Title} setlist" : "Unassigned setlist";
            await db.SaveChangesAsync();
        }

        floating.GigRef = gigRef;
        floating.IsFloating = false;
        floating.Name = null;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // "Everything associated with this gig" for Gig Management's past-gig
    // viewer - every ScheduleItem+Artifacts tied to it, plus its most
    // recent Flyer (and which source image built it, if that image still
    // exists - SourceCatalogItem is nullable/SetNull, see Flyer.cs). A new,
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

        // Gig.SelectedFlyerId (an explicit pick - see Gig.cs) wins when
        // set; otherwise fall back to the most recently generated Flyer,
        // preserving every gig's existing behavior from before that field
        // existed.
        var flyer = gig.SelectedFlyerId is { } selectedId
            ? await db.Flyers.AsNoTracking().Include(f => f.SourceCatalogItem).FirstOrDefaultAsync(f => f.Id == selectedId && f.BandId == bandId && !f.IsArchived)
            : null;
        flyer ??= await db.Flyers.AsNoTracking()
            .Include(f => f.SourceCatalogItem)
            .Where(f => f.BandId == bandId && f.GigRef == gigRef && !f.IsArchived)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            gig = new { title = gig.Title, venue = gig.Venue, date = GigDateTimeFormatting.FormatDate(gig.Date), flyerMain = gig.FlyerMain },
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
                sourceCatalogItemId = flyer.SourceCatalogItemId,
                sourceImageLabel = flyer.SourceCatalogItem?.Label ?? flyer.SourceCatalogItem?.OriginalFilename
            }
        });
    }

    // Copies a past gig's setlist into the current one - real songs are
    // matched by SongId (never duplicated if already in the target);
    // manual/ad-hoc entries have no SongId to dedupe on, so each one is
    // copied fresh. Appends after the target's current max SortOrder,
    // iterating the source in its own order so relative order among the
    // newly-imported songs is preserved even though absolute SortOrder
    // values get renumbered.
    [HttpPost("{gigRef}/import-from/{sourceGigRef}")]
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

        var existingSongIds = targetSet.Songs.Where(s => s.SongId is not null).Select(s => s.SongId!.Value).ToHashSet();
        var nextSort = targetSet.Songs.Count == 0 ? 0 : targetSet.Songs.Max(s => s.SortOrder) + 1;
        var imported = 0;
        foreach (var song in sourceSet.Songs.OrderBy(s => s.SortOrder))
        {
            if (song.SongId is { } sid)
            {
                if (existingSongIds.Contains(sid)) continue;
                db.GigSetSongs.Add(new GigSetSong { GigSetId = targetSet.Id, SongId = sid, SortOrder = nextSort++ });
            }
            else
            {
                db.GigSetSongs.Add(new GigSetSong
                {
                    GigSetId = targetSet.Id,
                    ManualTitle = song.ManualTitle,
                    ManualArtist = song.ManualArtist,
                    ManualLengthSeconds = song.ManualLengthSeconds,
                    ManualYouTubeUrl = song.ManualYouTubeUrl,
                    ManualSpotifyUrl = song.ManualSpotifyUrl,
                    SortOrder = nextSort++
                });
            }
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

        var songs = set?.Songs.OrderBy(gs => gs.SortOrder).Select(Serialize) ?? [];
        return Ok(new { gigRef, songs });
    }

    [HttpPost("{gigRef}/songs")]
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

    // "Add manually" and "add from web search" share this one endpoint -
    // a web-search result is just a pre-filled version of the same manual
    // entry (see RepertoireController's own search-then-manual-form
    // pattern), and neither is meant to create a permanent Song/
    // RepertoireEntry row (Song creation stays BandAdmin-only, see
    // SongsController.Create - a quick setlist add shouldn't require that).
    [HttpPost("{gigRef}/manual-songs")]
    public async Task<IActionResult> AddManualSong(string gigRef, [FromBody] AddManualGigSetSongRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return BadRequest(new { error = "Title is required." });

        var set = await db.GigSets.Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        if (set is null)
        {
            set = new GigSet { BandId = bandId, GigRef = gigRef };
            db.GigSets.Add(set);
            await db.SaveChangesAsync();
        }

        var nextSort = set.Songs.Count == 0 ? 0 : set.Songs.Max(s => s.SortOrder) + 1;
        var entry = new GigSetSong
        {
            GigSetId = set.Id,
            ManualTitle = title[..Math.Min(title.Length, 300)],
            ManualArtist = string.IsNullOrWhiteSpace(request.Artist) ? null : request.Artist.Trim(),
            ManualLengthSeconds = request.LengthSeconds,
            ManualYouTubeUrl = string.IsNullOrWhiteSpace(request.YouTubeUrl) ? null : request.YouTubeUrl.Trim(),
            ManualSpotifyUrl = string.IsNullOrWhiteSpace(request.SpotifyUrl) ? null : request.SpotifyUrl.Trim(),
            SortOrder = nextSort
        };
        db.GigSetSongs.Add(entry);
        await db.SaveChangesAsync();
        return Ok(Serialize(entry));
    }

    [HttpPut("{gigRef}/reorder")]
    public async Task<IActionResult> Reorder(string gigRef, [FromBody] ReorderGigSetRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var set = await db.GigSets.Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        if (set is null) return NotFound(new { error = "Not found" });

        var currentIds = set.Songs.Select(s => s.Id).ToHashSet();
        if (request.Ids.Count != currentIds.Count || !request.Ids.All(currentIds.Contains))
            return BadRequest(new { error = "The song list doesn't match this set - reload and try again." });

        for (var i = 0; i < request.Ids.Count; i++)
        {
            set.Songs.First(s => s.Id == request.Ids[i]).SortOrder = i;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{gigRef}/songs/{id:guid}")]
    public async Task<IActionResult> RemoveSong(string gigRef, Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var set = await db.GigSets.Include(s => s.Songs)
            .FirstOrDefaultAsync(s => s.BandId == bandId && s.GigRef == gigRef);
        var entry = set?.Songs.FirstOrDefault(s => s.Id == id);
        if (set is null || entry is null) return NotFound(new { error = "Not found" });

        set.Songs.Remove(entry);
        db.GigSetSongs.Remove(entry);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
