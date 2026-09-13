using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Auth;

public class ControlVisibilityAccessor(
    IHttpContextAccessor httpContextAccessor,
    IActiveBandAccessor activeBand,
    ApplicationDbContext db,
    ControlVisibilityService controlVisibility) : IControlVisibilityAccessor
{
    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HttpContext.");

    private string? SessionKey()
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return null;
        var userId = HttpContext.User.GetUserId();
        if (userId is null) return null;
        return $"ControlVisibility:{bandId}:{userId}";
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetRulesAsync()
    {
        var key = SessionKey();
        if (key is null) return new Dictionary<string, bool>();

        var cached = HttpContext.Session.GetString(key);
        if (cached is not null)
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(cached) ?? new();

        var rules = await ComputeAsync();
        HttpContext.Session.SetString(key, JsonSerializer.Serialize(rules));
        return rules;
    }

    public void Invalidate()
    {
        var key = SessionKey();
        if (key is not null) HttpContext.Session.Remove(key);
    }

    private async Task<Dictionary<string, bool>> ComputeAsync()
    {
        var bandId = activeBand.GetActiveBandId();
        var userId = HttpContext.User.GetUserId();
        if (bandId is null || userId is null) return new Dictionary<string, bool>();

        BandRole? role = null;
        var membership = await db.BandMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.BandId == bandId);
        if (membership is not null) role = membership.Role;
        else if (HttpContext.User.IsSuperAdmin()) role = BandRole.BandAdmin;

        // No membership and not SuperAdmin: nothing to compute - the page's
        // own [Authorize] policy denies the request before this matters.
        if (role is null) return new Dictionary<string, bool>();

        return await controlVisibility.GetEffectiveRulesAsync(bandId.Value, role.Value);
    }
}
