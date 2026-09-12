using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Playwright;
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
public class TechRiderController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    TechRiderPdfService pdfService,
    ITechRiderSitePublisher siteEditor,
    IBandSiteConnection bandSiteConnection) : ControllerBase
{
    private async Task<byte[]> GeneratePdfBytesAsync(Guid actId)
    {
        const string internalHost = "localhost";
        var cookies = new List<PdfCookie>();
        foreach (var name in new[] { "BandManager.Auth", "BandManager.Session" })
        {
            if (Request.Cookies.TryGetValue(name, out var value))
                cookies.Add(new PdfCookie(name, value, internalHost, "/"));
        }
        var printUrl = $"http://{internalHost}:8080/print-tech-rider.html?actId={actId}";
        return await pdfService.GeneratePdfAsync(printUrl, cookies);
    }

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

    // Headless-prints print-tech-rider.html to PDF (see
    // TechRiderPdfService.cs) - the caller's own auth/session cookies are
    // forwarded so the internal request renders exactly what they'd see
    // themselves, no separate export-auth mechanism needed.
    [HttpGet("pdf")]
    public async Task<IActionResult> DownloadPdf(Guid actId)
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return BadRequest(new { error = "No active band selected." });

        var act = await db.Acts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == actId && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });

        byte[] pdf;
        try { pdf = await GeneratePdfBytesAsync(actId); }
        catch (Exception ex) when (ex is InvalidOperationException or PlaywrightException)
        {
            return StatusCode(502, new { error = $"Could not generate the PDF: {ex.Message}" });
        }

        var fileName = $"{act.Name.Replace(' ', '-')}-TechRider.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    // Pushes this Act's PDF to the band's connected site (best-effort
    // epk.html Downloads link patch included) - a no-op, not an error,
    // for a band with no site configured, matching every other
    // site-publishing feature in this app.
    [HttpPost("publish")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Publish(Guid actId)
    {
        var bandId = activeBand.GetActiveBandId();
        if (bandId is null) return BadRequest(new { error = "No active band selected." });

        var band = await db.Bands.FirstOrDefaultAsync(b => b.Id == bandId);
        if (band is null) return BadRequest(new { error = "No active band selected." });

        var act = await db.Acts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == actId && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });

        if (!await bandSiteConnection.HasSiteConfiguredAsync(band))
            return Ok(new { ok = true, published = false, reason = "This band has no website connected - nothing to publish to." });

        byte[] pdf;
        try { pdf = await GeneratePdfBytesAsync(actId); }
        catch (Exception ex) when (ex is InvalidOperationException or PlaywrightException)
        {
            return StatusCode(502, new { error = $"Could not generate the PDF: {ex.Message}" });
        }

        try
        {
            await siteEditor.PublishAsync(band, act, pdf);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = $"Could not push to the site: {ex.Message}" });
        }

        return Ok(new { ok = true, published = true, path = TechRiderSiteEditor.PdfPath(band, act) });
    }
}
