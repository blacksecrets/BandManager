using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

// One with-act as sent from the client: either an existing with-band's id
// (picked from the dropdown of this band's previously-used with-bands), or
// a Name with no id, meaning "create a new stub Band for this" - see
// Band.IsOnboarded. Never both; Id wins if somehow both are present.
public record GigWithActInput(Guid? WithBandId, string? Name, string? Url);
public record SetSelectedFlyerRequest(Guid FlyerId);

/// <summary>
/// Gigs are a real DB table now (see Entities.Gig) - the database is the
/// source of truth, not the Band's site. If the Band has a site configured
/// (SiteBaseUrl/GitHubOwner/GitHubRepo/a website credential all present),
/// every write here also best-effort publishes the gig's current full
/// state to that site's calendar.js via GigsSiteEditor - a push failure
/// doesn't undo the DB save (mirrors FlyersController.Create's established
/// "already saved even if pushing live failed" pattern). A Band with no
/// site simply never attempts that step - Gig Management, With-acts, and
/// everything else here fully works either way.
///
/// Known interim limitation, not fixed in this pass: FlyerMain (the
/// simple "attach one flyer image" field on a gig) still requires a site,
/// same as before this rewrite - it's a site-relative GitHub path with no
/// independent local storage of its own yet. This doesn't regress
/// anything (today's code already throws without a site for this), and a
/// siteless band can still fully use the separate Flyer system
/// (CatalogStore-backed, already site-independent) instead.
/// </summary>
[ApiController]
[Route("/api/gigs")]
[Authorize(Policy = "BandAdmin")]
public class GigsController(
    ApplicationDbContext db,
    IActiveBandAccessor activeBand,
    GigsSiteEditor gigsSiteEditor,
    GitHubSiteClient gitHub,
    CatalogStore catalogStore,
    CredentialStore credentialStore,
    FlyerCache flyerCache,
    Scheduler scheduler,
    GoogleCalendarPushService googleCalendarPush,
    OutlookCalendarPushService outlookCalendarPush,
    ILogger<GigsController> logger) : ControllerBase
{
    private const long MaxFlyerBytes = 20 * 1024 * 1024;

    private static string FormatDate(DateOnly d) => GigDateTimeFormatting.FormatDate(d);
    private static string? FormatTime(TimeOnly? t) => GigDateTimeFormatting.FormatTime(t);
    private static TimeOnly? ParseTimeOrNull(string? raw) => GigDateTimeFormatting.ParseTimeOrNull(raw);

    // Best-effort, same spirit as the site push above (TryPublishAsync) -
    // but never surfaced as a 502, since a Google/Outlook push only ever
    // affects members who've individually connected their own calendar
    // (see UserExternalCalendarConnection), not the shared site every
    // visitor sees. Every call already no-ops instantly for a band with
    // zero connected members, which is every band until Phase 8's OAuth
    // apps are configured - see ExternalCalendarController's doc comment.
    private async Task PushGigToExternalCalendarsAsync(Gig gig)
    {
        try { await googleCalendarPush.PushGigAsync(gig); }
        catch (Exception ex) { logger.LogWarning(ex, "Google Calendar push failed for gig {GigId}", gig.Id); }
        try { await outlookCalendarPush.PushGigAsync(gig); }
        catch (Exception ex) { logger.LogWarning(ex, "Outlook Calendar push failed for gig {GigId}", gig.Id); }
    }

    private async Task DeleteGigFromExternalCalendarsAsync(Guid bandId, Guid gigId)
    {
        try { await googleCalendarPush.DeleteGigAsync(bandId, gigId); }
        catch (Exception ex) { logger.LogWarning(ex, "Google Calendar delete failed for gig {GigId}", gigId); }
        try { await outlookCalendarPush.DeleteGigAsync(bandId, gigId); }
        catch (Exception ex) { logger.LogWarning(ex, "Outlook Calendar delete failed for gig {GigId}", gigId); }
    }

    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    // Resolves the gig's Act: the picked one if it's a real Act on this
    // band, otherwise the band's IsDefault Act - every gig always ends up
    // with one, matching Gig.ActId's doc comment.
    private async Task<Guid?> ResolveActIdAsync(Guid bandId, Guid? requested)
    {
        if (requested is { } id && await db.Acts.AnyAsync(a => a.Id == id && a.BandId == bandId))
            return id;
        var defaultAct = await db.Acts.FirstOrDefaultAsync(a => a.BandId == bandId && a.IsDefault);
        return defaultAct?.Id;
    }

    // Falls back to the picked Venue's own default promoter when no
    // explicit pick was made - never forces a value like ResolveActIdAsync
    // does, since "no promoter yet" is a legitimate state for a Gig,
    // unlike Act.
    private async Task<Guid?> ResolvePromoterIdAsync(Guid bandId, Guid? requested, Guid? venueId)
    {
        if (requested is { } id && await db.Promoters.AnyAsync(p => p.Id == id && p.BandId == bandId))
            return id;
        if (venueId is { } vId)
            return (await db.Venues.FirstOrDefaultAsync(v => v.Id == vId && v.BandId == bandId))?.DefaultPromoterId;
        return null;
    }

    private async Task<bool> HasSiteConfiguredAsync(Band band) =>
        !string.IsNullOrWhiteSpace(band.SiteBaseUrl) && !string.IsNullOrWhiteSpace(band.GitHubOwner) && !string.IsNullOrWhiteSpace(band.GitHubRepo)
        && await credentialStore.GetCredentialAsync(band.Id, "website") is not null;

    // Resolves each input to a real Band row - either the picked existing
    // with-band, or a freshly-created stub (Name given, no Id - "there may
    // be more than one with the same name" means typing a name always
    // makes a new row here; matching an existing one by name is what the
    // dropdown of this band's own past with-bands is for, client-side).
    private async Task<List<(Band WithBand, string? Url)>> ResolveWithActsAsync(List<GigWithActInput> inputs)
    {
        var result = new List<(Band, string?)>();
        foreach (var input in inputs)
        {
            if (input.WithBandId is { } id)
            {
                var existing = await db.Bands.FindAsync(id);
                if (existing is not null) result.Add((existing, input.Url));
                continue;
            }
            var name = input.Name?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var stub = new Band { Name = name[..Math.Min(name.Length, 200)], Slug = Guid.NewGuid().ToString("N"), IsOnboarded = false };
            db.Bands.Add(stub);
            result.Add((stub, input.Url));
        }
        return result;
    }

    private static async Task<List<WithAct>> ToSiteWithActsAsync(ApplicationDbContext db, Guid gigId) =>
        await db.GigWithBands.Where(w => w.GigId == gigId).Include(w => w.WithBand)
            .OrderBy(w => w.SortOrder)
            .Select(w => new WithAct(w.WithBand.Name, w.Url))
            .ToListAsync();

    /// <summary>Best-effort site publish - swallows nothing silently that
    /// matters: returns an error message string on failure (caller decides
    /// what to do with it), or null on success/skip.</summary>
    private async Task<string?> TryPublishAsync(Band band, Gig gig)
    {
        if (!await HasSiteConfiguredAsync(band)) return null;
        try
        {
            var withActs = await ToSiteWithActsAsync(db, gig.Id);
            await gigsSiteEditor.PublishGigAsync(band, gig, withActs);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private async Task<object> SerializeGigAsync(Gig gig)
    {
        var withActs = await db.GigWithBands.Where(w => w.GigId == gig.Id).Include(w => w.WithBand)
            .OrderBy(w => w.SortOrder)
            .Select(w => new { withBandId = w.WithBandId, name = w.WithBand.Name, url = w.Url })
            .ToListAsync();
        return new
        {
            id = gig.Ref,
            title = gig.Title,
            actId = gig.ActId,
            promoterId = gig.PromoterId,
            venue = gig.Venue,
            venueUrl = gig.VenueUrl,
            date = FormatDate(gig.Date),
            // ISO alongside the display string above - lets a client-side
            // <input type="date"> prefill correctly without parsing the
            // display text back apart (same reasoning GigSetsController's
            // sortDate already established for its own gig list).
            dateIso = gig.Date.ToString("yyyy-MM-dd"),
            time = gig.Time,
            doorsTime = FormatTime(gig.DoorsTime),
            openerTime = FormatTime(gig.OpenerTime),
            headlinerTime = FormatTime(gig.HeadlinerTime),
            address = gig.Address,
            with = withActs,
            ticketsUrl = gig.TicketsUrl,
            flyerMain = gig.FlyerMain,
            freeAdmission = gig.FreeAdmission,
            customTicketsText = gig.CustomTicketsText,
            ticketMode = gig.TicketMode
        };
    }

    // Read access is broader than the class-level BandAdmin policy - the
    // Flyer Editor (any BandMember) needs the full gig record.
    [HttpGet("{gigRef}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });
        return Ok(await SerializeGigAsync(gig));
    }

    // Narrower than the full Update below - date/time only, and open to
    // any BandMember rather than BandAdmin-only, specifically so dragging
    // a Calendar entry to reschedule (calendar.js) doesn't require opening
    // up the rest of Update's fields (title, address, tickets, with-acts,
    // ...) to non-admins, which nothing asked for.
    public record RescheduleGigRequest(string Date, string? Time);

    [HttpPut("{gigRef}/reschedule")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Reschedule(string gigRef, [FromBody] RescheduleGigRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        if (!DateTime.TryParse(request.Date, out var parsedDate))
            return BadRequest(new { error = "A valid date is required" });
        gig.Date = DateOnly.FromDateTime(parsedDate);
        if (request.Time is not null) gig.Time = request.Time.Trim().Length > 0 ? request.Time.Trim() : null;

        gig.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await PushGigToExternalCalendarsAsync(gig);

        var pushError = await TryPublishAsync(band, gig);
        if (pushError is not null) return StatusCode(502, new { error = $"Rescheduled, but could not push to the site: {pushError}" });
        return Ok(new { ok = true, date = GigDateTimeFormatting.FormatDate(gig.Date) });
    }

    [HttpPut("{gigRef}")]
    public async Task<IActionResult> Update(string gigRef, [FromBody] JsonElement body)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        string? S(string key) => body.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        // title/address: only applied if non-empty - a blank submission
        // here only ever means nothing was typed yet, never "clear it."
        if (S("title") is { Length: > 0 } title) gig.Title = title.Trim()[..Math.Min(title.Trim().Length, 500)];
        if (S("address") is { Length: > 0 } address) gig.Address = address.Trim()[..Math.Min(address.Trim().Length, 500)];

        // Everything else can be legitimately cleared - a blank value is
        // itself a meaningful state, not something to silently drop.
        void SetIfPresent(string key, Action<string> apply)
        {
            if (body.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                apply((v.GetString() ?? "").Trim()[..Math.Min((v.GetString() ?? "").Trim().Length, 500)]);
        }
        SetIfPresent("venue", v => gig.Venue = v);
        if (S("date") is { Length: > 0 } dateRaw)
        {
            if (!DateTime.TryParse(dateRaw, out var parsedDate))
                return BadRequest(new { error = "A valid date is required" });
            gig.Date = DateOnly.FromDateTime(parsedDate);
        }
        SetIfPresent("ticketsUrl", v => gig.TicketsUrl = v);
        SetIfPresent("customTicketsText", v => gig.CustomTicketsText = v);
        SetIfPresent("venueUrl", v => gig.VenueUrl = v);
        SetIfPresent("time", v => gig.Time = v);
        if (body.TryGetProperty("doorsTime", out var dtEl) && dtEl.ValueKind == JsonValueKind.String) gig.DoorsTime = ParseTimeOrNull(dtEl.GetString());
        if (body.TryGetProperty("openerTime", out var otEl) && otEl.ValueKind == JsonValueKind.String) gig.OpenerTime = ParseTimeOrNull(otEl.GetString());
        if (body.TryGetProperty("headlinerTime", out var htEl) && htEl.ValueKind == JsonValueKind.String) gig.HeadlinerTime = ParseTimeOrNull(htEl.GetString());
        var ticketMode = S("ticketMode");
        if (ticketMode is "url" or "free" or "custom") gig.TicketMode = ticketMode;

        if (body.TryGetProperty("actId", out var actIdEl) && actIdEl.ValueKind == JsonValueKind.String
            && Guid.TryParse(actIdEl.GetString(), out var requestedActId))
            gig.ActId = await ResolveActIdAsync(band.Id, requestedActId);

        if (body.TryGetProperty("promoterId", out var promoterIdEl))
        {
            if (promoterIdEl.ValueKind == JsonValueKind.String && Guid.TryParse(promoterIdEl.GetString(), out var requestedPromoterId)
                && await db.Promoters.AnyAsync(p => p.Id == requestedPromoterId && p.BandId == band.Id))
                gig.PromoterId = requestedPromoterId;
            else if (promoterIdEl.ValueKind is JsonValueKind.Null or JsonValueKind.String)
                gig.PromoterId = null; // an empty string or explicit null both mean "cleared"
        }

        if (body.TryGetProperty("with", out var withEl) && withEl.ValueKind == JsonValueKind.String)
        {
            List<GigWithActInput>? inputs;
            try { inputs = JsonSerializer.Deserialize<List<GigWithActInput>>(withEl.GetString()!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { return BadRequest(new { error = "Invalid with-acts payload." }); }

            db.GigWithBands.RemoveRange(db.GigWithBands.Where(w => w.GigId == gig.Id));
            if (inputs is { Count: > 0 })
            {
                var resolved = await ResolveWithActsAsync(inputs);
                var order = 0;
                foreach (var (withBand, url) in resolved)
                    db.GigWithBands.Add(new GigWithBand { GigId = gig.Id, WithBandId = withBand.Id, WithBand = withBand, Url = url, SortOrder = order++ });
            }
        }

        gig.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await PushGigToExternalCalendarsAsync(gig);

        var pushError = await TryPublishAsync(band, gig);
        if (pushError is not null) return StatusCode(502, new { error = $"Saved, but could not push to the site: {pushError}" });
        return Ok(new { ok = true });
    }

    // A title, venue, address, and date are required.
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
        var date = DateOnly.FromDateTime(parsedDate);

        List<GigWithActInput> withInputs = [];
        if (F("with") is { Length: > 0 } withJson)
        {
            try { withInputs = JsonSerializer.Deserialize<List<GigWithActInput>>(withJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []; }
            catch (JsonException) { return BadRequest(new { error = "Invalid with-acts payload." }); }
        }

        // Optional traceability back to the VenueCampaign this gig came
        // from (see Venue.cs/VenueCampaign.cs) - Venue/Address above stay
        // independently editable either way, this is just a pointer.
        Guid? venueId = null;
        if (Guid.TryParse(F("venueId"), out var parsedVenueId) && await db.Venues.AnyAsync(v => v.Id == parsedVenueId && v.BandId == band.Id))
            venueId = parsedVenueId;

        Guid? requestedActId = Guid.TryParse(F("actId"), out var parsedActId) ? parsedActId : null;
        var actId = await ResolveActIdAsync(band.Id, requestedActId);

        Guid? requestedPromoterId = Guid.TryParse(F("promoterId"), out var parsedPromoterId) ? parsedPromoterId : null;
        var promoterId = await ResolvePromoterIdAsync(band.Id, requestedPromoterId, venueId);

        var gig = new Gig
        {
            BandId = band.Id,
            Ref = Guid.NewGuid().ToString(),
            Title = title,
            ActId = actId,
            PromoterId = promoterId,
            Venue = venue,
            VenueId = venueId,
            Address = address,
            Date = date,
            VenueUrl = F("venueUrl") is { Length: > 0 } vu ? vu : null,
            Time = F("time") is { Length: > 0 } ti ? ti : null,
            DoorsTime = ParseTimeOrNull(F("doorsTime")),
            OpenerTime = ParseTimeOrNull(F("openerTime")),
            HeadlinerTime = ParseTimeOrNull(F("headlinerTime")),
            TicketMode = F("ticketMode") is "url" or "free" or "custom" ? F("ticketMode") : null,
            TicketsUrl = F("ticketsUrl") is { Length: > 0 } tu ? tu : null,
            CustomTicketsText = F("customTicketsText") is { Length: > 0 } ctt ? ctt : null,
        };
        db.Gigs.Add(gig);

        var resolvedWith = await ResolveWithActsAsync(withInputs);
        var order = 0;
        foreach (var (withBand, url) in resolvedWith)
            db.GigWithBands.Add(new GigWithBand { GigId = gig.Id, Gig = gig, WithBandId = withBand.Id, WithBand = withBand, Url = url, SortOrder = order++ });

        // Flyer at creation time still requires a site - see class doc
        // comment's "known interim limitation."
        if ((form.Files.GetFile("flyer") is not null || !string.IsNullOrEmpty(form["catalogItemId"]) || !string.IsNullOrEmpty(form["url"])) && !await HasSiteConfiguredAsync(band))
            return BadRequest(new { error = "Attaching a flyer at creation needs this band's website connected first (Configure Web Presence). You can add one later once a flyer template is set up, or create the gig without one now." });

        ResolvedMedia? resolved = null;
        if (await HasSiteConfiguredAsync(band))
        {
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
            if (resolved is not null)
            {
                var ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
                if (ext is not ("png" or "jpg" or "webp" or "gif"))
                    return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
                gig.FlyerMain = $"flyers/{gig.Ref}.{ext}";
            }
        }

        await db.SaveChangesAsync();

        if (resolved is not null)
        {
            try { await gitHub.PutBinaryFileAsync(band, gig.FlyerMain!, resolved.Buffer, $"Add flyer for \"{title}\""); }
            catch (InvalidOperationException ex) { return StatusCode(502, new { error = ex.Message }); }
            await flyerCache.WriteDirectlyAsync(band, gig.FlyerMain!, resolved.Buffer);
        }

        var pushError = await TryPublishAsync(band, gig);

        // Generation normally only runs on a timer - without this, the new
        // gig's tile (and its cross-platform countdown tiles) wouldn't
        // show up until then. A failure here doesn't undo the successful
        // gig creation, so it's swallowed.
        try { await scheduler.GenerateAllAsync(band.Id, band); } catch { /* logged nowhere yet - acceptable, matches old app's console-only handling */ }
        await PushGigToExternalCalendarsAsync(gig);

        if (pushError is not null) return StatusCode(502, new { error = $"Gig created, but could not push to the site: {pushError}", id = gig.Ref, gigId = gig.Id });
        return Ok(new { ok = true, id = gig.Ref, gigId = gig.Id });
    }

    // Replaces a gig's flyer image - still requires a site, see class doc
    // comment's "known interim limitation."
    [HttpPost("{gigRef}/flyer")]
    [RequestSizeLimit(MaxFlyerBytes)]
    public async Task<IActionResult> UploadFlyer(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });
        if (!await HasSiteConfiguredAsync(band))
            return BadRequest(new { error = "This band's website isn't connected yet - add it under Configure Web Presence first." });
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
            string path;
            if (string.IsNullOrEmpty(gig.FlyerMain))
            {
                var ext = CatalogStore.ExtForMimeType(resolved.MimeType, null);
                if (ext is not ("png" or "jpg" or "webp" or "gif"))
                    return BadRequest(new { error = "Unsupported image type - use PNG, JPEG, WebP, or GIF" });
                path = $"flyers/{gig.Ref}.{ext}";
                await gitHub.PutBinaryFileAsync(band, path, resolved.Buffer, $"Add flyer for {gig.Title}");
                gig.FlyerMain = path;
            }
            else
            {
                path = gig.FlyerMain;
                var sha = await gitHub.GetFileShaAsync(band, path);
                await gitHub.PutBinaryFileAsync(band, path, resolved.Buffer, $"Update flyer for {gig.Title}", sha);
            }
            gig.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await flyerCache.WriteDirectlyAsync(band, path, resolved.Buffer);

            var pushError = await TryPublishAsync(band, gig);
            if (pushError is not null) return StatusCode(502, new { error = $"Flyer saved, but could not push to the site: {pushError}", path });
            return Ok(new { ok = true, path });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // What archiving this gig would take down with it - for the confirm
    // modal's bulleted warning. No IsArchived filtering needed here: a gig
    // that isn't archived yet by definition has nothing archived under it.
    [HttpGet("{gigRef}/archive-preview")]
    public async Task<IActionResult> ArchivePreview(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var scheduleItemCount = await db.ScheduleItems.CountAsync(s => s.BandId == band.Id && s.GigRef == gigRef);
        var flyerCount = await db.Flyers.CountAsync(f => f.BandId == band.Id && f.GigRef == gigRef);
        var songCount = await db.GigSetSongs.CountAsync(s => s.GigSet.BandId == band.Id && s.GigSet.GigRef == gigRef);
        var gigPrepCount = await db.GigPrepChecklistItems.CountAsync(i => i.GigId == gig.Id);

        return Ok(new
        {
            scheduleItems = scheduleItemCount,
            flyers = flyerCount,
            setlistSongs = songCount,
            gigPrepItems = gigPrepCount
        });
    }

    // Idempotent, mirrors SuperAdminController.ArchiveBand exactly -
    // cascades the same archive to every currently-non-archived
    // ScheduleItem/Flyer sharing this GigRef. Never touches a connected
    // site's live listing (see the tranche plan's explicit non-goal) -
    // purely an in-app visibility toggle.
    [HttpPost("{gigRef}/archive")]
    public async Task<IActionResult> Archive(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        if (!gig.IsArchived)
        {
            var now = DateTime.UtcNow;
            gig.IsArchived = true;
            gig.ArchivedAt = now;
            await db.ScheduleItems.Where(s => s.BandId == band.Id && s.GigRef == gigRef && !s.IsArchived)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsArchived, true).SetProperty(x => x.ArchivedAt, now));
            await db.Flyers.Where(f => f.BandId == band.Id && f.GigRef == gigRef && !f.IsArchived)
                .ExecuteUpdateAsync(f => f.SetProperty(x => x.IsArchived, true).SetProperty(x => x.ArchivedAt, now));
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    // The precise reverse of Archive - clears Gig.IsArchived, and clears
    // it only on ScheduleItem/Flyer rows for this GigRef that are
    // currently archived (safe since nothing else can independently
    // archive either today).
    [HttpPost("{gigRef}/unarchive")]
    public async Task<IActionResult> Unarchive(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        if (gig.IsArchived)
        {
            gig.IsArchived = false;
            gig.ArchivedAt = null;
            await db.ScheduleItems.Where(s => s.BandId == band.Id && s.GigRef == gigRef && s.IsArchived)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsArchived, false).SetProperty(x => x.ArchivedAt, (DateTime?)null));
            await db.Flyers.Where(f => f.BandId == band.Id && f.GigRef == gigRef && f.IsArchived)
                .ExecuteUpdateAsync(f => f.SetProperty(x => x.IsArchived, false).SetProperty(x => x.ArchivedAt, (DateTime?)null));
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    // Every Flyer row generated for this gig - for the "Select flyer"
    // modal (Gig Management) and the Web Presence tile flyer picker
    // (Dashboard). Read access is broader than the class-level BandAdmin
    // policy, same reasoning as Get above.
    [HttpGet("{gigRef}/flyers")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> ListFlyers(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.AsNoTracking().FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var flyers = await db.Flyers.AsNoTracking()
            .Include(f => f.GeneratedCatalogItem)
            .Where(f => f.BandId == band.Id && f.GigRef == gigRef && !f.IsArchived)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        return Ok(flyers.Select(f => new
        {
            id = f.Id,
            createdAt = f.CreatedAt,
            filePath = f.GeneratedCatalogItem.FilePath,
            catalogItemId = f.GeneratedCatalogItemId,
            isSelected = f.Id == gig.SelectedFlyerId
        }));
    }

    // Picks which of this gig's (possibly several) Flyers is "the" one -
    // resolved by GigSetsController.GetGigItems wherever a gig's flyer is
    // shown (Dashboard tiles, Web Presence). Best-effort re-pushes that
    // flyer's already-rendered bytes to FlyerMain's path too, via the same
    // shared helper FlyersController.Create uses, so a connected site's
    // calendar listing picks up the change as well - a push failure still
    // leaves the in-app selection saved (mirrors this controller's other
    // "already saved even if pushing live failed" endpoints).
    [HttpPost("{gigRef}/selected-flyer")]
    public async Task<IActionResult> SetSelectedFlyer(string gigRef, [FromBody] SetSelectedFlyerRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        var flyer = await db.Flyers.FirstOrDefaultAsync(f => f.Id == request.FlyerId && f.BandId == band.Id && f.GigRef == gigRef && !f.IsArchived);
        if (flyer is null) return NotFound(new { error = "Flyer not found for this gig" });

        gig.SelectedFlyerId = flyer.Id;

        if (await HasSiteConfiguredAsync(band))
        {
            try
            {
                var rendered = await catalogStore.GetCatalogItemBufferAsync(band.Id, flyer.GeneratedCatalogItemId);
                await gigsSiteEditor.PushFlyerImageAsync(band, gig, rendered, $"Update flyer for {gig.Title}");
                await db.SaveChangesAsync();
                var pushError = await TryPublishAsync(band, gig);
                if (pushError is not null) return StatusCode(502, new { error = $"Flyer selected, but could not push to the site: {pushError}" });
                return Ok(new { ok = true });
            }
            catch (InvalidOperationException ex)
            {
                await db.SaveChangesAsync();
                return StatusCode(502, new { error = $"Flyer selected, but could not push it live: {ex.Message}" });
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Deletes the gig and every schedule item tied to it across every
    // platform - none of those make sense anymore once the show is gone.
    [HttpDelete("{gigRef}")]
    public async Task<IActionResult> Delete(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return NotFound(new { error = "Gig not found" });

        string? pushError = null;
        if (await HasSiteConfiguredAsync(band))
        {
            try { await gigsSiteEditor.UnpublishGigAsync(band, gig.Ref, gig.Title); }
            catch (InvalidOperationException ex) { pushError = ex.Message; }
        }

        var gigId = gig.Id;
        db.Gigs.Remove(gig); // cascades GigWithBands
        await db.ScheduleItems.Where(s => s.BandId == band.Id && s.GigRef == gigRef).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        await DeleteGigFromExternalCalendarsAsync(band.Id, gigId);

        if (pushError is not null) return StatusCode(502, new { error = $"Gig deleted, but could not remove it from the site: {pushError}" });
        return Ok(new { ok = true });
    }
}
