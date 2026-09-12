using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record CreateSongRequest(string Title, string? OriginalArtist, string? Album, string? Key,
    int? LengthSeconds, string? YouTubeUrl, string? SpotifyUrl, string? SongsterrUrl);
public record UpdateSongRequest(string Title, string? OriginalArtist, string? Album, string? Key,
    int? LengthSeconds, string? YouTubeUrl, string? SpotifyUrl, string? SongsterrUrl);
public record SetTuningRequest(string Instrument, string Tuning);
public record ResolveNewSongRequest(string Message);
public record BulkResolveNewSongsRequest(List<Guid> Ids, string Message);

/// <summary>
/// The global song catalog - shared across every Band the same way
/// Platforms/ContentTypes are (see Song's doc comment). Reading (and, via
/// ProposeEdit, editing) is open to every Band member: "the entire catalog"
/// is meant to be browsable/searchable by everyone, not just admins. Create
/// stays BandAdmin (matches the existing "Add a song" flow); direct Update
/// is SuperAdmin-only now - a BandAdmin/BandMember's edit instead goes
/// through ProposeEdit and SongEditRequestsController's review queue, since
/// this Song row is shared across every Band using it. SetTuning stays
/// immediate for everyone - see SetTuning's own comment for why.
/// </summary>
[ApiController]
[Route("/api/songs")]
[Authorize(Policy = "BandMember")]
public class SongsController(ApplicationDbContext db, SongSearchService songSearch, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(Song s, Guid? pendingEditRequestId = null) => new
    {
        id = s.Id,
        title = s.Title,
        originalArtist = s.OriginalArtist,
        album = s.Album,
        key = s.Key,
        lengthSeconds = s.LengthSeconds,
        youTubeUrl = s.YouTubeUrl,
        spotifyUrl = s.SpotifyUrl,
        songsterrUrl = s.SongsterrUrl,
        tunings = s.Tunings ?? new Dictionary<string, string>(),
        status = s.Status.ToString(),
        pendingEditRequestId
    };

    // The full catalog, for the Repertoire page's "Full Song Catalog"
    // panel - everyone can browse it, per the "band members should be able
    // to call up the entire catalog" request. pendingEditRequestId lets the
    // grid show an "under review" badge without a second round-trip per row.
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var songs = await db.Songs.AsNoTracking().OrderBy(s => s.Title).ToListAsync();
        // Dictionary<Guid, Guid>.GetValueOrDefault would return Guid.Empty
        // (not null) for a song with nothing pending - TryGetValue avoids
        // that trap and actually yields null for Serialize's Guid? param.
        var pending = await db.SongEditRequests.AsNoTracking()
            .Where(r => r.Status == EditRequestStatus.Pending)
            .ToDictionaryAsync(r => r.SongId, r => r.Id);
        return Ok(songs.Select(s => Serialize(s, pending.TryGetValue(s.Id, out var reqId) ? reqId : null)));
    }

    // Search the shared catalog - every Song any Band has ever entered,
    // not just this Band's own repertoire (that's the whole point: find
    // one someone else already filled in before creating a duplicate).
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q)
    {
        var query = q?.Trim() ?? "";
        if (query.Length == 0) return Ok(Array.Empty<object>());

        var lowered = query.ToLowerInvariant();
        var songs = await db.Songs.AsNoTracking()
            .Where(s => s.Title.ToLower().Contains(lowered)
                || (s.OriginalArtist != null && s.OriginalArtist.ToLower().Contains(lowered))
                || (s.Album != null && s.Album.ToLower().Contains(lowered)))
            .OrderBy(s => s.Title)
            .Take(25)
            .ToListAsync();
        return Ok(songs.Select(s => Serialize(s)));
    }

    // Exact (case-insensitive) title+artist duplicate check - the "does
    // this already exist in the shared catalog" question the New Song
    // forms ask before creating anything, same matching rule
    // RepertoireController.Import already uses for CSV rows. Not the
    // same job as Search above (free-text, up to 25 loose matches) - this
    // is a single yes/no lookup for one specific pair.
    [HttpGet("match")]
    public async Task<IActionResult> Match([FromQuery] string title, [FromQuery] string? artist)
    {
        var titleTrimmed = title?.Trim() ?? "";
        if (titleTrimmed.Length == 0) return Ok(new { match = (object?)null });
        var artistTrimmed = artist?.Trim();

        var song = await db.Songs.AsNoTracking().FirstOrDefaultAsync(s =>
            s.Title.ToLower() == titleTrimmed.ToLower()
            && ((s.OriginalArtist == null && artistTrimmed == null) || (s.OriginalArtist != null && artistTrimmed != null && s.OriginalArtist.ToLower() == artistTrimmed.ToLower())));
        return Ok(new { match = song is null ? null : Serialize(song) });
    }

    // Live YouTube + Spotify lookup - Songsterr has no API to call (see
    // SongSearchService's doc comment), so it's never part of this result
    // set; the songsterrUrl field is always filled in by hand.
    [HttpGet("search-web")]
    public async Task<IActionResult> SearchWeb([FromQuery] string? q)
    {
        var query = q?.Trim() ?? "";
        if (query.Length == 0) return Ok(new { youTube = Array.Empty<object>(), spotify = Array.Empty<object>() });

        // Sequential, not Task.WhenAll - both calls share this request's
        // single scoped DbContext (via SongSearchService's GetCredentialAsync),
        // and EF Core throws if two operations run concurrently on the same
        // DbContext instance. Each call is already fast (one HTTP request
        // plus a cheap credential lookup), so sequential costs nothing
        // meaningful in practice.
        var youTube = await songSearch.SearchYouTubeAsync(query);
        var spotify = await songSearch.SearchSpotifyAsync(query);

        return Ok(new { youTube, spotify });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var song = await db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (song is null) return NotFound(new { error = "Not found" });
        return Ok(Serialize(song));
    }

    // For a song not already in the shared catalog - not found by search,
    // or an original the Band wrote themselves. Stays BandAdmin: unlike
    // editing an existing shared Song (which needs review, see ProposeEdit),
    // creating a brand-new catalog entry has nothing yet for a review to
    // meaningfully diff against, so it's treated like the existing
    // "Add a song" flow always has been.
    [HttpPost]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateSongRequest request)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return BadRequest(new { error = "Title is required." });

        var song = new Song
        {
            Title = title,
            OriginalArtist = request.OriginalArtist?.Trim(),
            Album = request.Album?.Trim(),
            Key = request.Key?.Trim(),
            LengthSeconds = request.LengthSeconds,
            YouTubeUrl = request.YouTubeUrl?.Trim(),
            SpotifyUrl = request.SpotifyUrl?.Trim(),
            SongsterrUrl = request.SongsterrUrl?.Trim()
        };
        db.Songs.Add(song);
        await db.SaveChangesAsync();
        return Ok(Serialize(song));
    }

    // Edits the shared entry directly, no review - SuperAdmin-only, since
    // they're the one who'd otherwise be approving this exact change (see
    // ProposeEdit for the BandAdmin/BandMember path, which stages the same
    // fields into a SongEditRequest instead of applying them here).
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSongRequest request)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return BadRequest(new { error = "Title is required." });

        var song = await db.Songs.FindAsync(id);
        if (song is null) return NotFound(new { error = "Not found" });

        song.Title = title;
        song.OriginalArtist = request.OriginalArtist?.Trim();
        song.Album = request.Album?.Trim();
        song.Key = request.Key?.Trim();
        song.LengthSeconds = request.LengthSeconds;
        song.YouTubeUrl = request.YouTubeUrl?.Trim();
        song.SpotifyUrl = request.SpotifyUrl?.Trim();
        song.SongsterrUrl = request.SongsterrUrl?.Trim();
        await db.SaveChangesAsync();
        return Ok(Serialize(song));
    }

    // The BandAdmin/BandMember edit path: stages the change as a
    // SongEditRequest instead of touching the Song, since it's shared
    // across every Band using it - see SongEditRequestsController for the
    // SuperAdmin review/approve/reject side. Only fields that actually
    // changed are stored (an empty diff is rejected outright, and a Song
    // already under review can't take a second proposal - both mirror
    // RepertoireController.Add's existing duplicate-check idiom).
    [HttpPost("{id:guid}/propose-edit")]
    public async Task<IActionResult> ProposeEdit(Guid id, [FromBody] UpdateSongRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return BadRequest(new { error = "Title is required." });

        var song = await db.Songs.FindAsync(id);
        if (song is null) return NotFound(new { error = "Not found" });

        if (await db.SongEditRequests.AnyAsync(r => r.SongId == id && r.Status == EditRequestStatus.Pending))
            return BadRequest(new { error = "This song already has an edit under review." });

        var changes = SongEditDiff.Build(
            song.Title, song.OriginalArtist, song.Album, song.Key, song.LengthSeconds, song.YouTubeUrl, song.SpotifyUrl, song.SongsterrUrl,
            title, request.OriginalArtist?.Trim(), request.Album?.Trim(), request.Key?.Trim(), request.LengthSeconds,
            request.YouTubeUrl?.Trim(), request.SpotifyUrl?.Trim(), request.SongsterrUrl?.Trim());

        if (changes.Count == 0) return BadRequest(new { error = "No changes to submit." });

        var editRequest = new SongEditRequest
        {
            SongId = id,
            RequestedByUserId = userId.Value,
            BandId = bandId,
            Changes = changes
        };
        db.SongEditRequests.Add(editRequest);
        await db.SaveChangesAsync();
        return Ok(new { ok = true, id = editRequest.Id });
    }

    // For a hand-typed song that doesn't match anything already in the
    // shared catalog (see SongEditRequestsController's doc comment on the
    // parallel review workflow this mirrors for edits) - creates the Song
    // immediately (so it's usable in the submitting Band's own repertoire
    // right away, per the "Add a song" flow this backs), but as
    // PendingReview rather than Approved, so it shows as "Under Review"
    // everywhere else until a SuperAdmin resolves it. Open to any Band
    // member, unlike Create (BandAdmin-only) - proposing something for
    // review is a lower bar than directly publishing to the shared catalog.
    [HttpPost("propose-new")]
    public async Task<IActionResult> ProposeNew([FromBody] CreateSongRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) return BadRequest(new { error = "Title is required." });

        var song = new Song
        {
            Title = title,
            OriginalArtist = request.OriginalArtist?.Trim(),
            Album = request.Album?.Trim(),
            Key = request.Key?.Trim(),
            LengthSeconds = request.LengthSeconds,
            YouTubeUrl = request.YouTubeUrl?.Trim(),
            SpotifyUrl = request.SpotifyUrl?.Trim(),
            SongsterrUrl = request.SongsterrUrl?.Trim(),
            Status = SongStatus.PendingReview,
            ProposedByUserId = userId.Value
        };
        db.Songs.Add(song);
        await db.SaveChangesAsync();
        return Ok(Serialize(song));
    }

    [HttpPost("{id:guid}/approve-new")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> ApproveNew(Guid id, [FromBody] ResolveNewSongRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });

        var (ok, error) = await ApproveNewOneAsync(id, message);
        if (!ok) return BadRequest(new { error });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/reject-new")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> RejectNew(Guid id, [FromBody] ResolveNewSongRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });

        var (ok, error) = await RejectNewOneAsync(id, message);
        if (!ok) return BadRequest(new { error });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Bulk variants for the DataGrid-based catalog grid's checkbox
    // selection, same shape as SongEditRequestsController's bulk-approve/
    // bulk-reject - one shared message, a row already resolved by the time
    // this runs is skipped rather than failing the whole batch.
    [HttpPost("bulk-approve-new")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> BulkApproveNew([FromBody] BulkResolveNewSongsRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });

        var count = 0;
        foreach (var id in request.Ids)
        {
            var (ok, _) = await ApproveNewOneAsync(id, message);
            if (ok) count++;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, count });
    }

    [HttpPost("bulk-reject-new")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> BulkRejectNew([FromBody] BulkResolveNewSongsRequest request)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "A message is required." });

        var count = 0;
        foreach (var id in request.Ids)
        {
            var (ok, _) = await RejectNewOneAsync(id, message);
            if (ok) count++;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, count });
    }

    private async Task<(bool Ok, string? Error)> ApproveNewOneAsync(Guid id, string message)
    {
        var song = await db.Songs.FindAsync(id);
        if (song is null) return (false, "Not found");
        if (song.Status != SongStatus.PendingReview) return (false, "Not awaiting review.");

        song.Status = SongStatus.Approved;
        NotifyProposer(song, message);
        return (true, null);
    }

    private async Task<(bool Ok, string? Error)> RejectNewOneAsync(Guid id, string message)
    {
        var song = await db.Songs.FindAsync(id);
        if (song is null) return (false, "Not found");
        if (song.Status != SongStatus.PendingReview) return (false, "Not awaiting review.");

        song.Status = SongStatus.Rejected;
        NotifyProposer(song, message);
        return (true, null);
    }

    // Reuses SongEditReviewed - conceptually the same "a catalog submission
    // you made was resolved" notification, just for a whole new Song
    // instead of a proposed edit to an existing one. Not every submission
    // is guaranteed to have a submitter (Status could in principle be set
    // some other way later), so this silently no-ops rather than fail the
    // whole approve/reject on a missing ProposedByUserId.
    private void NotifyProposer(Song song, string message)
    {
        if (song.ProposedByUserId is not { } proposerId) return;
        db.Notifications.Add(new Notification
        {
            UserId = proposerId,
            Message = $"{song.Title}: {message}",
            Kind = NotificationKind.SongEditReviewed
        });
    }

    // Adds/updates one instrument's tuning on the shared Song - keyed by
    // whatever instrument name this Band uses (see BandInstrument), so it
    // organically fills in for every Band as more of them contribute.
    [HttpPut("{id:guid}/tuning")]
    public async Task<IActionResult> SetTuning(Guid id, [FromBody] SetTuningRequest request)
    {
        var instrument = request.Instrument?.Trim();
        if (string.IsNullOrEmpty(instrument)) return BadRequest(new { error = "Instrument is required." });

        var song = await db.Songs.FindAsync(id);
        if (song is null) return NotFound(new { error = "Not found" });

        var tunings = song.Tunings ?? new Dictionary<string, string>();
        var tuning = request.Tuning?.Trim() ?? "";
        if (tuning.Length == 0) tunings.Remove(instrument);
        else tunings[instrument] = tuning;
        song.Tunings = tunings;
        await db.SaveChangesAsync();
        return Ok(Serialize(song));
    }
}
