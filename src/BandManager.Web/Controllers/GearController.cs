using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveGearRequest(string Type, string? Make, string? Model, decimal? LengthInches, decimal? WidthInches, decimal? DepthInches, decimal? WeightPounds);
public record SaveGearSettingRequest(string Name, string? Value);

/// <summary>
/// A member's personal gear inventory - self-service, not band-scoped
/// (follows the person across every band, like their profile). See
/// Gear.cs/GearTypes.cs's doc comments for why "settings" start from a
/// per-type template of blank fields rather than any live lookup.
/// </summary>
[ApiController]
[Route("/api/gear")]
[Authorize]
public class GearController(ApplicationDbContext db) : ControllerBase
{
    private static object Serialize(Gear g) => new
    {
        id = g.Id,
        type = g.Type,
        make = g.Make,
        model = g.Model,
        lengthInches = g.LengthInches,
        widthInches = g.WidthInches,
        depthInches = g.DepthInches,
        weightPounds = g.WeightPounds,
        settings = g.Settings.OrderBy(s => s.SortOrder).Select(s => new { id = s.Id, name = s.Name, value = s.Value })
    };

    [HttpGet("types")]
    public IActionResult Types() => Ok(new
    {
        types = GearTypes.AllTypes,
        defaultSettings = GearTypes.DefaultSettings
    });

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var gear = await db.Gear.AsNoTracking().Include(g => g.Settings)
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.SortOrder)
            .ToListAsync();
        return Ok(gear.Select(Serialize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveGearRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var type = request.Type?.Trim();
        if (string.IsNullOrEmpty(type)) return BadRequest(new { error = "Gear type is required." });

        var maxSort = await db.Gear.Where(g => g.UserId == userId).Select(g => (int?)g.SortOrder).MaxAsync() ?? -1;
        var gear = new Gear
        {
            UserId = userId.Value,
            Type = type,
            Make = string.IsNullOrWhiteSpace(request.Make) ? null : request.Make.Trim(),
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
            LengthInches = request.LengthInches,
            WidthInches = request.WidthInches,
            DepthInches = request.DepthInches,
            WeightPounds = request.WeightPounds,
            SortOrder = maxSort + 1
        };
        db.Gear.Add(gear);

        // Pre-populate this type's usual settings as blanks, per
        // GearTypes.cs - "just save it" still works fine even for a type
        // with none, or one not in the picklist at all (a free-typed
        // "Other"-style entry gets zero starting settings, not an error).
        if (GearTypes.DefaultSettings.TryGetValue(type, out var defaults))
        {
            var sort = 0;
            foreach (var name in defaults)
            {
                db.GearSettings.Add(new GearSetting { GearId = gear.Id, Gear = gear, Name = name, Value = null, SortOrder = sort++ });
            }
        }

        await db.SaveChangesAsync();
        var saved = await db.Gear.Include(g => g.Settings).FirstAsync(g => g.Id == gear.Id);
        return Ok(Serialize(saved));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveGearRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var gear = await db.Gear.Include(g => g.Settings).FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
        if (gear is null) return NotFound(new { error = "Not found" });

        var type = request.Type?.Trim();
        if (string.IsNullOrEmpty(type)) return BadRequest(new { error = "Gear type is required." });

        gear.Type = type;
        gear.Make = string.IsNullOrWhiteSpace(request.Make) ? null : request.Make.Trim();
        gear.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        gear.LengthInches = request.LengthInches;
        gear.WidthInches = request.WidthInches;
        gear.DepthInches = request.DepthInches;
        gear.WeightPounds = request.WeightPounds;
        await db.SaveChangesAsync();
        return Ok(Serialize(gear));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var gear = await db.Gear.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
        if (gear is null) return Ok(new { ok = true });

        db.Gear.Remove(gear); // cascades GearSettings
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private async Task<Gear?> RequireOwnedGearAsync(Guid gearId, Guid userId) =>
        await db.Gear.Include(g => g.Settings).FirstOrDefaultAsync(g => g.Id == gearId && g.UserId == userId);

    [HttpPost("{gearId:guid}/settings")]
    public async Task<IActionResult> AddSetting(Guid gearId, [FromBody] SaveGearSettingRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var gear = await RequireOwnedGearAsync(gearId, userId.Value);
        if (gear is null) return NotFound(new { error = "Not found" });

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Setting name is required." });

        var nextSort = gear.Settings.Count == 0 ? 0 : gear.Settings.Max(s => s.SortOrder) + 1;
        var setting = new GearSetting { GearId = gear.Id, Name = name, Value = request.Value?.Trim(), SortOrder = nextSort };
        db.GearSettings.Add(setting);
        await db.SaveChangesAsync();
        return Ok(new { id = setting.Id, name = setting.Name, value = setting.Value });
    }

    [HttpPut("{gearId:guid}/settings/{settingId:guid}")]
    public async Task<IActionResult> UpdateSetting(Guid gearId, Guid settingId, [FromBody] SaveGearSettingRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var gear = await RequireOwnedGearAsync(gearId, userId.Value);
        var setting = gear?.Settings.FirstOrDefault(s => s.Id == settingId);
        if (gear is null || setting is null) return NotFound(new { error = "Not found" });

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Setting name is required." });

        setting.Name = name;
        setting.Value = request.Value?.Trim();
        await db.SaveChangesAsync();
        return Ok(new { id = setting.Id, name = setting.Name, value = setting.Value });
    }

    [HttpDelete("{gearId:guid}/settings/{settingId:guid}")]
    public async Task<IActionResult> DeleteSetting(Guid gearId, Guid settingId)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var gear = await RequireOwnedGearAsync(gearId, userId.Value);
        var setting = gear?.Settings.FirstOrDefault(s => s.Id == settingId);
        if (gear is null || setting is null) return Ok(new { ok = true });

        db.GearSettings.Remove(setting);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
