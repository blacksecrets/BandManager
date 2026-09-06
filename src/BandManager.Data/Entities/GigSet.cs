namespace BandManager.Data.Entities;

/// <summary>
/// The setlist for one gig. Gigs themselves aren't stored in this DB at
/// all - they live on the Band's own live site and are read live via
/// GigsSource, the same source ScheduleItems' GigRef already points at -
/// so this keys off that same ref (SiteContentRef.GigRef) rather than a
/// foreign key, and simply doesn't exist for a gig until someone starts
/// building its set.
/// </summary>
public class GigSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string GigRef { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GigSetSong> Songs { get; set; } = new List<GigSetSong>();
}

/// <summary>One ordered track in a GigSet - always a Song already in the
/// owning GigSet.Band's repertoire (enforced in GigSetsController, not
/// here), same "this table just orders/joins, the API enforces the real
/// rule" split used elsewhere.</summary>
public class GigSetSong
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GigSetId { get; set; }
    public GigSet GigSet { get; set; } = null!;
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public int SortOrder { get; set; }
}
