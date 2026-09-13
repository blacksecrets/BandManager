using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data.Services;

/// <summary>
/// Merges a band's ControlVisibilityRule rows over ControlVisibilityDefaults
/// to answer "what should this role see" - the one place both the
/// session-caching accessor (BandManager.Web.Auth.ControlVisibilityAccessor)
/// and the admin-facing write endpoint (ControlVisibilityController) compute
/// this, so they can never disagree.
/// </summary>
public class ControlVisibilityService(ApplicationDbContext db)
{
    /// <summary>Every ControlKey this role should see, true/false, for one band -
    /// every key in ControlVisibilityDefaults is present even with no DB row.</summary>
    public async Task<Dictionary<string, bool>> GetEffectiveRulesAsync(Guid bandId, BandRole role)
    {
        var overrides = await db.ControlVisibilityRules.AsNoTracking()
            .Where(r => r.BandId == bandId && r.Role == role)
            .ToDictionaryAsync(r => r.ControlKey, r => r.IsVisible);

        var result = new Dictionary<string, bool>();
        foreach (var controlKey in ControlVisibilityDefaults.ByControlKey.Keys)
        {
            result[controlKey] = overrides.TryGetValue(controlKey, out var isVisible)
                ? isVisible
                : ControlVisibilityDefaults.DefaultFor(controlKey, role);
        }
        return result;
    }
}
