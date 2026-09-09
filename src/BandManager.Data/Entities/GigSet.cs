namespace BandManager.Data.Entities;

/// <summary>
/// The setlist for one gig. Gigs are real DB rows now (see Gig.cs), but
/// this still keys off the gig's own Ref string rather than a Guid FK -
/// unchanged from before that migration, since Gig.Ref is exactly the
/// same stable per-band identifier either way, and a GigSet simply
/// doesn't exist for a gig until someone starts building its set (no
/// FK to enforce eagerly).
/// </summary>
public class GigSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string GigRef { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // A "floating" setlist - GigRef is a synthetic value ("floating-" +
    // a new Guid), not a real Gig.Ref, so this row is otherwise
    // completely ordinary to every existing song-editing endpoint
    // (GetSet/AddSong/AddManualSong/Reorder/RemoveSong are all keyed
    // purely by GigRef string already, with no check that it resolves to
    // a real Gig). Name is this row's own identifying label, shown
    // wherever a real gig's Title would otherwise appear - null once
    // GigsController.AssignFloatingSetlist repoints GigRef to a real gig
    // and flips IsFloating back off.
    public bool IsFloating { get; set; }
    public string? Name { get; set; }

    public ICollection<GigSetSong> Songs { get; set; } = new List<GigSetSong>();
}

/// <summary>One ordered track in a GigSet. Usually a real Song already in
/// the owning GigSet.Band's repertoire (enforced in GigSetsController,
/// not here) - SongId set, the Manual* fields unused. SongId is null for
/// a quick ad-hoc entry that isn't meant to become a permanent repertoire
/// entry - the Manual* fields carry its display info instead, whether
/// typed by hand or prefilled from a one-off web search result (same
/// "search prefills a manual-entry form" pattern RepertoireController's
/// own add-song flow already uses). Never both null and set.</summary>
public class GigSetSong
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GigSetId { get; set; }
    public GigSet GigSet { get; set; } = null!;
    public Guid? SongId { get; set; }
    public Song? Song { get; set; }
    public string? ManualTitle { get; set; }
    public string? ManualArtist { get; set; }
    public int? ManualLengthSeconds { get; set; }
    public string? ManualYouTubeUrl { get; set; }
    public string? ManualSpotifyUrl { get; set; }
    public int SortOrder { get; set; }
}
