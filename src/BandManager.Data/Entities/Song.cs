namespace BandManager.Data.Entities;

// Separate from EditRequestStatus/SongEditRequest, which governs proposed
// *changes* to an already-approved Song - this is the Song's own baseline
// state, for a hand-typed brand-new catalog entry that hasn't been through
// SuperAdmin review yet (see SongsController.ProposeNew). Every
// pre-existing Song is backfilled to Approved (see the migration) - this
// workflow only applies going forward.
public enum SongStatus
{
    Approved = 0,
    PendingReview = 1,
    Rejected = 2
}

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

    // Free-form, not an enum - a fixed genre list would fight the reality
    // of a catalog seeded from many different source lists (classic rock,
    // disco, wedding-band standards...) that don't share one taxonomy.
    // GenresController.List (distinct values already in the catalog) is
    // what backs the genre selector's autocomplete, so genre entry stays
    // consistent in practice without needing to be enforced in the schema.
    public string? Genre { get; set; }
    public SongStatus Status { get; set; } = SongStatus.Approved;

    // Set when Status is PendingReview via SongsController.ProposeNew, so
    // SuperAdmin's approve-new/reject-new (unlike SongEditRequest, a brand
    // new Song has no separate request row to carry this) can notify the
    // submitter the same way SongEditRequestsController's review does.
    public Guid? ProposedByUserId { get; set; }

    public string? YouTubeUrl { get; set; }
    public string? SpotifyUrl { get; set; }
    public string? SongsterrUrl { get; set; }

    // Plain lyrics text for the stage teleprompter (LyricsPromptController/
    // prompter.html) - blank-line-separated sections are what the
    // prompter's pedal-advance treats as one scroll "page". Shared across
    // every Band using this Song, same as Tunings - edited immediately by
    // anyone, no review queue, for the same reason SetTuning skips one
    // (organically filled in by whoever has it, not curated metadata).
    public string? LyricsText { get; set; }

    // Instrument name -> tuning (e.g. "Guitar" -> "Drop D") - free-form
    // both sides since instrument rosters/naming vary per Band (see
    // BandInstrument). Whichever Band figures out a tuning first benefits
    // every other Band that later matches an instrument name to it;
    // unmatched instrument names just show blank until someone fills them
    // in, which adds a new key rather than overwriting anyone else's.
    public Dictionary<string, string>? Tunings { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
