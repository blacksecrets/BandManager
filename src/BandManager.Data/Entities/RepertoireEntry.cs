namespace BandManager.Data.Entities;

/// <summary>Ready to play, being worked on, or just planned - the three
/// states a Band Admin moves a song through while building the Band's
/// repertoire.</summary>
public enum RepertoireStatus
{
    New = 0,
    InProgress = 1,
    Ready = 2
}

/// <summary>
/// One Band's claim on a (globally shared) Song - the repertoire grid is
/// this table joined to Song, scoped to the active Band. Removing a
/// RepertoireEntry never touches the underlying Song (other Bands, or a
/// re-add later, still find it).
/// </summary>
public class RepertoireEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public RepertoireStatus Status { get; set; } = RepertoireStatus.New;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
