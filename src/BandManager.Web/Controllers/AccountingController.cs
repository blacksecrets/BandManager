using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SetPayoutDefaultsRequest(Guid? DefaultGigPayeeUserId, Guid? DefaultMerchPayeeUserId);
public record SetPayoutTermsRequest(string? Note);
public record SetPayoutRosterRequest(List<Guid> UserIds);
public record PayoutPercentageInput(Guid UserId, decimal Percentage);
public record SetPayoutPercentagesRequest(List<PayoutPercentageInput> Percentages);
public record SetGigPayoutHeaderRequest(
    decimal? Amount, string? PaidByFirstName, string? PaidByLastName,
    string? PaidByOrganization, string? PaidByEmail, string? PaidByPhone, string? PayoutType);
public record GigPayoutRecipientInput(Guid UserId, bool IsPaid, string? PayoutType);
public record SetGigPayoutRecipientsRequest(List<GigPayoutRecipientInput> Recipients);

/// <summary>
/// Band Admin > Accounting's "Receivables" control (who gets what share of
/// every gig's payout, by default) plus each Gig's own payout record
/// (Gig Management > a gig's Accounting view). Receivables config is
/// BandAdmin/SuperAdmin-only throughout (it lives under Band Admin); a
/// specific gig's payout can be viewed by any BandMember but only saved by
/// a BandAdmin/SuperAdmin - see the two GET actions' own policy override.
/// </summary>
// No class-level [Authorize] here deliberately - stacking a class-level
// policy with a looser one on a single action (as this controller needs
// for GetGigPayout, viewable by any BandMember) doesn't override, it ANDs
// the two together, so a plain BandMember would fail the class-level
// BandAdmin check regardless of the action's own attribute. Every action
// states its own required policy instead - caught this live via a 403
// that should have been a 200 while testing the "everyone can view"
// requirement.
[ApiController]
[Route("/api/accounting")]
public class AccountingController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private async Task<(Band Band, IActionResult? Error)> RequireActiveBandAsync()
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) return (null!, BadRequest(new { error = "No active band selected." }));
        var band = await db.Bands.FindAsync(id.Value);
        if (band is null) return (null!, BadRequest(new { error = "No active band selected." }));
        return (band, null);
    }

    private static object SerializeMember(ApplicationUser u) => new
    {
        userId = u.Id,
        firstName = u.FirstName,
        lastName = u.LastName,
        email = u.Email
    };

    // --- Payout Terms (D7): plain-language "how you get paid" -----------
    // Read is BandMember (every member should be able to read the terms
    // without asking), write is BandAdmin-only - the same access split
    // Receivables/GetGigPayout already use for view-vs-edit.
    [HttpGet("payout-terms")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetPayoutTerms()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        return Ok(new { note = band.PayoutTermsNote });
    }

    [HttpPut("payout-terms")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetPayoutTerms([FromBody] SetPayoutTermsRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var note = request.Note?.Trim();
        band.PayoutTermsNote = string.IsNullOrEmpty(note) ? null : note;
        await db.SaveChangesAsync();
        return Ok(new { ok = true, note = band.PayoutTermsNote });
    }

    // --- Receivables (Band Admin > Accounting) ---

    [HttpGet("receivables")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> GetReceivables()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var members = await db.BandMemberships.Where(m => m.BandId == band.Id)
            .Include(m => m.User).Select(m => m.User).OrderBy(u => u.FirstName ?? u.UserName).ToListAsync();

        var recipients = await db.PayoutRecipients.AsNoTracking()
            .Where(p => p.BandId == band.Id).Include(p => p.User).ToListAsync();

        return Ok(new
        {
            defaultGigPayeeUserId = band.DefaultGigPayeeUserId,
            defaultMerchPayeeUserId = band.DefaultMerchPayeeUserId,
            bandMembers = members.Select(SerializeMember),
            recipients = recipients.OrderBy(r => r.User.FirstName ?? r.User.UserName).Select(r => new
            {
                userId = r.UserId,
                firstName = r.User.FirstName,
                lastName = r.User.LastName,
                email = r.User.Email,
                percentage = r.Percentage
            })
        });
    }

    [HttpPut("receivables/defaults")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetReceivablesDefaults([FromBody] SetPayoutDefaultsRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        if (request.DefaultGigPayeeUserId is { } gigPayeeId && !await db.BandMemberships.AnyAsync(m => m.BandId == band.Id && m.UserId == gigPayeeId))
            return BadRequest(new { error = "That person isn't a member of this band." });
        if (request.DefaultMerchPayeeUserId is { } merchPayeeId && !await db.BandMemberships.AnyAsync(m => m.BandId == band.Id && m.UserId == merchPayeeId))
            return BadRequest(new { error = "That person isn't a member of this band." });

        band.DefaultGigPayeeUserId = request.DefaultGigPayeeUserId;
        band.DefaultMerchPayeeUserId = request.DefaultMerchPayeeUserId;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Adds/removes PayoutRecipient rows to match the multi-select's current
    // choice - a newly-added person starts at 0% (the grid's own Save,
    // SetPercentages below, is what actually assigns real shares).
    [HttpPut("receivables/roster")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetReceivablesRoster([FromBody] SetPayoutRosterRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var validUserIds = await db.BandMemberships.Where(m => m.BandId == band.Id).Select(m => m.UserId).ToListAsync();
        var wantedIds = request.UserIds.Where(id => validUserIds.Contains(id)).ToHashSet();

        var existing = await db.PayoutRecipients.Where(p => p.BandId == band.Id).ToListAsync();
        var existingIds = existing.Select(e => e.UserId).ToHashSet();

        foreach (var toRemove in existing.Where(e => !wantedIds.Contains(e.UserId)))
            db.PayoutRecipients.Remove(toRemove);

        foreach (var newUserId in wantedIds.Where(id => !existingIds.Contains(id)))
            db.PayoutRecipients.Add(new PayoutRecipient { BandId = band.Id, UserId = newUserId, Percentage = 0 });

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("receivables/percentages")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetReceivablesPercentages([FromBody] SetPayoutPercentagesRequest request)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var total = request.Percentages.Sum(p => p.Percentage);
        if (total != 100)
            return BadRequest(new { error = $"Percentages must add up to exactly 100 (currently {total})." });
        if (request.Percentages.Any(p => p.Percentage < 0 || p.Percentage > 100))
            return BadRequest(new { error = "Each percentage must be between 0 and 100." });

        var recipients = await db.PayoutRecipients.Where(p => p.BandId == band.Id).ToListAsync();
        var byUserId = recipients.ToDictionary(r => r.UserId);
        foreach (var input in request.Percentages)
        {
            if (byUserId.TryGetValue(input.UserId, out var recipient)) recipient.Percentage = input.Percentage;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- A specific Gig's payout (Gig Management > a gig's Accounting view) ---

    private async Task<(Band Band, Gig Gig, IActionResult? Error)> RequireGigAsync(string gigRef)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return (null!, null!, err);
        var gig = await db.Gigs.FirstOrDefaultAsync(g => g.BandId == band.Id && g.Ref == gigRef);
        if (gig is null) return (null!, null!, NotFound(new { error = "Gig not found" }));
        return (band, gig, null);
    }

    // A member's own payout status across every gig in the active band
    // where they're on the payout roster - the "My <band>" > Accounting
    // page's data source. Read-only (all the actual editing stays under
    // Band Admin > Band Accounting / a gig's own Accounting view) - same
    // live-computed-amount rule as GetGigPayout above.
    [HttpGet("my-payouts")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetMyPayouts()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        var userId = User.GetUserId()!.Value;

        var myPercentage = await db.PayoutRecipients.AsNoTracking()
            .Where(p => p.BandId == band.Id && p.UserId == userId).Select(p => (decimal?)p.Percentage).FirstOrDefaultAsync();
        if (myPercentage is null) return Ok(Array.Empty<object>());

        // One query - only gigs that actually have a payout row for this
        // band, joined straight to this user's own recipient row (if any)
        // instead of loading every Gig's full columns just to filter/
        // dictionary-build them in memory afterward.
        var rows = await (
            from payout in db.GigPayouts.AsNoTracking()
            where payout.Gig.BandId == band.Id
            join myRow in db.GigPayoutRecipients.AsNoTracking().Where(r => r.UserId == userId)
                on payout.GigId equals myRow.GigId into myRowJoin
            from myRow in myRowJoin.DefaultIfEmpty()
            orderby payout.Gig.Date descending
            select new
            {
                gigRef = payout.Gig.Ref,
                gigTitle = payout.Gig.Title,
                date = payout.Gig.Date,
                amount = payout.Amount,
                isPaid = myRow != null && myRow.IsPaid,
                payoutType = myRow != null ? myRow.PayoutType : null
            }
        ).ToListAsync();

        var result = rows.Select(r => new
        {
            r.gigRef,
            r.gigTitle,
            date = r.date.ToString("yyyy-MM-dd"),
            amount = r.amount is { } total ? Math.Round(total * myPercentage.Value / 100m, 2) : (decimal?)null,
            r.isPaid,
            payoutType = r.payoutType?.ToString()
        });
        return Ok(result);
    }

    // D13: year/quarter earnings rollup, beyond the existing per-gig view -
    // BandAdmin-only (unlike my-payouts/gigs/{gigRef} above, which any
    // member can see their own slice of) since this is the whole band's
    // gross figures, not just the caller's own cut. Per-member totals use
    // each member's CURRENT PayoutRecipient.Percentage applied
    // retroactively across the whole year, same live-calculation
    // convention GetMyPayouts/GetGigPayout already use - there's no
    // historical snapshot of what the split was on any past date, so a
    // member who joined partway through the year still shows a total as
    // if they'd always held their current share. Only gigs with a real,
    // non-null payout amount count.
    [HttpGet("summary")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> GetFinancialSummary([FromQuery] int year)
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;
        if (year < 2000 || year > 2100) return BadRequest(new { error = "Not a valid year." });

        var rows = await db.GigPayouts.AsNoTracking()
            .Where(p => p.Gig.BandId == band.Id && p.Amount != null && p.Gig.Date.Year == year)
            .Select(p => new { p.Gig.Date, Amount = p.Amount!.Value })
            .ToListAsync();

        var quarters = Enumerable.Range(1, 4).Select(q =>
        {
            var inQuarter = rows.Where(r => (r.Date.Month - 1) / 3 + 1 == q).ToList();
            return new { quarter = q, grossAmount = inQuarter.Sum(r => r.Amount), gigCount = inQuarter.Count };
        }).ToList();

        var roster = await db.PayoutRecipients.AsNoTracking()
            .Where(p => p.BandId == band.Id).Include(p => p.User).ToListAsync();
        var totalGross = rows.Sum(r => r.Amount);
        var byMember = roster.Select(r => new
        {
            userId = r.UserId,
            name = r.User.DisplayName,
            r.Percentage,
            totalReceived = Math.Round(totalGross * r.Percentage / 100m, 2)
        }).OrderByDescending(m => m.totalReceived).ToList();

        return Ok(new { year, totalGross, gigCount = rows.Count, quarters, byMember });
    }

    [HttpGet("summary/years")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> GetFinancialSummaryYears()
    {
        var (band, err) = await RequireActiveBandAsync();
        if (err is not null) return err;

        var years = await db.GigPayouts.AsNoTracking()
            .Where(p => p.Gig.BandId == band.Id && p.Amount != null)
            .Select(p => p.Gig.Date.Year)
            .Distinct().OrderByDescending(y => y).ToListAsync();
        return Ok(years);
    }

    [HttpGet("gigs/{gigRef}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetGigPayout(string gigRef)
    {
        var (band, gig, err) = await RequireGigAsync(gigRef);
        if (err is not null) return err;

        var payout = await db.GigPayouts.AsNoTracking().FirstOrDefaultAsync(p => p.GigId == gig.Id);
        var roster = await db.PayoutRecipients.AsNoTracking().Where(p => p.BandId == band.Id).Include(p => p.User).ToListAsync();
        var existingRecipients = await db.GigPayoutRecipients.AsNoTracking().Where(r => r.GigId == gig.Id).ToDictionaryAsync(r => r.UserId);

        return Ok(new
        {
            amount = payout?.Amount,
            paidByFirstName = payout?.PaidByFirstName,
            paidByLastName = payout?.PaidByLastName,
            paidByOrganization = payout?.PaidByOrganization,
            paidByEmail = payout?.PaidByEmail,
            paidByPhone = payout?.PaidByPhone,
            payoutType = payout?.PayoutType?.ToString(),
            recipients = roster.OrderBy(r => r.User.FirstName ?? r.User.UserName).Select(r =>
            {
                existingRecipients.TryGetValue(r.UserId, out var existing);
                return new
                {
                    userId = r.UserId,
                    firstName = r.User.FirstName,
                    lastName = r.User.LastName,
                    email = r.User.Email,
                    percentage = r.Percentage,
                    // Always computed live from the band's current roster,
                    // never stored - see GigPayoutRecipient's doc comment.
                    amount = payout?.Amount is { } total ? Math.Round(total * r.Percentage / 100m, 2) : (decimal?)null,
                    isPaid = existing?.IsPaid ?? false,
                    payoutType = existing?.PayoutType?.ToString()
                };
            })
        });
    }

    [HttpPut("gigs/{gigRef}/header")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetGigPayoutHeader(string gigRef, [FromBody] SetGigPayoutHeaderRequest request)
    {
        var (_, gig, err) = await RequireGigAsync(gigRef);
        if (err is not null) return err;

        if (request.Amount is < 0) return BadRequest(new { error = "Payout amount can't be negative." });
        PayoutMethod? payoutType = null;
        if (!string.IsNullOrEmpty(request.PayoutType))
        {
            if (!Enum.TryParse<PayoutMethod>(request.PayoutType, ignoreCase: true, out var parsed))
                return BadRequest(new { error = "Invalid payout type." });
            payoutType = parsed;
        }

        var payout = await db.GigPayouts.FirstOrDefaultAsync(p => p.GigId == gig.Id);
        if (payout is null)
        {
            payout = new GigPayout { GigId = gig.Id };
            db.GigPayouts.Add(payout);
        }
        payout.Amount = request.Amount;
        payout.PaidByFirstName = request.PaidByFirstName?.Trim();
        payout.PaidByLastName = request.PaidByLastName?.Trim();
        payout.PaidByOrganization = request.PaidByOrganization?.Trim();
        payout.PaidByEmail = request.PaidByEmail?.Trim();
        payout.PaidByPhone = request.PaidByPhone?.Trim();
        payout.PayoutType = payoutType;
        payout.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Saves each recipient's paid/how-paid state and notifies anyone whose
    // paid status actually changed - "newly paid" gets told they were paid,
    // someone flipped back to unpaid (correcting a mistake) gets told
    // they're not yet paid. Only fires on a real change, not every save,
    // so re-saving the grid for an unrelated reason doesn't spam everyone.
    [HttpPut("gigs/{gigRef}/recipients")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetGigPayoutRecipients(string gigRef, [FromBody] SetGigPayoutRecipientsRequest request)
    {
        var (band, gig, err) = await RequireGigAsync(gigRef);
        if (err is not null) return err;

        var validUserIds = (await db.PayoutRecipients.Where(p => p.BandId == band.Id).Select(p => p.UserId).ToListAsync()).ToHashSet();
        var existing = await db.GigPayoutRecipients.Where(r => r.GigId == gig.Id).ToDictionaryAsync(r => r.UserId);

        foreach (var input in request.Recipients)
        {
            if (!validUserIds.Contains(input.UserId)) continue;

            PayoutMethod? payoutType = null;
            if (!string.IsNullOrEmpty(input.PayoutType))
            {
                if (!Enum.TryParse<PayoutMethod>(input.PayoutType, ignoreCase: true, out var parsed))
                    return BadRequest(new { error = "Invalid payout type." });
                payoutType = parsed;
            }

            existing.TryGetValue(input.UserId, out var row);
            var wasPaid = row?.IsPaid ?? false;

            if (row is null)
            {
                row = new GigPayoutRecipient { GigId = gig.Id, UserId = input.UserId };
                db.GigPayoutRecipients.Add(row);
            }
            row.IsPaid = input.IsPaid;
            row.PayoutType = payoutType;
            row.PaidAt = input.IsPaid ? (row.PaidAt ?? DateTime.UtcNow) : null;

            if (wasPaid != input.IsPaid)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = input.UserId,
                    BandId = band.Id,
                    GigId = gig.Id,
                    Kind = NotificationKind.PayoutStatusChanged,
                    Message = input.IsPaid
                        ? $"You were paid for {gig.Title}."
                        : $"You have not yet been paid for {gig.Title}."
                });
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
