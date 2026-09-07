using System.Text;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveVenueRequest(string Name, string? AddressLine1, string? City, string? State, string? PostalCode, string? Phone, string? Website, string? Notes);
public record SaveVenueContactRequest(string? Name, string? Title, string? Email, string? Phone, bool IsPrimary);

/// <summary>
/// This band's venue book - read/write open to any BandMember (booking
/// outreach isn't a BandAdmin-only concern, same reasoning Gig Sets'
/// rebuild used), unlike the cadence step templates
/// (VenueCadenceStepsController), which stay BandAdmin like every other
/// band-wide policy setting in this app.
/// </summary>
[ApiController]
[Route("/api/venues")]
[Authorize(Policy = "BandMember")]
public class VenuesController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private const long MaxImportBytes = 5 * 1024 * 1024;

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(Venue v) => new
    {
        id = v.Id,
        name = v.Name,
        addressLine1 = v.AddressLine1,
        city = v.City,
        state = v.State,
        postalCode = v.PostalCode,
        phone = v.Phone,
        website = v.Website,
        notes = v.Notes,
        contacts = v.Contacts.Select(c => new { id = c.Id, name = c.Name, title = c.Title, email = c.Email, phone = c.Phone, isPrimary = c.IsPrimary })
    };

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venues = await db.Venues.AsNoTracking().Include(v => v.Contacts)
            .Where(v => v.BandId == bandId).OrderBy(v => v.Name).ToListAsync();
        return Ok(venues.Select(Serialize));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venue = await db.Venues.AsNoTracking().Include(v => v.Contacts).FirstOrDefaultAsync(v => v.Id == id && v.BandId == bandId);
        if (venue is null) return NotFound(new { error = "Not found" });
        return Ok(Serialize(venue));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveVenueRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Venue name is required." });

        var venue = new Venue
        {
            BandId = bandId,
            Name = name,
            AddressLine1 = Clean(request.AddressLine1),
            City = Clean(request.City),
            State = Clean(request.State),
            PostalCode = Clean(request.PostalCode),
            Phone = Clean(request.Phone),
            Website = Clean(request.Website),
            Notes = Clean(request.Notes)
        };
        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        return Ok(Serialize(venue));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveVenueRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venue = await db.Venues.Include(v => v.Contacts).FirstOrDefaultAsync(v => v.Id == id && v.BandId == bandId);
        if (venue is null) return NotFound(new { error = "Not found" });

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Venue name is required." });

        venue.Name = name;
        venue.AddressLine1 = Clean(request.AddressLine1);
        venue.City = Clean(request.City);
        venue.State = Clean(request.State);
        venue.PostalCode = Clean(request.PostalCode);
        venue.Phone = Clean(request.Phone);
        venue.Website = Clean(request.Website);
        venue.Notes = Clean(request.Notes);
        await db.SaveChangesAsync();
        return Ok(Serialize(venue));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venue = await db.Venues.FirstOrDefaultAsync(v => v.Id == id && v.BandId == bandId);
        if (venue is null) return Ok(new { ok = true });

        db.Venues.Remove(venue); // cascades VenueContacts + VenueCampaign + its Communications
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // --- Contacts ---

    [HttpPost("{venueId:guid}/contacts")]
    public async Task<IActionResult> AddContact(Guid venueId, [FromBody] SaveVenueContactRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venue = await db.Venues.Include(v => v.Contacts).FirstOrDefaultAsync(v => v.Id == venueId && v.BandId == bandId);
        if (venue is null) return NotFound(new { error = "Not found" });

        if (request.IsPrimary)
            foreach (var c in venue.Contacts) c.IsPrimary = false;

        var contact = new VenueContact
        {
            VenueId = venueId,
            Name = Clean(request.Name),
            Title = Clean(request.Title),
            Email = Clean(request.Email),
            Phone = Clean(request.Phone),
            IsPrimary = request.IsPrimary || venue.Contacts.Count == 0
        };
        db.VenueContacts.Add(contact);
        await db.SaveChangesAsync();
        return Ok(new { id = contact.Id, name = contact.Name, title = contact.Title, email = contact.Email, phone = contact.Phone, isPrimary = contact.IsPrimary });
    }

    [HttpPut("{venueId:guid}/contacts/{contactId:guid}")]
    public async Task<IActionResult> UpdateContact(Guid venueId, Guid contactId, [FromBody] SaveVenueContactRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venue = await db.Venues.Include(v => v.Contacts).FirstOrDefaultAsync(v => v.Id == venueId && v.BandId == bandId);
        var contact = venue?.Contacts.FirstOrDefault(c => c.Id == contactId);
        if (venue is null || contact is null) return NotFound(new { error = "Not found" });

        if (request.IsPrimary)
            foreach (var c in venue.Contacts) c.IsPrimary = false;

        contact.Name = Clean(request.Name);
        contact.Title = Clean(request.Title);
        contact.Email = Clean(request.Email);
        contact.Phone = Clean(request.Phone);
        contact.IsPrimary = request.IsPrimary;
        await db.SaveChangesAsync();
        return Ok(new { id = contact.Id, name = contact.Name, title = contact.Title, email = contact.Email, phone = contact.Phone, isPrimary = contact.IsPrimary });
    }

    [HttpDelete("{venueId:guid}/contacts/{contactId:guid}")]
    public async Task<IActionResult> DeleteContact(Guid venueId, Guid contactId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var venue = await db.Venues.Include(v => v.Contacts).FirstOrDefaultAsync(v => v.Id == venueId && v.BandId == bandId);
        var contact = venue?.Contacts.FirstOrDefault(c => c.Id == contactId);
        if (venue is null || contact is null) return Ok(new { ok = true });

        db.VenueContacts.Remove(contact);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- CSV bulk import: venues + primary contact ---

    [HttpGet("import/template")]
    public IActionResult ImportTemplate()
    {
        var bytes = Encoding.UTF8.GetBytes(VenueCsvImportService.BuildVenueTemplateCsv());
        return File(bytes, "text/csv", "venues-template.csv");
    }

    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<IActionResult> Import()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });
        var form = await Request.ReadFormAsync();
        var payload = await form.Files.GetFile("file").ToUploadedFilePayloadAsync();
        if (payload is null) return BadRequest(new { error = "No file provided" });

        var parsed = VenueCsvImportService.ParseVenues(payload.Bytes);
        if (parsed.Errors.Count > 0)
            return BadRequest(new { error = $"{parsed.Errors.Count} row(s) failed validation - fix and re-upload.", rowErrors = parsed.Errors });

        var added = 0;
        var skippedDuplicates = 0;
        foreach (var row in parsed.Rows)
        {
            if (await db.Venues.AnyAsync(v => v.BandId == bandId && v.Name.ToLower() == row.Name.ToLower()))
            {
                skippedDuplicates++;
                continue;
            }

            var venue = new Venue
            {
                BandId = bandId,
                Name = row.Name,
                AddressLine1 = row.AddressLine1,
                City = row.City,
                State = row.State,
                PostalCode = row.PostalCode,
                Phone = row.Phone,
                Website = row.Website,
                Notes = row.Notes
            };
            db.Venues.Add(venue);

            if (row.ContactName is not null || row.ContactEmail is not null || row.ContactPhone is not null)
            {
                db.VenueContacts.Add(new VenueContact
                {
                    Venue = venue,
                    Name = row.ContactName,
                    Email = row.ContactEmail,
                    Phone = row.ContactPhone,
                    IsPrimary = true
                });
            }
            added++;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, added, skippedDuplicates });
    }

    // --- CSV bulk import: historical communications against existing venues ---

    [HttpGet("import-communications/template")]
    public IActionResult ImportCommunicationsTemplate()
    {
        var bytes = Encoding.UTF8.GetBytes(VenueCsvImportService.BuildCommunicationsTemplateCsv());
        return File(bytes, "text/csv", "venue-communications-template.csv");
    }

    [HttpPost("import-communications")]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<IActionResult> ImportCommunications()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });
        var form = await Request.ReadFormAsync();
        var payload = await form.Files.GetFile("file").ToUploadedFilePayloadAsync();
        if (payload is null) return BadRequest(new { error = "No file provided" });

        var parsed = VenueCsvImportService.ParseCommunications(payload.Bytes);
        if (parsed.Errors.Count > 0)
            return BadRequest(new { error = $"{parsed.Errors.Count} row(s) failed validation - fix and re-upload.", rowErrors = parsed.Errors });

        var imported = 0;
        var skippedUnknownVenue = 0;
        foreach (var row in parsed.Rows)
        {
            var venue = await db.Venues.Include(v => v.Campaign).FirstOrDefaultAsync(v => v.BandId == bandId && v.Name.ToLower() == row.VenueName.ToLower());
            if (venue is null) { skippedUnknownVenue++; continue; }

            if (venue.Campaign is null)
            {
                venue.Campaign = new VenueCampaign { BandId = bandId, VenueId = venue.Id, Status = VenueCampaignStatus.Active, StartedAt = row.OccurredAt };
                db.VenueCampaigns.Add(venue.Campaign);
            }

            db.VenueCommunications.Add(new VenueCommunication
            {
                VenueCampaignId = venue.Campaign.Id,
                Type = row.Type,
                StepNumber = 0, // historical import - not tied to a live cadence step
                OccurredAt = row.OccurredAt,
                To = row.To,
                Cc = row.Cc,
                Bcc = row.Bcc,
                From = row.From,
                Subject = row.Subject,
                Body = row.Body,
                OutcomeNotes = row.OutcomeNotes,
                LoggedByUserId = userId.Value
            });
            if (venue.Campaign.LastCommunicationAt is null || row.OccurredAt > venue.Campaign.LastCommunicationAt)
                venue.Campaign.LastCommunicationAt = row.OccurredAt;
            imported++;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true, imported, skippedUnknownVenue });
    }
}
