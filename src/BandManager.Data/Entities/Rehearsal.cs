namespace BandManager.Data.Entities;

/// <summary>
/// One rehearsal on the Calendar - open to every band member to create/
/// edit/delete (not just BandAdmin), per the explicit "all band members
/// can manage rehearsal entries" request. RecurringRehearsalRuleId is set
/// only for an instance materialized from an active rule (see
/// RecurringRehearsalRule.cs and Scheduler.GenerateRehearsalsFromRulesAsync) -
/// null for a one-off rehearsal any member scheduled by hand. Deleting the
/// rule this came from doesn't retroactively delete already-materialized
/// instances (SetNull), matching Flyer/FlyerTemplate's precedent for "the
/// thing that generated this is gone, but this itself still stands."
/// </summary>
public class Rehearsal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public string? Title { get; set; }
    public string? Location { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    public Guid? RecurringRehearsalRuleId { get; set; }
    public RecurringRehearsalRule? RecurringRehearsalRule { get; set; }

    public Guid CreatedByUserId { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A BandAdmin-managed recurring rehearsal schedule (e.g. "every Tuesday,
/// 7pm, 2 hours, at the practice space") - materialized into real
/// Rehearsal rows on the same rolling-3-week-ahead, idempotent-by-
/// existence-check pattern CadenceRule's Recurring kind already
/// establishes (see Scheduler.GenerateRehearsalsFromRulesAsync).
/// DayOfWeek is a string ("Monday".."Sunday") matching CadenceRule.
/// ScheduleDays' existing convention, not the .NET DayOfWeek enum, so the
/// same weekday-name helpers (Scheduler.WeekdayDueDates) apply unchanged.
/// </summary>
public class RecurringRehearsalRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string DayOfWeek { get; set; }
    public required TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; } = 120;
    public string? Location { get; set; }
    public bool Active { get; set; } = true;

    public Guid CreatedByUserId { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
