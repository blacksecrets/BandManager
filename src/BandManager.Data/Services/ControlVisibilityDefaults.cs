using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>
/// The hardcoded fallback for every ControlKey a page checks, used
/// whenever a band has no ControlVisibilityRule row for that
/// (ControlKey, Role) pair - i.e. every band until a BandAdmin/SuperAdmin
/// deliberately overrides one. Keeping these here (rather than scattered
/// across each page's own JS) is what lets ControlVisibilityService
/// compute a complete answer for a band that's never touched this system.
/// A ControlKey not listed here at all defaults to visible for every
/// role - only actions with a real server-side role restriction belong
/// in this map, since hiding something a role can't do anyway is the
/// whole point (see the Gig Management set below, which mirrors
/// GigsController/FlyersController's own [Authorize(Policy="BandAdmin")]
/// actions).
/// </summary>
public static class ControlVisibilityDefaults
{
    // Declared before ByControlKey below - static field initializers run in
    // declaration order, so AdminOnly must exist before ByControlKey's
    // initializer references it, or every entry silently ends up null.
    private static readonly IReadOnlyDictionary<BandRole, bool> AdminOnly =
        new Dictionary<BandRole, bool> { [BandRole.User] = false, [BandRole.BandAdmin] = true };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<BandRole, bool>> ByControlKey =
        new Dictionary<string, IReadOnlyDictionary<BandRole, bool>>
        {
            ["gig-sets:add-gig-btn"] = AdminOnly,
            ["gig-sets:edit-btn"] = AdminOnly,
            ["gig-sets:flyer-btn"] = AdminOnly,
            ["gig-sets:select-flyer-btn"] = AdminOnly,
            ["gig-sets:archive-btn"] = AdminOnly,
        };

    public static bool DefaultFor(string controlKey, BandRole role) =>
        !ByControlKey.TryGetValue(controlKey, out var byRole) || !byRole.TryGetValue(role, out var isVisible) || isVisible;
}
