namespace BandManager.Data.Entities;

/// <summary>
/// A song - global/shared across every Band, the same way Platforms and
/// ContentTypes are, so one Band adding "Enter Sandman" with its tuning/
/// links means every other Band finds it already filled in instead of
/// re-entering it. A Band's own repertoire is a separate RepertoireEntry
/// row pointing at one of these; a Song with no RepertoireEntry anywhere
/// is just an unused catalog entry, never cleaned up automatically (same
/// "small, never worth garbage-collecting" reasoning as Platforms).
/// </summary>
public class Song
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? OriginalArtist { get; set; }
    public string? Album { get; set; }
    public string? Key { get; set; }
    public int? LengthSeconds { get; set; }

    public string? YouTubeUrl { get; set; }
    public string? SpotifyUrl { get; set; }
    public string? SongsterrUrl { get; set; }

    // Instrument name -> tuning (e.g. "Guitar" -> "Drop D") - free-form
    // both sides since instrument rosters/naming vary per Band (see
    // BandInstrument). Whichever Band figures out a tuning first benefits
    // every other Band that later matches an instrument name to it;
    // unmatched instrument names just show blank until someone fills them
    // in, which adds a new key rather than overwriting anyone else's.
    public Dictionary<string, string>? Tunings { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
