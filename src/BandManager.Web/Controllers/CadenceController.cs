using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record CadenceRuleBody(
    Guid? AccountId, string? ContentTypeId, string? Kind, string? Category, string? Description,
    Guid? AssigneeUserId1, Guid? AssigneeUserId2, string? ScheduleType, List<string>? ScheduleDays,
    Dictionary<string, string>? MessageTemplates, string? ManualInstructions, bool? Active);

/// <summary>
/// Cadence rule CRUD - ported from the old app's routes/cadence.js,
/// scoped to the active Band (an Account, and therefore any CadenceRule
/// on it, always belongs to exactly one Band - the AccountId ownership
/// check below is what prevents a BandAdmin of Band A from pointing a
/// rule at Band B's Account). Response field names stay snake_case to
/// match wwwroot/assets/cadence.js's existing expectations; request
/// bodies stay camelCase, matching the old app's own asymmetry (its
/// request bodies were already camelCase, only DB-row-spread responses
/// were snake_case).
/// </summary>
[ApiController]
[Authorize(Policy = "BandMember")]
public class CadenceController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private static readonly HashSet<string> ValidKinds = ["recurring", "gig_countdown", "gig_event", "gig_cover_photo"];
    private static readonly HashSet<string> ValidWeekdays =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    [HttpGet("/api/content-types")]
    public async Task<IActionResult> ContentTypes()
    {
        var rows = await db.ContentTypes.OrderBy(c => c.SortOrder).ToListAsync();
        return Ok(rows.Select(c => new { id = c.Id, required_artifacts = c.RequiredArtifacts, sort_order = c.SortOrder }));
    }

    [HttpGet("/api/platforms/{platformId}/content-types")]
    public async Task<IActionResult> ContentTypesForPlatform(string platformId)
    {
        var rows = await db.PlatformContentTypes
            .Where(pct => pct.PlatformId == platformId)
            .Include(pct => pct.ContentType)
            .OrderBy(pct => pct.ContentType.SortOrder)
            .Select(pct => pct.ContentType)
            .ToListAsync();
        return Ok(rows.Select(c => new { id = c.Id, required_artifacts = c.RequiredArtifacts, sort_order = c.SortOrder }));
    }

    [HttpGet("/api/cadence-rules")]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var rules = await db.CadenceRules
            .Include(r => r.Account).ThenInclude(a => a.Platform)
            .Where(r => r.Account.BandId == bandId)
            .OrderBy(r => r.Account.Platform.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();

        return Ok(rules.Select(Serialize));
    }

    [HttpPost("/api/cadence-rules")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Create([FromBody] CadenceRuleBody body)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var (validationError, account) = await ValidateAsync(body, bandId, existingKind: null);
        if (validationError is not null) return BadRequest(new { error = validationError });
        if (await ValidateAssigneesAsync(body.AssigneeUserId1, body.AssigneeUserId2, bandId) is { } assigneeError)
            return BadRequest(new { error = assigneeError });

        var rule = new CadenceRule
        {
            AccountId = account!.Id,
            ContentTypeId = body.ContentTypeId!,
            Kind = Enum.Parse<CadenceKind>(ToPascalCase(body.Kind!)),
            Category = body.Category!.Trim(),
            Description = body.Description!.Trim(),
            AssigneeUserId1 = body.AssigneeUserId1,
            AssigneeUserId2 = body.AssigneeUserId2,
            ScheduleType = body.Kind == "recurring" ? Enum.Parse<ScheduleType>(ToPascalCase(body.ScheduleType!)) : null,
            ScheduleDays = body.Kind == "recurring" ? body.ScheduleDays : null,
            MessageTemplates = body.Kind == "gig_countdown" ? body.MessageTemplates : null,
            ManualInstructions = Truncate(body.ManualInstructions, 2000),
            Active = true
        };
        db.CadenceRules.Add(rule);
        await db.SaveChangesAsync();

        rule.Account = account;
        return Ok(Serialize(rule));
    }

    [HttpPut("/api/cadence-rules/{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CadenceRuleBody body)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var existing = await db.CadenceRules.Include(r => r.Account).ThenInclude(a => a.Platform)
            .FirstOrDefaultAsync(r => r.Id == id && r.Account.BandId == bandId);
        if (existing is null) return NotFound(new { error = "Not found" });

        var mergedKind = existing.Kind.ToString();
        var (validationError, _) = await ValidateAsync(body with
        {
            AccountId = body.AccountId ?? existing.AccountId,
            ContentTypeId = body.ContentTypeId ?? existing.ContentTypeId,
            Kind = mergedKind,
            Category = body.Category ?? existing.Category,
            Description = body.Description ?? existing.Description,
            ScheduleType = body.ScheduleType ?? existing.ScheduleType?.ToString(),
            ScheduleDays = body.ScheduleDays ?? existing.ScheduleDays
        }, bandId, existingKind: existing.Kind);
        if (validationError is not null) return BadRequest(new { error = validationError });

        // Unlike the other fields on this endpoint, assignees are always
        // sent as the complete new pair by the picker UI (not a partial
        // patch) - so this can't coalesce with null-meaning-"unchanged"
        // the way Category/Description do, or clearing an assignee down
        // to "nobody" would be impossible to express.
        if (await ValidateAssigneesAsync(body.AssigneeUserId1, body.AssigneeUserId2, bandId) is { } assigneeError)
            return BadRequest(new { error = assigneeError });

        existing.Category = (body.Category ?? existing.Category).Trim();
        existing.Description = (body.Description ?? existing.Description).Trim();
        existing.AssigneeUserId1 = body.AssigneeUserId1;
        existing.AssigneeUserId2 = body.AssigneeUserId2;
        if (existing.Kind == CadenceKind.Recurring)
        {
            if (body.ScheduleType is not null) existing.ScheduleType = Enum.Parse<ScheduleType>(ToPascalCase(body.ScheduleType));
            if (body.ScheduleDays is not null) existing.ScheduleDays = body.ScheduleDays;
        }
        if (existing.Kind == CadenceKind.GigCountdown && body.MessageTemplates is not null)
        {
            existing.MessageTemplates = body.MessageTemplates;
        }
        if (body.ManualInstructions is not null) existing.ManualInstructions = Truncate(body.ManualInstructions, 2000);
        if (body.Active is not null) existing.Active = body.Active.Value;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(Serialize(existing));
    }

    [HttpDelete("/api/cadence-rules/{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var existing = await db.CadenceRules.Include(r => r.Account)
            .FirstOrDefaultAsync(r => r.Id == id && r.Account.BandId == bandId);
        if (existing is null) return NotFound(new { error = "Not found" });

        db.CadenceRules.Remove(existing);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private async Task<string?> ValidateAssigneesAsync(Guid? assignee1, Guid? assignee2, Guid bandId)
    {
        if (assignee1 is not null && assignee1 == assignee2) return "Can't assign the same person twice.";
        foreach (var id in new[] { assignee1, assignee2 })
        {
            if (id is null) continue;
            if (!await db.BandMemberships.AnyAsync(m => m.UserId == id && m.BandId == bandId))
                return "Assignee must be a member of this band.";
        }
        return null;
    }

    private async Task<(string? Error, Account? Account)> ValidateAsync(CadenceRuleBody body, Guid bandId, CadenceKind? existingKind)
    {
        var kindStr = body.Kind;
        if (kindStr is null || !ValidKinds.Contains(kindStr)) return ("Invalid kind", null);

        if (body.AccountId is not { } accountId) return ("accountId is required", null);
        var account = await db.Accounts.Include(a => a.Platform).FirstOrDefaultAsync(a => a.Id == accountId && a.BandId == bandId);
        if (account is null) return ("Unknown account", null);
        if (kindStr == "gig_cover_photo" && account.Platform.DisplayName != "Facebook")
            return ("Cover photo rules are Facebook-only", null);

        if (string.IsNullOrEmpty(body.ContentTypeId) || !await db.ContentTypes.AnyAsync(c => c.Id == body.ContentTypeId))
            return ("Unknown content type", null);

        var category = body.Category?.Trim() ?? "";
        if (category.Length == 0 || category.Length > 200) return ("Category is required (max 200 chars)", null);

        var description = body.Description?.Trim() ?? "";
        if (description.Length == 0 || description.Length > 1000) return ("Description is required (max 1000 chars)", null);

        if (kindStr == "recurring")
        {
            if (body.ScheduleType != "weekly" && body.ScheduleType != "monthly")
                return ("scheduleType must be weekly or monthly", null);
            if (body.ScheduleDays is null || body.ScheduleDays.Count == 0)
                return ("At least one schedule day is required", null);
            if (body.ScheduleType == "weekly")
            {
                if (!body.ScheduleDays.All(ValidWeekdays.Contains)) return ("Invalid weekday in scheduleDays", null);
            }
            else
            {
                if (!body.ScheduleDays.All(d => d == "end-of-month" || (int.TryParse(d, out var n) && n is >= 1 and <= 31)))
                    return ("Invalid day-of-month in scheduleDays", null);
            }
        }

        return (null, account);
    }

    private static object Serialize(CadenceRule r) => new
    {
        id = r.Id,
        account_id = r.AccountId,
        platform_id = r.Account.PlatformId,
        platform_name = r.Account.Platform.DisplayName,
        content_type_id = r.ContentTypeId,
        kind = ToSnakeCase(r.Kind.ToString()),
        category = r.Category,
        description = r.Description,
        assignee_user_id1 = r.AssigneeUserId1,
        assignee_user_id2 = r.AssigneeUserId2,
        schedule_type = r.ScheduleType is null ? null : ToSnakeCase(r.ScheduleType.ToString()!),
        schedule_days = r.ScheduleDays,
        message_templates = r.MessageTemplates,
        manual_instructions = r.ManualInstructions,
        active = r.Active,
        created_at = r.CreatedAt,
        updated_at = r.UpdatedAt
    };

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    private static string ToPascalCase(string snakeOrLower)
    {
        var parts = snakeOrLower.Split('_');
        return string.Concat(parts.Select(p => p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string ToSnakeCase(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }
}
