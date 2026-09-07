namespace BandManager.Data.Entities;

public enum VenueCampaignStatus
{
    Active = 0,
    Paused = 1, // tentative acceptance - cadence holds, not stopped
    CompleteBooked = 2,
    CompleteRejected = 3
}

/// <summary>
/// All past and future outreach to one Venue - one campaign per Venue
/// (a rejected campaign with a RetryDate reopens itself back to Active
/// rather than a new campaign row being created - see
/// VenueOutreachBackgroundService). CurrentStepNumber points at the
/// VenueCadenceStep due next; a step counts as "due" once
/// LastCommunicationAt (or StartedAt, before the first communication)
/// plus that step's DaysAfterPrevious has passed - computed on read
/// (VenueCampaignsController), not materialized, since there's nothing
/// to generate ahead of time the way ScheduleItems are.
/// </summary>
public class VenueCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    public VenueCampaignStatus Status { get; set; } = VenueCampaignStatus.Active;
    public int CurrentStepNumber { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCommunicationAt { get; set; }

    // Set only when Status is CompleteRejected.
    public string? RejectionReason { get; set; }
    public DateOnly? RetryDate { get; set; }
    public bool NeverRetry { get; set; }

    // Set only when Status is CompleteBooked - the Gig created from this
    // campaign's success (see GigsController's optional venueId/
    // venueCampaignId on create). SetNull, not Cascade: deleting the Gig
    // later shouldn't delete the campaign's own history.
    public Guid? BookedGigId { get; set; }
    public Gig? BookedGig { get; set; }

    public ICollection<VenueCommunication> Communications { get; set; } = new List<VenueCommunication>();
}

/// <summary>One logged contact attempt - an email actually sent (or at
/// least composed and recorded) or a phone call made, against one step
/// in the cadence. Email fields are null for a Phone-type row and vice
/// versa (Body holds the phone script in that case) - not split into
/// separate tables since exactly one of the two ever applies per row.</summary>
public class VenueCommunication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VenueCampaignId { get; set; }
    public VenueCampaign VenueCampaign { get; set; } = null!;

    public CommunicationType Type { get; set; }
    public int StepNumber { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string? From { get; set; }
    public string? Subject { get; set; }
    public required string Body { get; set; } // email body, or phone script/notes

    public string? OutcomeNotes { get; set; }

    public Guid LoggedByUserId { get; set; }
    public ApplicationUser LoggedByUser { get; set; } = null!;
}
