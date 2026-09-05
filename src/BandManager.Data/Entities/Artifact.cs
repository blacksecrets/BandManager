namespace BandManager.Data.Entities;

/// <summary>
/// One row per (ScheduleItem, artifact type) - e.g. "photo", "caption".
/// Enforced as at-most-one-current-row-per-type at the application layer
/// (delete-then-insert on replace), same as the old app's
/// clearExistingArtifact - not a DB unique constraint, since a type can
/// legitimately have zero rows.
/// </summary>
public class Artifact
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ScheduleItemId { get; set; }
    public ScheduleItem ScheduleItem { get; set; } = null!;

    public required string ArtifactType { get; set; } // "photo" | "flyer" | "video" | "audio" | "caption" | "event_url"
    public string? FilePath { get; set; }
    public string? TextValue { get; set; }

    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
