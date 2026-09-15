namespace BandManager.Data.Entities;

/// <summary>
/// One Band's stage video for one of its RepertoireEntry rows - deliberately
/// hung off RepertoireEntry, not the shared Song, unlike lyrics/tunings:
/// two Bands covering the same Song have completely different stage videos,
/// so this needs to be Band-private the way RepertoireEntry itself already
/// is, not shared catalog data. The actual file lives in the normal Catalog
/// system (CatalogItem/CatalogStore) - this row is just the "which catalog
/// item, and what do we know about it" attachment point, one-to-one with a
/// RepertoireEntry.
/// </summary>
public class RepertoireEntryVideo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepertoireEntryId { get; set; }
    public RepertoireEntry RepertoireEntry { get; set; } = null!;

    public Guid? CatalogItemId { get; set; }
    public CatalogItem? CatalogItem { get; set; }

    // Cached at upload time from <video>.duration - CatalogItem itself
    // doesn't track media duration for video (only Width/Height, and only
    // for images), and the compiled per-gig sequence needs every song's
    // duration up front to lay out one continuous timeline without having
    // to load every video just to ask it.
    public double? DurationSeconds { get; set; }

    // False until a human has looked at (and, if needed, corrected) the
    // AI-detected tempo segments - drives a "needs review" badge rather
    // than silently trusting an automated guess for a live show.
    public bool ClickTrackConfirmed { get; set; }

    public ICollection<RepertoireEntryVideoBreakpoint> Breakpoints { get; set; } = new List<RepertoireEntryVideoBreakpoint>();
    public ICollection<RepertoireEntryVideoTempoSegment> TempoSegments { get; set; } = new List<RepertoireEntryVideoTempoSegment>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A named jump-point within one RepertoireEntryVideo, for the Stage Video
/// Player's breakpoint-jump controls. Exactly one row per video has
/// IsSongStart = true (auto-created when a video is first attached,
/// default TimestampSeconds 0 but editable - some videos have lead-in/
/// countdown footage before the song actually starts); the controller
/// enforces that row can't be deleted, only every other row can be freely
/// added/renamed/reordered/removed. "Song end" is deliberately not a
/// stored breakpoint - it's always just the video's own duration.
/// </summary>
public class RepertoireEntryVideoBreakpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepertoireEntryVideoId { get; set; }
    public RepertoireEntryVideo RepertoireEntryVideo { get; set; } = null!;

    public required string Label { get; set; }
    public double TimestampSeconds { get; set; }
    public int SortOrder { get; set; }
    public bool IsSongStart { get; set; }
}

/// <summary>
/// One tempo segment within a RepertoireEntryVideo's click track - a list,
/// not a single BPM, because a song's tempo can genuinely change partway
/// through (a slow intro, a double-time outro). Each segment covers from
/// its own StartTimestampSeconds until the next segment's start (or the
/// video's end for the last one). BeatOffsetSeconds is the phase within
/// this segment - where the first beat actually lands, since a segment
/// rarely starts exactly on a downbeat. Populated either by the AI
/// tempo-detection pass (see tempoDetect.js) or by hand in the correction
/// editor; RepertoireEntryVideo.ClickTrackConfirmed tracks whether a human
/// has ever reviewed this list.
/// </summary>
public class RepertoireEntryVideoTempoSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepertoireEntryVideoId { get; set; }
    public RepertoireEntryVideo RepertoireEntryVideo { get; set; } = null!;

    public double StartTimestampSeconds { get; set; }
    public decimal Bpm { get; set; }
    public decimal BeatOffsetSeconds { get; set; }
    public int SortOrder { get; set; }
}
