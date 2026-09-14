namespace BandManager.Data.Entities;

/// <summary>
/// One band member's reimbursable purchase - distinct from Trip
/// (Travel.cs), which is a mileage log; this is a normal expense with a
/// receipt. Personal to the member who entered it (only they can edit/
/// delete it), same "your own log, band admin has oversight but doesn't
/// own the data" split GigPayoutRecipient.IsPaid already uses - a
/// BandAdmin can mark IsReimbursed but doesn't edit the expense itself.
/// </summary>
public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public required string Purpose { get; set; }
    public string? Vendor { get; set; }
    public string? VendorUrl { get; set; }

    // Null until a receipt is uploaded - creating the expense record and
    // attaching a receipt are two separate steps, same as the rest of
    // this app's "save first, upload after" file patterns (e.g. a Venue
    // campaign's Flyer). Stored as a plain filename under data/receipts,
    // same convention as ApplicationUser.AvatarFileName.
    public string? ReceiptFileName { get; set; }

    public DateOnly PurchaseDate { get; set; }
    public decimal Amount { get; set; }
    public bool IsReimbursed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
