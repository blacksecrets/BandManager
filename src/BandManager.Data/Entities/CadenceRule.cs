namespace BandManager.Data.Entities;

public enum CadenceKind
{
    Recurring = 0,
    GigCountdown = 1,
    GigEvent = 2,
    GigCoverPhoto = 3
}

public enum ScheduleType
{
    Weekly = 0,
    Monthly = 1
}

/// <summary>
/// Replaces the old app's hardcoded RECURRING_TEMPLATE/GIG_DRIVEN_TEMPLATE
/// - editable per Band via the Cadence page instead of code. Scoped to a
/// Band through its Account (every Account belongs to exactly one Band),
/// not a direct BandId column - mirrors the old schema's
/// cadence_rules.account_id relationship.
/// </summary>
public class CadenceRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public required string ContentTypeId { get; set; }
    public ContentType ContentType { get; set; } = null!;

    public CadenceKind Kind { get; set; }
    public required string Category { get; set; }
    public required string Description { get; set; }
    public string? Owner { get; set; }

    public ScheduleType? ScheduleType { get; set; } // null for gig_* kinds
    public List<string>? ScheduleDays { get; set; } // weekday names, or day-of-month ints/"end-of-month"
    public Dictionary<string, string>? MessageTemplates { get; set; } // gig_countdown only: {"default": "...", "2": "..."}
    public string? ManualInstructions { get; set; }

    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
