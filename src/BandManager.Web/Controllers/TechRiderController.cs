using System.Text.Json;
using BandManager.Data;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// Assembles one Act's full Tech Rider document - everything
/// print-tech-rider.html needs in a single call, mirroring the real
/// BlackSecretsTechRider.pdf's structure (Overview, Stage Plot, Input/Mic
/// Channel List, Video Note, FOH EQ Notes, Monitor Mix, Contact Info).
/// Every list here is read straight from its own table (Act,
/// StagePlotItem+BandGearItem, TechRiderInputChannel, TechRiderMonitorMix,
/// TechRiderMicEqNote) - this controller does no writing of its own, it's
/// a read-only join for the print/publish/PDF paths that all share this
/// one document shape.
/// </summary>
[ApiController]
[Route("/api/acts/{actId:guid}/tech-rider")]
[Authorize(Policy = "BandMember")]
public class TechRiderController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid actId)
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return BadRequest(new { error = "No active band selected." });

        var act = await db.Acts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == actId && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });

        var band = await db.Bands.AsNoTracking().FirstAsync(b => b.Id == bandId);

        var gearLegend = await db.StagePlotItems.AsNoTracking().Include(i => i.BandGearItem)
            .Where(i => i.StagePlot.ActId == actId)
            .OrderBy(i => i.VisibleId)
            .Select(i => new
            {
                visibleId = i.VisibleId,
                type = i.BandGearItem.Type,
                make = i.BandGearItem.Make,
                model = i.BandGearItem.Model,
                lengthInches = i.BandGearItem.LengthInches,
                widthInches = i.BandGearItem.WidthInches,
                depthInches = i.BandGearItem.DepthInches,
                weightPounds = i.BandGearItem.WeightPounds
            })
            .ToListAsync();

        var inputChannels = await db.TechRiderInputChannels.AsNoTracking()
            .Where(c => c.ActId == actId).OrderBy(c => c.SortOrder)
            .Select(c => new { c.ChannelNumber, c.Source, c.MicRecommendation, c.ProvidedBy, c.PositioningNotes })
            .ToListAsync();

        var monitorMixes = await db.TechRiderMonitorMixes.AsNoTracking()
            .Where(m => m.ActId == actId).OrderBy(m => m.SortOrder)
            .Select(m => new { m.Position, m.MixDescription })
            .ToListAsync();

        var micEqNoteRows = await db.TechRiderMicEqNotes.AsNoTracking()
            .Where(n => n.ActId == actId).OrderBy(n => n.SortOrder).ToListAsync();
        var micEqNotes = micEqNoteRows.Select(n => new
        {
            micModel = n.MicModel,
            context = n.Context,
            frequencyRows = JsonSerializer.Deserialize<List<FrequencyRowRequest>>(n.FrequencyRowsJson) ?? new List<FrequencyRowRequest>(),
            generalNotes = n.GeneralNotes
        });

        return Ok(new
        {
            actId = act.Id,
            actName = act.Name,
            bandName = band.Name,
            introText = act.IntroText,
            videoNotes = act.VideoNotes,
            generalNotes = act.GeneralNotes,
            techContactName = act.TechContactName,
            techContactPhone = act.TechContactPhone,
            techContactEmail = act.TechContactEmail,
            stagePlotImageUrl = $"/api/acts/{actId}/stage-plot/render",
            hasStagePlot = gearLegend.Count > 0,
            gearLegend,
            inputChannels,
            monitorMixes,
            micEqNotes
        });
    }
}
