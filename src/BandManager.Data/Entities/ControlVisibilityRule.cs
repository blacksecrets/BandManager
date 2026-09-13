namespace BandManager.Data.Entities;

/// <summary>
/// Per-band override of whether a named UI control is shown to a given
/// role - the DB-driven half of the role-visibility design. A missing row
/// for a (BandId, ControlKey, Role) triple means "use that control's
/// hardcoded default" (see ControlVisibilityDefaults) - only bands that
/// actually want to deviate from the shipped default need a row here.
/// Loaded into session at login/band-switch (see
/// ControlVisibilityAccessor), not read fresh on every request, so a
/// change one BandAdmin makes doesn't silently reshuffle another member's
/// UI mid-session - ControlVisibilityController notifies affected members
/// and they pick up the change on their next login instead.
/// </summary>
public class ControlVisibilityRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string ControlKey { get; set; }
    public BandRole Role { get; set; }
    public bool IsVisible { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
    public ApplicationUser UpdatedByUser { get; set; } = null!;
}
