namespace BandManager.Data.Entities;

/// <summary>
/// One of a band's performance configurations - most bands have exactly
/// one (their normal show), but a band may run a second, quite different
/// one (e.g. a full-electric tribute set vs. an acoustic/unplugged set) -
/// different gear, different stage plot, different Tech Rider entirely.
/// Every Band gets one Act automatically at creation time (see
/// SuperAdminController.CreateBand), named after the band itself, with
/// IsDefault true - that one is undeletable (ActController.Delete rejects
/// it), guaranteeing a band is never left with zero Acts. Every other Act
/// a Band Admin creates is freely deletable, as long as no Gig still
/// points at it (reassign first - see Gig.ActId).
/// </summary>
public class Act
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string Name { get; set; }
    public bool IsDefault { get; set; }

    // The Tech Rider's own content - see TechRiderInputChannel/
    // TechRiderMonitorMix/TechRiderMicEqNote for the structured sections;
    // these are the free-text parts (overview paragraph, video/general
    // notes) plus this Act's own tech contact, distinct from the Band's
    // own contact info and from a Gig's Venue/Promoter.
    public string? IntroText { get; set; }
    public string? VideoNotes { get; set; }
    public string? GeneralNotes { get; set; }
    public string? TechContactName { get; set; }
    public string? TechContactPhone { get; set; }
    public string? TechContactEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
