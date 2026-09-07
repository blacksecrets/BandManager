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
    public required string Date { get; set; }
    public string? Time { get; set; }
    public string? Address { get; set; }

    public string? DoorsTime { get; set; }
    public string? OpenerTime { get; set; }
    public string? HeadlinerTime { get; set; }

    public string? TicketsUrl { get; set; }
    public string? FlyerMain { get; set; }
    public bool FreeAdmission { get; set; }
    public string? CustomTicketsText { get; set; }
    public string? TicketMode { get; set; }

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
