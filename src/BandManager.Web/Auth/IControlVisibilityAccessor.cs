namespace BandManager.Web.Auth;

/// <summary>
/// The effective per-control show/hide map for the caller's active band and
/// role - session-cached, refreshed only at login or a band switch (see
/// ControlVisibilityAccessor), not recomputed on every request. That
/// deliberate staleness is what makes the Notification + "log back in to
/// see it" flow in ControlVisibilityController meaningful: without it, a
/// rule change would just silently reshuffle everyone's UI mid-session.
/// Invalidate() is for the one exception - the admin who just changed a
/// rule sees their own change immediately rather than needing to re-login
/// to their own change.
/// </summary>
public interface IControlVisibilityAccessor
{
    Task<IReadOnlyDictionary<string, bool>> GetRulesAsync();
    void Invalidate();
}
