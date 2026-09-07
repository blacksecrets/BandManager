using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record LogCommunicationRequest(string Type, string? To, string? Cc, string? Bcc, string? From, string? Subject, string Body, bool SendEmail, string? OutcomeNotes);
public record SetCampaignStatusRequest(string Status, string? RejectionReason, DateOnly? RetryDate, bool NeverRetry, Guid? BookedGigId);

/// <summary>
/// A Venue's outreach campaign - "mini-ConstantContact" for booking, per
/// the user's own framing. Modeled on Gig Management's shape (a grid
/// instead of tiles, a detail view on click, best-effort external send)
/// but its own controller since venues/campaigns aren't gig-driven.
/// Read/write open to any BandMember, matching VenuesController.
/// </summary>
[ApiController]
[Route("/api/venue-campaigns")]
[Authorize(Policy = "BandMember")]
public class VenueCampaignsController(ApplicationDbContext db, IActiveBandAccessor activeBand, IEmailSender emailSender) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static (bool DueNow, DateOnly? DueDate, VenueCadenceStep? Step) ComputeDue(VenueCampaign? campaign, List<VenueCadenceStep> steps)
    {
        if (campaign is null || campaign.Status != VenueCampaignStatus.Active) return (false, null, null);
        var step = steps.FirstOrDefault(s => s.StepNumber == campaign.CurrentStepNumber && s.Active);
        if (step is null) return (false, null, null); // cadence exhausted - no more configured steps
        var baseDate = campaign.LastCommunicationAt ?? campaign.StartedAt;
        var dueDate = DateOnly.FromDateTime(baseDate.AddDays(step.DaysAfterPrevious));
        return (dueDate <= DateOnly.FromDateTime(DateTime.UtcNow), dueDate, step);
    }

    // Cheap placeholder substitution so a saved template can say "Hi
    // {ContactName}, this is {BandName}..." - deliberately not a full
    // templating engine, just the handful of fields any outreach message
    // actually needs.
    private static string Fill(string text, Venue venue, VenueContact? contact, Band band) => text
        .Replace("{VenueName}", venue.Name)
        .Replace("{ContactName}", contact?.Name ?? "there")
        .Replace("{BandName}", band.Name);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;

        var venues = await db.Venues.AsNoTracking().Include(v => v.Contacts).Include(v => v.Campaign)
            .Where(v => v.BandId == bandId).OrderBy(v => v.Name).ToListAsync();
        var steps = await db.VenueCadenceSteps.AsNoTracking().Where(s => s.BandId == bandId).ToListAsync();

        var result = venues.Select(v =>
        {
            var (dueNow, dueDate, step) = ComputeDue(v.Campaign, steps);
            var primaryContact = v.Contacts.FirstOrDefault(c => c.IsPrimary) ?? v.Contacts.FirstOrDefault();
            return new
            {
                venueId = v.Id,
                venueName = v.Name,
                campaignId = v.Campaign?.Id,
                status = v.Campaign?.Status.ToString() ?? "NotStarted",
                currentStepNumber = v.Campaign?.CurrentStepNumber,
                currentStepType = step?.Type.ToString(),
                dueNow,
                dueDate,
                lastCommunicationAt = v.Campaign?.LastCommunicationAt,
                primaryContactName = primaryContact?.Name,
                primaryContactEmail = primaryContact?.Email,
                primaryContactPhone = primaryContact?.Phone,
                bookedGigId = v.Campaign?.BookedGigId
            };
        });
        return Ok(result);
    }

    [HttpPost("{venueId:guid}/start")]
    public async Task<IActionResult> Start(Guid venueId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venue = await db.Venues.Include(v => v.Campaign).FirstOrDefaultAsync(v => v.Id == venueId && v.BandId == bandId);
        if (venue is null) return NotFound(new { error = "Not found" });

        if (venue.Campaign is not null && venue.Campaign.Status == VenueCampaignStatus.Active)
            return BadRequest(new { error = "This venue already has an active campaign." });

        if (venue.Campaign is null)
        {
            venue.Campaign = new VenueCampaign { BandId = bandId, VenueId = venueId };
            db.VenueCampaigns.Add(venue.Campaign);
        }
        else
        {
            // Restarting a Paused/Rejected/Booked campaign - deliberate
            // user action, overrides NeverRetry (that flag is advisory,
            // not a hard lock, since the user clicking "Start" again is
            // itself the override).
            venue.Campaign.Status = VenueCampaignStatus.Active;
            venue.Campaign.CurrentStepNumber = 1;
            venue.Campaign.StartedAt = DateTime.UtcNow;
            venue.Campaign.LastCommunicationAt = null;
            venue.Campaign.RejectionReason = null;
            venue.Campaign.RetryDate = null;
            venue.Campaign.NeverRetry = false;
            venue.Campaign.BookedGigId = null;
        }
        await db.SaveChangesAsync();
        return Ok(new { campaignId = venue.Campaign.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var band = await db.Bands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bandId);
        var campaign = await db.VenueCampaigns.AsNoTracking()
            .Include(c => c.Venue).ThenInclude(v => v.Contacts)
            .Include(c => c.Communications).ThenInclude(m => m.LoggedByUser)
            .FirstOrDefaultAsync(c => c.Id == id && c.BandId == bandId);
        if (campaign is null || band is null) return NotFound(new { error = "Not found" });

        var steps = await db.VenueCadenceSteps.AsNoTracking().Where(s => s.BandId == bandId).OrderBy(s => s.StepNumber).ToListAsync();
        var (dueNow, dueDate, dueStep) = ComputeDue(campaign, steps);
        var primaryContact = campaign.Venue.Contacts.FirstOrDefault(c => c.IsPrimary) ?? campaign.Venue.Contacts.FirstOrDefault();

        return Ok(new
        {
            id = campaign.Id,
            status = campaign.Status.ToString(),
            currentStepNumber = campaign.CurrentStepNumber,
            startedAt = campaign.StartedAt,
            lastCommunicationAt = campaign.LastCommunicationAt,
            rejectionReason = campaign.RejectionReason,
            retryDate = campaign.RetryDate,
            neverRetry = campaign.NeverRetry,
            bookedGigId = campaign.BookedGigId,
            venue = new
            {
                id = campaign.Venue.Id,
                name = campaign.Venue.Name,
                addressLine1 = campaign.Venue.AddressLine1,
                city = campaign.Venue.City,
                state = campaign.Venue.State,
                postalCode = campaign.Venue.PostalCode,
                phone = campaign.Venue.Phone,
                website = campaign.Venue.Website,
                notes = campaign.Venue.Notes,
                contacts = campaign.Venue.Contacts.Select(c => new { id = c.Id, name = c.Name, title = c.Title, email = c.Email, phone = c.Phone, isPrimary = c.IsPrimary })
            },
            dueNow,
            dueDate,
            nextStep = dueStep is null ? null : new
            {
                stepNumber = dueStep.StepNumber,
                type = dueStep.Type.ToString(),
                subject = dueStep.DefaultSubject is null ? null : Fill(dueStep.DefaultSubject, campaign.Venue, primaryContact, band),
                body = Fill(dueStep.DefaultBody, campaign.Venue, primaryContact, band)
            },
            communications = campaign.Communications.OrderByDescending(m => m.OccurredAt).Select(m => new
            {
                id = m.Id,
                type = m.Type.ToString(),
                stepNumber = m.StepNumber,
                occurredAt = m.OccurredAt,
                to = m.To,
                cc = m.Cc,
                bcc = m.Bcc,
                from = m.From,
                subject = m.Subject,
                body = m.Body,
                outcomeNotes = m.OutcomeNotes,
                loggedByName = m.LoggedByUser.DisplayName
            })
        });
    }

    [HttpPost("{id:guid}/communications")]
    public async Task<IActionResult> LogCommunication(Guid id, [FromBody] LogCommunicationRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var campaign = await db.VenueCampaigns.Include(c => c.Venue).FirstOrDefaultAsync(c => c.Id == id && c.BandId == bandId);
        if (campaign is null) return NotFound(new { error = "Not found" });
        if (!Enum.TryParse<CommunicationType>(request.Type, ignoreCase: true, out var type))
            return BadRequest(new { error = "Type must be \"Email\" or \"Phone\"." });
        var body = request.Body?.Trim();
        if (string.IsNullOrEmpty(body)) return BadRequest(new { error = "Body/script is required." });

        var comm = new VenueCommunication
        {
            VenueCampaignId = campaign.Id,
            Type = type,
            StepNumber = campaign.CurrentStepNumber,
            To = Clean(request.To),
            Cc = Clean(request.Cc),
            Bcc = Clean(request.Bcc),
            From = Clean(request.From),
            Subject = Clean(request.Subject),
            Body = body,
            OutcomeNotes = Clean(request.OutcomeNotes),
            LoggedByUserId = userId.Value
        };
        db.VenueCommunications.Add(comm);

        campaign.LastCommunicationAt = comm.OccurredAt;
        campaign.CurrentStepNumber += 1;
        await db.SaveChangesAsync();

        // Best-effort, real send goes through the same IEmailSender every
        // other transactional email in this app uses - logs instead of
        // truly delivering until a real provider is configured
        // (LoggingEmailSender), same known/disclosed limitation as
        // password-reset/invite emails.
        if (type == CommunicationType.Email && request.SendEmail && !string.IsNullOrEmpty(comm.To))
        {
            try { await emailSender.SendAsync(comm.To, comm.Subject ?? $"Message from {campaign.Venue.Name}'s booking contact", comm.Body); }
            catch { /* best-effort - the communication is already logged either way */ }
        }

        return Ok(new { ok = true, communicationId = comm.Id, newStepNumber = campaign.CurrentStepNumber });
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetCampaignStatusRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var campaign = await db.VenueCampaigns.FirstOrDefaultAsync(c => c.Id == id && c.BandId == bandId);
        if (campaign is null) return NotFound(new { error = "Not found" });
        if (!Enum.TryParse<VenueCampaignStatus>(request.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = "Invalid status." });

        campaign.Status = status;
        if (status == VenueCampaignStatus.CompleteRejected)
        {
            var reason = request.RejectionReason?.Trim();
            if (string.IsNullOrEmpty(reason)) return BadRequest(new { error = "A rejection reason is required." });
            campaign.RejectionReason = reason;
            campaign.RetryDate = request.NeverRetry ? null : request.RetryDate;
            campaign.NeverRetry = request.NeverRetry;
        }
        else if (status == VenueCampaignStatus.CompleteBooked)
        {
            campaign.BookedGigId = request.BookedGigId;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, status = campaign.Status.ToString() });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
