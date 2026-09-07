namespace BandManager.Data.Entities;

/// <summary>A generated flyer image, keyed to exactly one gig ("one each" -
/// re-flyering a gig just adds a new row, old ones kept for history rather
/// than being deleted). FlyerTemplateId is nullable and SetNull-deleted: a
/// flyer stays viewable even after its source template is removed - "which
/// template, if any" is exactly what Gig Management's past-gig viewer
/// needs to tolerate gracefully.</summary>
public class Flyer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public Guid GeneratedCatalogItemId { get; set; }
    public CatalogItem GeneratedCatalogItem { get; set; } = null!;

    public Guid? FlyerTemplateId { get; set; }
    public FlyerTemplate? FlyerTemplate { get; set; }

    // SiteContentRef.GigRef(gig) - the same stable ref ScheduleItem/GigSet
    // already key off, so a Flyer is correlatable with everything else
    // tied to a gig without a new join concept.
    public required string GigRef { get; set; }

    public required List<FlyerFieldDef> Fields { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
