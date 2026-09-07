using System.Text;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// SuperAdmin's bulk song-catalog import - CSV parsing/validation lives in
/// SongCsvImportService (BandManager.Data, no ASP.NET types); this
/// controller is just the IFormFile bridge, band-assignment write, and the
/// template download. Whole-file reject-on-any-error and reuse-on-duplicate
/// behavior were confirmed with the user rather than assumed - see the
/// plan.
/// </summary>
[ApiController]
[Route("/api/superadmin/songs/import")]
[Authorize(Policy = "SuperAdmin")]
public class SongCsvImportController(ApplicationDbContext db) : ControllerBase
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = Encoding.UTF8.GetBytes(SongCsvImportService.BuildTemplateCsv());
        return File(bytes, "text/csv", "song-catalog-template.csv");
    }

    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Import()
    {
        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });
        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var payload = await file.ToUploadedFilePayloadAsync();
        if (payload is null) return BadRequest(new { error = "No file provided" });

        var bandIds = new List<Guid>();
        foreach (var raw in form["bandIds"])
        {
            if (!Guid.TryParse(raw, out var bandId)) return BadRequest(new { error = $"'{raw}' isn't a valid band id." });
            bandIds.Add(bandId);
        }
        if (bandIds.Count > 0)
        {
            var validCount = await db.Bands.CountAsync(b => bandIds.Contains(b.Id) && !b.IsArchived);
            if (validCount != bandIds.Distinct().Count())
                return BadRequest(new { error = "One of the selected bands doesn't exist or is archived." });
        }

        var parsed = SongCsvImportService.Parse(payload.Bytes);
        if (parsed.Errors.Count > 0)
        {
            return BadRequest(new
            {
                error = $"{parsed.Errors.Count} row(s) failed validation - fix and re-upload.",
                rowErrors = parsed.Errors
            });
        }

        var songsCreated = 0;
        var songsReused = 0;
        var repertoireEntriesAdded = 0;

        await using var tx = await db.Database.BeginTransactionAsync();

        // De-dupe within the file itself (case-insensitive Title+Artist) so
        // two identical rows in one upload don't fight to create the Song
        // twice - later rows just resolve to the same Guid map entry.
        var resolved = new Dictionary<(string, string), Guid>();
        foreach (var row in parsed.Rows)
        {
            var key = (row.Title.ToLowerInvariant(), row.OriginalArtist.ToLowerInvariant());
            if (resolved.ContainsKey(key)) continue;

            var existing = await db.Songs.FirstOrDefaultAsync(s =>
                s.Title.ToLower() == row.Title.ToLower() && s.OriginalArtist != null && s.OriginalArtist.ToLower() == row.OriginalArtist.ToLower());

            if (existing is not null)
            {
                resolved[key] = existing.Id;
                songsReused++;
            }
            else
            {
                var song = new Song
                {
                    Title = row.Title,
                    OriginalArtist = row.OriginalArtist,
                    Album = row.Album,
                    Key = row.Key,
                    LengthSeconds = row.LengthSeconds,
                    YouTubeUrl = row.YouTubeUrl,
                    SpotifyUrl = row.SpotifyUrl,
                    SongsterrUrl = row.SongsterrUrl
                };
                db.Songs.Add(song);
                resolved[key] = song.Id;
                songsCreated++;
            }
        }
        await db.SaveChangesAsync(); // songs need real Ids before RepertoireEntry rows can reference them

        foreach (var songId in resolved.Values.Distinct())
        {
            foreach (var bandId in bandIds)
            {
                if (await db.RepertoireEntries.AnyAsync(e => e.BandId == bandId && e.SongId == songId)) continue;
                db.RepertoireEntries.Add(new RepertoireEntry { BandId = bandId, SongId = songId, Status = RepertoireStatus.New });
                repertoireEntriesAdded++;
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new { ok = true, songsCreated, songsReused, repertoireEntriesAdded });
    }
}
