namespace BandManager.Data.Entities;

/// <summary>
/// A gig, now a real DB row - the database is the source of truth. A
/// connected site's calendar.js (GigsSiteEditor) is a best-effort, one-way
/// publish target written to *after* this row is saved, never read from -
/// a band with no website still fully works. Ref is the stable business
/// key everything else (Flyer.GigRef, GigSet.GigRef, ScheduleItem.GigRef)
/// already keys off by string, unique per (BandId, Ref): for a gig
/// imported from an existing site during the one-time backfill, Ref
/// preserves SiteContentRef.GigRef(gig)'s old output exactly, so those
/// existing rows keep resolving with no migration of their own. A
/// brand-new gig just gets Ref = Id.ToString() - no more hashing needed.
/// </summary>
public class Gig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string Ref { get; set; }

    public required string Title { get; set; }
    public string? Venue { get; set; }
    public string? VenueUrl { get; set; }

    // Set when this gig was created from a booked VenueCampaign (see
    // GigsController.Create's optional venueId) - purely for traceability
    // back to the outreach that landed it; Venue/VenueUrl above stay the
    // real display fields, filled from the Venue record at creation time
    // but editable independently afterward.
    public Guid? VenueId { get; set; }
    public Venue? VenueEntity { get; set; }
    public DateOnly Date { get; set; }

    // Time stays free text, deliberately NOT DateOnly/TimeOnly - real data
    // holds things like "Doors: 7PM - Show: 8PM" or "TBD", not a single
    // clock time, and it's pushed verbatim to a connected site's public
    // listing (GigsSiteEditor) - forcing it into a real time column would
    // lose real, currently-displayed information. DoorsTime/OpenerTime/
    // HeadlinerTime below are genuinely single-time fields (unused in
    // production as of this conversion), so those did convert.
    public string? Time { get; set; }
    public string? Address { get; set; }

    public TimeOnly? DoorsTime { get; set; }
    public TimeOnly? OpenerTime { get; set; }
    public TimeOnly? HeadlinerTime { get; set; }

    public string? TicketsUrl { get; set; }
    public string? FlyerMain { get; set; }
    public bool FreeAdmission { get; set; }
    public string? CustomTicketsText { get; set; }
    public string? TicketMode { get; set; }

    // Explicit "this one" pick among this gig's (possibly several) Flyer
    // rows - null means no pick has been made yet, so every reader falls
    // back to the existing "most recent" resolution (GigSetsController.GetGigItems),
    // preserving today's behavior for every gig that predates this field.
    // SetNull (not Restrict) on the referenced Flyer's delete/archive, same
    // reasoning as Flyer.SourceCatalogItemId - losing the pick just means
    // falling back to most-recent again, never a hard error.
    public Guid? SelectedFlyerId { get; set; }
    public Flyer? SelectedFlyer { get; set; }

    // Soft-delete, mirroring Band.IsArchived exactly - archiving a gig
    // hides it (and cascades to its ScheduleItems/Flyers, see
    // GigsController.Archive) everywhere without touching a connected
    // site's live listing (see the tranche plan's explicit non-goal: this
    // is purely an in-app visibility toggle). GigSet/GigPrep need no flag
    // of their own - neither has an independent listing page, so hiding
    // the Gig already hides them.
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GigWithBand> WithBands { get; set; } = new List<GigWithBand>();
}

/// <summary>One supporting act on a gig, referencing a real (possibly
/// unclaimed) Band row rather than a free-text name - see Band.IsOnboarded.
/// Url stays per-appearance (e.g. a specific event-page link for this one
/// show), not assumed to live permanently on the with-band's own row.
/// SortOrder preserves display order among a gig's with-acts.</summary>
public class GigWithBand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GigId { get; set; }
    public Gig Gig { get; set; } = null!;
    public Guid WithBandId { get; set; }
    public Band WithBand { get; set; } = null!;
    public string? Url { get; set; }
    public int SortOrder { get; set; }
}
