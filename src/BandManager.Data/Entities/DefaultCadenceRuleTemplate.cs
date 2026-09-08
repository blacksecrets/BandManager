namespace BandManager.Data.Entities;

/// <summary>
/// The starter set of CadenceRules a brand-new band gets, so it isn't
/// staring at zero posting rules (and therefore zero generated task
/// tiles) until someone builds every rule by hand. Modeled on Black
/// Secrets' own real, working cadence as of 2026-09 - the platform mix,
/// content types, and posting rhythm that's actually been running for a
/// real band - with the descriptions genericized for any band rather
/// than copied verbatim.
///
/// Lives in the DB (seeded/upserted by DbSeeder, like Platforms and
/// ContentTypes) rather than being hardcoded only in
/// SuperAdminController, so the starter set can be tuned later without a
/// code change. Applied once, at band-creation time
/// (SuperAdminController.CreateBand) - editing a template row afterward
/// doesn't retroactively touch any band's already-created CadenceRules,
/// same as ContentType/Platform edits never retroactively rewrite
/// existing data either.
/// </summary>
public class DefaultCadenceRuleTemplate
{
    public required string Key { get; set; } // stable, human-readable - e.g. "facebook-recurring-video-clip"

    public required string PlatformId { get; set; }
    public required string ContentTypeId { get; set; }

    public CadenceKind Kind { get; set; }
    public required string Category { get; set; }
    public required string Description { get; set; }

    public ScheduleType? ScheduleType { get; set; }
    public List<string>? ScheduleDays { get; set; }
    public Dictionary<string, string>? MessageTemplates { get; set; }
    public string? ManualInstructions { get; set; }

    public int SortOrder { get; set; }
}
