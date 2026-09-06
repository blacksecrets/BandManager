using BandManager.Data;
using BandManager.Data.Entities;
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

/// <summary>
/// The global song catalog - shared across every Band the same way
/// Platforms/ContentTypes are (see Song's doc comment). Every action here
/// is BandAdmin-only: this is purely a support surface for
/// RepertoireController's "build the repertoire" flow, not something a
/// regular User browses independently.
/// </summary>
[ApiController]
[Route("/api/songs")]
[Authorize(Policy = "BandAdmin")]
public class SongsController(ApplicationDbContext db, SongSearchService songSearch) : ControllerBase
{
    private static object Serialize(Song s) => new
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
        tunings = s.Tunings ?? new Dictionary<string, string>()
    };

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
        return Ok(songs.Select(Serialize));
    }

    // Live YouTube + Spotify lookup - Songsterr has no API to call (see
    // SongSearchService's doc comment), so it's never part of this result
    // set; the songsterrUrl field is always filled in by hand.
    [HttpGet("search-web")]
    public async Task<IActionResult> SearchWeb([FromQuery] string? q)
    {
        var query = q?.Trim() ?? "";
        if (query.Length == 0) return Ok(new { youTube = Array.Empty<object>(), spotify = Array.Empty<object>() });

        var youTubeTask = songSearch.SearchYouTubeAsync(query);
        var spotifyTask = songSearch.SearchSpotifyAsync(query);
        await Task.WhenAll(youTubeTask, spotifyTask);

        return Ok(new { youTube = youTubeTask.Result, spotify = spotifyTask.Result });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var song = await db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (song is null) return NotFound(new { error = "Not found" });
        return Ok(Serialize(song));
    }

    // For a song not already in the shared catalog - not found by search,
    // or an original the Band wrote themselves.
    [HttpPost]
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

    // Edits the shared entry itself - same "whoever fixes it helps every
    // Band" reasoning as tuning below, so there's no per-Band override of
    // title/artist/links, just one shared record everyone keeps current.
    [HttpPut("{id:guid}")]
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
