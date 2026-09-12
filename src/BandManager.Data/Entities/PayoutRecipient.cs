namespace BandManager.Data.Entities;

public enum PayoutMethod { Cash, Check, Electronic }

/// <summary>
/// One band member's standing share of a gig's net payout - the roster
/// edited on Band Admin > Accounting's "Receivables" control. This is the
/// one, current, band-wide split; a specific gig's own payout (see
/// GigPayoutRecipient) always divides using whatever this table says
/// *right now*, not a snapshot frozen at some earlier date - simplest
/// model that matches "amounts as calculated from the payout amount and
/// individual percentages" (a live calculation, not a stored one).
/// </summary>
public class PayoutRecipient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    // 0-100, validated both client-side and server-side. The full roster's
    // percentages must sum to exactly 100 before the grid's Save enables -
    // enforced again server-side in AccountingController, not just trusted
    // from the client.
    public decimal Percentage { get; set; }
}
