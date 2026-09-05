namespace BandManager.Data.Entities;

public enum ScheduleItemStatus { Open, Posted, Cancelled }

/// <summary>
/// One tile on the Dashboard/board. Ported 1:1 from the old app's
/// schedule_items table (see src/statusEngine.js for the status-derivation
/// logic this drives, and src/scheduler.js for how these get generated
/// from a CadenceRule). BandId is a direct column (not just derived via
/// Account) because no_api items can exist with no Account at all.
/// </summary>
public class ScheduleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }

    // Idempotency key for cadence generation - INSERT-if-not-exists on
    // (BandId, TemplateKey, DueDate, GigRef), mirrors the old app's
    // UNIQUE(template_key, due_date, gig_ref).
    public required string TemplateKey { get; set; }

    public required string Platform { get; set; } // display name, kept alongside AccountId same as the old schema
    public required string Owner { get; set; }
    public required string ContentType { get; set; }
    public required string Category { get; set; }
    public string? Example { get; set; }
    public DateOnly? DueDate { get; set; }

    public bool NoApi { get; set; }
    public bool AutoHandled { get; set; }
    public string? GigRef { get; set; }
    public string? MediaRef { get; set; }
    public string? GalleryRef { get; set; }

    public ScheduleItemStatus Status { get; set; } = ScheduleItemStatus.Open;
    public bool ArtifactsOwed { get; set; }

    public DateTime? PostedAt { get; set; }
    public string? PostedVia { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Artifact> Artifacts { get; set; } = new List<Artifact>();
}
