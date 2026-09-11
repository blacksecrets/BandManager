using Microsoft.Playwright;

namespace BandManager.Web.Services;

public record PdfCookie(string Name, string Value, string Domain, string Path);

/// <summary>
/// Headless-prints print-tech-rider.html to PDF via Playwright/Chromium -
/// the exact same page a browser prints, so the PDF always matches the
/// in-app preview (see TechRiderController.cs's own doc comment: one
/// document shape, shared by print/PDF/publish). Launches a fresh browser
/// per call rather than keeping one running - this is an occasional
/// admin action (downloading a Tech Rider), not a hot path, so the
/// per-call startup cost isn't worth a long-lived browser instance's
/// extra complexity/memory.
///
/// The internal request needs the caller's own auth: since this
/// navigates to the app's own print-tech-rider.html over loopback
/// (http://localhost:8080), it carries the caller's actual
/// BandManager.Auth/BandManager.Session cookies rather than inventing a
/// separate PDF-export auth bypass - the headless page sees exactly the
/// same authenticated, active-band-scoped view the caller would.
/// </summary>
public class TechRiderPdfService
{
    public async Task<byte[]> GeneratePdfAsync(string printPageUrl, IEnumerable<PdfCookie> cookies)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Running as a non-root container user without the sandbox's
            // usual namespace privileges - the well-known Docker fix.
            Args = ["--no-sandbox"]
        });
        var context = await browser.NewContextAsync();
        await context.AddCookiesAsync(cookies.Select(c => new Cookie
        {
            Name = c.Name,
            Value = c.Value,
            Domain = c.Domain,
            Path = c.Path
        }));

        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(printPageUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        if (response is null || !response.Ok)
            throw new InvalidOperationException($"Could not load the Tech Rider page (status {response?.Status.ToString() ?? "none"}).");

        return await page.PdfAsync(new PagePdfOptions
        {
            Format = "Letter",
            PrintBackground = true,
            Margin = new Margin { Top = "0.4in", Bottom = "0.4in", Left = "0.4in", Right = "0.4in" }
        });
    }
}
