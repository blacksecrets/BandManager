namespace BandManager.Data.Entities;

/// <summary>
/// One row per instrument slot a Band wants tuning tracked for (e.g.
/// "Lead Guitar", "Bass", "Guitar 2") - purely a label list a BandAdmin
/// maintains for their own Band; matched against Song.Tunings by name
/// when rendering the repertoire grid.
/// </summary>
public class BandInstrument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}
