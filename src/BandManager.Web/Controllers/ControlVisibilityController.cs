using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record ControlVisibilityRuleInput(string ControlKey, string Role, bool IsVisible);
public record SetControlVisibilityRequest(List<ControlVisibilityRuleInput> Rules);

/// <summary>
/// The DB-driven per-band control-visibility system: GET returns the
/// caller's own effective show/hide map (session-cached - see
/// ControlVisibilityAccessor), PUT (BandAdmin/SuperAdmin) changes a band's
/// rules and notifies every member that a re-login is needed to see it.
/// There's no dedicated settings page for PUT yet (deferred - the pages
/// this drives, like Gig Management, are the only consumers so far); it's
/// reachable today by any BandAdmin-authenticated caller, same as every
/// other API-only admin action in this codebase before it got a page.
/// </summary>
[ApiController]
[Route("/api/control-visibility")]
[Authorize(Policy = "BandMember")]
public class ControlVisibilityController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    IControlVisibilityAccessor controlVisibilityAccessor,
    ControlVisibilityService controlVisibility) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMine() => Ok(await controlVisibilityAccessor.GetRulesAsync());

    /// <summary>Both roles' effective rules plus which keys have an explicit
    /// DB override, for whatever eventually builds a settings page on top of
    /// this (not built yet - see the class doc comment).</summary>
    [HttpGet("rules")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> GetAllRules()
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return BadRequest(new { error = "No active band selected." });

        var overrides = await db.ControlVisibilityRules.AsNoTracking()
            .Where(r => r.BandId == bandId)
            .Select(r => new { r.ControlKey, Role = r.Role.ToString() })
            .ToListAsync();
        var overrideKeys = overrides.Select(o => $"{o.ControlKey}|{o.Role}").ToHashSet();

        var forUser = await controlVisibility.GetEffectiveRulesAsync(bandId.Value, BandRole.User);
        var forAdmin = await controlVisibility.GetEffectiveRulesAsync(bandId.Value, BandRole.BandAdmin);

        var controlKeys = ControlVisibilityDefaults.ByControlKey.Keys;
        return Ok(controlKeys.Select(key => new
        {
            controlKey = key,
            user = new { isVisible = forUser[key], isOverridden = overrideKeys.Contains($"{key}|User") },
            bandAdmin = new { isVisible = forAdmin[key], isOverridden = overrideKeys.Contains($"{key}|BandAdmin") }
        }));
    }

    [HttpPut]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Set([FromBody] SetControlVisibilityRequest request)
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return BadRequest(new { error = "No active band selected." });
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var changedControlKeys = new HashSet<string>();

        foreach (var input in request.Rules)
        {
            if (!ControlVisibilityDefaults.ByControlKey.ContainsKey(input.ControlKey))
                return BadRequest(new { error = $"Unknown control: {input.ControlKey}" });
            if (!Enum.TryParse<BandRole>(input.Role, out var role))
                return BadRequest(new { error = $"Unknown role: {input.Role}" });

            var existing = await db.ControlVisibilityRules
                .FirstOrDefaultAsync(r => r.BandId == bandId && r.ControlKey == input.ControlKey && r.Role == role);

            var previousValue = existing?.IsVisible ?? ControlVisibilityDefaults.DefaultFor(input.ControlKey, role);
            if (previousValue == input.IsVisible) continue;

            changedControlKeys.Add(input.ControlKey);
            if (existing is not null)
            {
                existing.IsVisible = input.IsVisible;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedByUserId = userId.Value;
            }
            else
            {
                db.ControlVisibilityRules.Add(new ControlVisibilityRule
                {
                    BandId = bandId.Value,
                    ControlKey = input.ControlKey,
                    Role = role,
                    IsVisible = input.IsVisible,
                    UpdatedByUserId = userId.Value
                });
            }
        }

        if (changedControlKeys.Count == 0) return Ok(new { changed = false });

        // Every current member (including whoever just made this change -
        // their own browser tab is on the same stale session cache as
        // everyone else's) gets told a re-login is needed to see it. The
        // acting admin's own session is refreshed immediately below so
        // their own next request in *this* session already reflects it,
        // but their notification still explains what changed for anyone
        // reading it later or from another device.
        var memberIds = await db.BandMemberships.Where(m => m.BandId == bandId).Select(m => m.UserId).ToListAsync();
        var band = await db.Bands.FindAsync(bandId.Value);
        var what = changedControlKeys.Count == 1 ? "a control's" : $"{changedControlKeys.Count} controls'";
        var message = $"A Band Admin changed {what} visibility settings for {band?.Name}. Log out and back in to see the update.";
        foreach (var memberId in memberIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = memberId,
                BandId = bandId,
                Kind = NotificationKind.ControlVisibilityChanged,
                Message = message
            });
        }

        await db.SaveChangesAsync();
        controlVisibilityAccessor.Invalidate();
        return Ok(new { changed = true, changedControlKeys });
    }
}
