namespace BandManager.Data.Entities;

/// <summary>Global reference data - what a piece of content IS and what
/// it needs to be complete. Mirrors the old app's content_types table.</summary>
public class ContentType
{
    public required string Id { get; set; } // display string, e.g. "Text + Photo"
    public required List<string> RequiredArtifacts { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Global reference data - which content types are offered for a
/// given platform in the cadence/ad-hoc editors (filtering only, not a
/// hard posting constraint). Mirrors platform_content_types.</summary>
public class PlatformContentType
{
    public required string PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;

    public required string ContentTypeId { get; set; }
    public ContentType ContentType { get; set; } = null!;
}
