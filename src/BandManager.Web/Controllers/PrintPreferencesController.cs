using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SetPrintPreferenceRequest(string FontFamily, bool Bold, bool Italic, int FontSizePt, string LineSpacing, bool NumberLines);

/// <summary>
/// Self-service saved print formatting - one row per user, global across
/// every band (see PrintPreference.cs). GET always materializes the
/// documented default (Arial, Bold, 14pt, double-spaced, numbered) for
/// anyone who's never saved one, same "never special-case unconfigured"
/// convention as NotificationPreferencesController.
/// </summary>
[ApiController]
[Route("/api/print-preferences")]
[Authorize]
public class PrintPreferencesController(ApplicationDbContext db) : ControllerBase
{
    private static object Serialize(PrintPreference p) => new
    {
        fontFamily = p.FontFamily,
        bold = p.Bold,
        italic = p.Italic,
        fontSizePt = p.FontSizePt,
        lineSpacing = p.LineSpacing.ToString(),
        numberLines = p.NumberLines
    };

    private static readonly PrintPreference Default = new()
    {
        FontFamily = "Arial",
        Bold = true,
        Italic = false,
        FontSizePt = 14,
        LineSpacing = PrintLineSpacing.Double,
        NumberLines = true
    };

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var pref = await db.PrintPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        return Ok(Serialize(pref ?? Default));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] SetPrintPreferenceRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        if (!Enum.TryParse<PrintLineSpacing>(request.LineSpacing, ignoreCase: true, out var spacing))
            return BadRequest(new { error = "Invalid line spacing." });

        var fontFamily = request.FontFamily?.Trim();
        if (string.IsNullOrEmpty(fontFamily)) return BadRequest(new { error = "Font family is required." });
        var fontSizePt = Math.Clamp(request.FontSizePt, 8, 36);

        var pref = await db.PrintPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (pref is null)
        {
            pref = new PrintPreference { UserId = userId.Value, FontFamily = fontFamily };
            db.PrintPreferences.Add(pref);
        }
        pref.FontFamily = fontFamily[..Math.Min(fontFamily.Length, 100)];
        pref.Bold = request.Bold;
        pref.Italic = request.Italic;
        pref.FontSizePt = fontSizePt;
        pref.LineSpacing = spacing;
        pref.NumberLines = request.NumberLines;
        await db.SaveChangesAsync();

        return Ok(Serialize(pref));
    }
}
