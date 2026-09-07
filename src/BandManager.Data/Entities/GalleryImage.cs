namespace BandManager.Data.Entities;

/// <summary>
/// A gallery photo, now a real DB row - same DB-is-source-of-truth/site-
/// is-a-publish-target model as Gig. Structurally much simpler than a gig:
/// no satellite entities, no CadenceRule involvement, one static "keep
/// this accurate" Scheduler tile per item. Ref is the stable business key
/// ScheduleItem.GalleryRef already points at.
/// </summary>
public class GalleryImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string Ref { get; set; }

    public string? Alt { get; set; }
    public string? Thumb { get; set; }
    public string? Full { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
