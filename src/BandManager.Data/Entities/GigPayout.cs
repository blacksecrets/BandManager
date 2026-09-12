namespace BandManager.Data.Entities;

/// <summary>
/// One gig's own payout record - the top half of Gig Management's
/// Accounting view ("what did the band get paid, by whom, how"). Created
/// lazily on first save (see AccountingController), not for every Gig up
/// front. Editable by BandAdmin/SuperAdmin only; every other BandMember
/// can view it - see the Accounting endpoints' own authorization.
/// </summary>
public class GigPayout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GigId { get; set; }
    public Gig Gig { get; set; } = null!;

    public decimal? Amount { get; set; }

    // Only FirstName is required (per the request) - a payer identified by
    // just a first name (a familiar promoter, a doorman) is still worth
    // recording rather than blocking the save entirely.
    public string? PaidByFirstName { get; set; }
    public string? PaidByLastName { get; set; }
    public string? PaidByOrganization { get; set; }
    public string? PaidByEmail { get; set; }
    public string? PaidByPhone { get; set; }

    public PayoutMethod? PayoutType { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One band member's cut of one specific gig's payout - IsPaid/PayoutType
/// are the only state genuinely specific to this gig+person; the amount
/// itself is always computed live (GigPayout.Amount * the band's current
/// PayoutRecipient.Percentage for this user), never stored, so it can't
/// drift out of sync with the band's roster. Rows are created lazily,
/// mirroring whichever users currently appear in the band's
/// PayoutRecipient roster - see AccountingController.GetGigPayout.
/// </summary>
public class GigPayoutRecipient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GigId { get; set; }
    public Gig Gig { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public bool IsPaid { get; set; }
    public PayoutMethod? PayoutType { get; set; }
    public DateTime? PaidAt { get; set; }
}
