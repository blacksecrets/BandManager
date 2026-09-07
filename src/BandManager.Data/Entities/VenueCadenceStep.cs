namespace BandManager.Data.Entities;

public enum CommunicationType { Email = 0, Phone = 1 }

/// <summary>
/// One step in this band's outreach cadence (BandAdmin-managed, like
/// BandInstrument/CadenceRule) - step 1 is the initial contact
/// (DaysAfterPrevious usually 0, due the moment a campaign starts),
/// steps 2+ are follow-ups due N days after the previous step's
/// communication was logged. Every VenueCampaign walks the same
/// sequence, tracked via VenueCampaign.CurrentStepNumber. The default
/// subject/body doubles as an email template or a phone script
/// depending on Type - editable per-send on the campaign detail page,
/// this row is just the starting point.
/// </summary>
public class VenueCadenceStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public int StepNumber { get; set; }
    public int DaysAfterPrevious { get; set; }
    public CommunicationType Type { get; set; }
    public string? DefaultSubject { get; set; }
    public required string DefaultBody { get; set; }
    public bool Active { get; set; } = true;
}
