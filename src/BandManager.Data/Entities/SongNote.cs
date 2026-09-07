namespace BandManager.Data.Entities;

/// <summary>
/// One band member's own personal note on one song in their band's
/// repertoire ("capo 3", "watch the key change in the bridge", etc.) -
/// keyed off RepertoireEntry (not directly off Song) since Song is a
/// globally shared catalog row and a note is inherently about how THIS
/// band plays it, not a fact about the song itself. Every member has
/// their own row per song they've bothered to annotate (unique on
/// (UserId, RepertoireEntryId)) - shown/edited as "my note" in the
/// Repertoire grid, and offered a la carte per user when printing a
/// setlist (see SongNotesController, PrintSetlistController).
/// </summary>
public class SongNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid RepertoireEntryId { get; set; }
    public RepertoireEntry RepertoireEntry { get; set; } = null!;
    public required string Text { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
