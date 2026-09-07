using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveVenueCadenceStepRequest(int DaysAfterPrevious, string Type, string? DefaultSubject, string DefaultBody, bool Active);

/// <summary>
/// This band's outreach cadence template - BandAdmin-managed, like
/// BandInstrument/CadenceRule (a policy setting for the whole band, not
/// something every member tunes). See VenueCadenceStep.cs.
/// </summary>
[ApiController]
[Route("/api/venue-cadence-steps")]
[Authorize(Policy = "BandAdmin")]
public class VenueCadenceStepsController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(VenueCadenceStep s) => new
    {
        id = s.Id,
        stepNumber = s.StepNumber,
        daysAfterPrevious = s.DaysAfterPrevious,
        type = s.Type.ToString(),
        defaultSubject = s.DefaultSubject,
        defaultBody = s.DefaultBody,
        active = s.Active
    };

    [HttpGet]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var steps = await db.VenueCadenceSteps.AsNoTracking().Where(s => s.BandId == bandId).OrderBy(s => s.StepNumber).ToListAsync();
        return Ok(steps.Select(Serialize));
    }

    // Appends the next step number - the sequence is always 1..N, in
    // order; there's no reordering (deleting the last one and re-adding
    // is the way to change the end of the sequence).
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveVenueCadenceStepRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!Enum.TryParse<CommunicationType>(request.Type, ignoreCase: true, out var type))
            return BadRequest(new { error = "Type must be \"Email\" or \"Phone\"." });
        var body = request.DefaultBody?.Trim();
        if (string.IsNullOrEmpty(body)) return BadRequest(new { error = "A default body/script is required." });

        var nextStep = await db.VenueCadenceSteps.Where(s => s.BandId == bandId).Select(s => (int?)s.StepNumber).MaxAsync() is { } max ? max + 1 : 1;
        var step = new VenueCadenceStep
        {
            BandId = bandId,
            StepNumber = nextStep,
            DaysAfterPrevious = Math.Max(0, request.DaysAfterPrevious),
            Type = type,
            DefaultSubject = string.IsNullOrWhiteSpace(request.DefaultSubject) ? null : request.DefaultSubject.Trim(),
            DefaultBody = body,
            Active = request.Active
        };
        db.VenueCadenceSteps.Add(step);
        await db.SaveChangesAsync();
        return Ok(Serialize(step));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveVenueCadenceStepRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var step = await db.VenueCadenceSteps.FirstOrDefaultAsync(s => s.Id == id && s.BandId == bandId);
        if (step is null) return NotFound(new { error = "Not found" });

        if (!Enum.TryParse<CommunicationType>(request.Type, ignoreCase: true, out var type))
            return BadRequest(new { error = "Type must be \"Email\" or \"Phone\"." });
        var body = request.DefaultBody?.Trim();
        if (string.IsNullOrEmpty(body)) return BadRequest(new { error = "A default body/script is required." });

        step.DaysAfterPrevious = Math.Max(0, request.DaysAfterPrevious);
        step.Type = type;
        step.DefaultSubject = string.IsNullOrWhiteSpace(request.DefaultSubject) ? null : request.DefaultSubject.Trim();
        step.DefaultBody = body;
        step.Active = request.Active;
        await db.SaveChangesAsync();
        return Ok(Serialize(step));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var step = await db.VenueCadenceSteps.FirstOrDefaultAsync(s => s.Id == id && s.BandId == bandId);
        if (step is null) return Ok(new { ok = true });

        db.VenueCadenceSteps.Remove(step);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
