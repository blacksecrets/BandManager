namespace BandManager.Data.Entities;

/// <summary>
/// A media (YouTube/SoundCloud) entry, now a real DB row - same DB-is-
/// source-of-truth/site-is-a-publish-target model as Gig. Structurally
/// much simpler than a gig: no satellite entities, no CadenceRule
/// involvement, one static "keep this accurate" Scheduler tile per item.
/// Ref is the stable business key ScheduleItem.MediaRef already points at.
/// </summary>
public class MediaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string Ref { get; set; }

    public required string Title { get; set; }
    public string? Url { get; set; }
    public string? Thumbnail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
