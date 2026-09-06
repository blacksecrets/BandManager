using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record UpdateGigRequest(
    string? Title, string? Venue, string? VenueUrl, string? Address, string? Date, string? Time,
    string? WithArtists, string? WithArtistsUrl, string? TicketMode, string? TicketsUrl, string? CustomTicketsText);

/// <summary>
/// Pushes Calendar Listing edits to the active Band's own site repo via
/// GitHub - ported from the old app's routes/website.js. BandAdmin only
/// (matches every other Setup/site-editing surface). Not yet wired: this
/// doesn't populate ScheduleItemsController's own item.gig field (still
/// hardcoded null there) - that's a separate gap, tracked apart from this.
/// </summary>
[ApiController]
[Route("/api/gigs")]
[Authorize(Policy = "BandAdmin")]
public class GigsController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    GigsSource gigsSource,
    GigsSiteEditor gigsSiteEditor,
    GitHubSiteClient gitHub,
    CatalogStore catalogStore,
    FlyerCache flyerCache,
    Scheduler scheduler) : ControllerBase
{
    private const long MaxFlyerBytes = 20 * 1024 * 1024;

    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    // Title/address stay "only if non-empty" - calendar.js's own comment
    // calls address required alongside date/title/venue (rendered
    // straight into a Google Maps link with no empty-state handling), so
    // a blank submission here would only ever mean nothing was typed yet.
    // Venue/date/ticketsUrl/etc can be legitimately cleared - a blank
    // value is itself a meaningful state, not something to silently drop.
    [HttpPut("{gigRef}")]
    public async Task<IActionResult> Update(string gigRef, [FromBody] Dictionary<string, string> body)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var gig = await gigsSource.FindGigByRefAsync(band, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var fields = new Dictionary<string, string>();
        foreach (var key in new[] { "title", "address" })
        {
            if (body.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                fields[key] = v.Trim()[..Math.Min(v.Trim().Length, 500)];
        }
        foreach (var key in new[] { "venue", "date", "ticketsUrl", "customTicketsText", "venueUrl", "time", "withArtists", "withArtistsUrl" })
        {
            if (body.TryGetValue(key, out var v))
                fields[key] = v.Trim()[..Math.Min(v.Trim().Length, 500)];
        }
        if (body.TryGetValue("ticketMode", out var mode) && mode is "url" or "free" or "custom")
            fields["ticketMode"] = mode;

        if (fields.Count == 0) return BadRequest(new { error = "Nothing to update" });

        try
        {
            await gigsSiteEditor.UpdateGigFieldsAsync(band, gig, fields);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // A title, venue, address, and date are required - a show only comes
    // into calendar.js's gigs array through this form, or by hand
    // directly in the site repo.
    [HttpPost]
    [RequestSizeLimit(MaxFlyerBytes)]
    public async Task<IActionResult> Create()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        if (!Request.HasFormContentType) return BadRequest(new { error = "Expected form data" });
        var form = await Request.ReadFormAsync();

        string F(string key) => form.TryGetValue(key, out var v) ? (v.ToString() ?? "").Trim() : "";
        var title = F("title");
        var venue = F("venue");
        var address = F("address");
        var dateRaw = F("date");
        if (title.Length == 0) return BadRequest(new { error = "Title is required" });
        if (venue.Length == 0) return BadRequest(new { error = "Venue is required" });
        if (address.Length == 0) return BadRequest(new { error = "Address is required" });
        if (!System.Text.RegularExpressions.Regex.IsMatch(dateRaw, @"^\d{4}-\d{2}-\d{2}$") || !DateTime.TryParse(dateRaw, out var parsedDate))
            return BadRequest(new { error = "A valid date is required" });

        // Reformatted to calendar.js's own human-readable convention
        // ("Friday, October 3, 2026"), which is also what the gig-driven
        // scheduling elsewhere in the app expects to be able to re-parse.
        var date = parsedDate.ToString("dddd, MMMM d, yyyy");

        ResolvedMedia? resolved;
        try
        {
            resolved = await catalogStore.ResolveMediaInputAsync(
                band.Id, await form.Files.GetFile("flyer").ToUploadedFilePayloadAsync(), form["catalogItemId"], form["url"],
                MediaType.Image, User.Identity?.Name, MaxFlyerBytes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        string? ext = null;
        if (resolved is not null)
        {
            ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
            if (ext is not ("png" or "jpg" or "webp" or "gif"))
                return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
        }

        var fields = new GigsSiteEditor.NewGigFields(
            title, venue, address, date, dateRaw,
            F("venueUrl") is { Length: > 0 } vu ? vu : null,
            F("time") is { Length: > 0 } ti ? ti : null,
            F("withArtists") is { Length: > 0 } wa ? wa : null,
            F("withArtistsUrl") is { Length: > 0 } wau ? wau : null,
            F("ticketMode") is "url" or "free" or "custom" ? F("ticketMode") : null,
            F("ticketsUrl") is { Length: > 0 } tu ? tu : null,
            F("customTicketsText") is { Length: > 0 } ctt ? ctt : null);

        try
        {
            var (id, flyerMain) = await gigsSiteEditor.AddGigAsync(band, fields, resolved?.Buffer, ext);
            if (flyerMain is not null && resolved is not null)
                await flyerCache.WriteDirectlyAsync(band, flyerMain, resolved.Buffer);

            // Generation normally only runs on a timer - without this, the
            // new gig's tile (and its cross-platform countdown tiles)
            // wouldn't show up until then. A failure here doesn't undo the
            // successful write to calendar.js, so it's swallowed rather
            // than turned into an error response for the add itself.
            try { await scheduler.GenerateAllAsync(band.Id, band); } catch { /* logged nowhere yet - acceptable, matches old app's console-only handling */ }

            return Ok(new { ok = true, id });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Replaces a gig's flyer image. Usually in place at the same file
    // path - but if the gig has never had a flyer, this is the first
    // time it gets one, which needs a new path and adding the flyerMain
    // field (mirrors the media/gallery "external -> local file" upgrade
    // path).
    [HttpPost("{gigRef}/flyer")]
    [RequestSizeLimit(MaxFlyerBytes)]
    public async Task<IActionResult> UploadFlyer(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await gigsSource.FindGigByRefAsync(band, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });
        if (!Request.HasFormContentType) return BadRequest(new { error = "Expected form data" });
        var form = await Request.ReadFormAsync();

        ResolvedMedia? resolved;
        try
        {
            resolved = await catalogStore.ResolveMediaInputAsync(
                band.Id, await form.Files.GetFile("file").ToUploadedFilePayloadAsync(), form["catalogItemId"], form["url"],
                MediaType.Image, User.Identity?.Name, MaxFlyerBytes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        if (resolved is null) return BadRequest(new { error = "Provide a file, a Catalog pick, or a URL" });

        try
        {
            if (string.IsNullOrEmpty(gig.FlyerMain))
            {
                var ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
                if (ext is not ("png" or "jpg" or "webp" or "gif"))
                    return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
                var newPath = $"flyers/{gig.Id}.{ext}";
                await gitHub.PutBinaryFileAsync(band, newPath, resolved.Buffer, $"Add flyer for {gig.Title}");
                await gigsSiteEditor.UpdateGigFieldsAsync(band, gig, new Dictionary<string, string> { ["flyerMain"] = newPath });
                await flyerCache.WriteDirectlyAsync(band, newPath, resolved.Buffer);
                return Ok(new { ok = true, path = newPath });
            }

            var sha = await gitHub.GetFileShaAsync(band, gig.FlyerMain);
            await gitHub.PutBinaryFileAsync(band, gig.FlyerMain, resolved.Buffer, $"Update flyer for {gig.Title}", sha);
            await flyerCache.WriteDirectlyAsync(band, gig.FlyerMain, resolved.Buffer);
            return Ok(new { ok = true, path = gig.FlyerMain });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // Deletes the gig from the live site entirely, and every schedule
    // item tied to it across every platform - none of those make sense
    // anymore once the show itself is gone.
    [HttpDelete("{gigRef}")]
    public async Task<IActionResult> Delete(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await gigsSource.FindGigByRefAsync(band, gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        try
        {
            await gigsSiteEditor.DeleteGigAsync(band, gig);
            await db.ScheduleItems.Where(s => s.BandId == band.Id && s.GigRef == gigRef).ExecuteDeleteAsync();
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
