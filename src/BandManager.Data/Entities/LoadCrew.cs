namespace BandManager.Data.Entities;

public enum LoadCrewListType { LoadIn, LoadOut }

/// <summary>
/// One Act's standard load-in/load-out task ("who sets up drums," "who
/// talks to the sound engineer") - the starting content copied onto every
/// new gig's checklist for that Act. Deliberately BAND-wide and shared,
/// unlike GigPrepDefaultItem/GigPrepChecklistItem (which are explicitly
/// per-user, private prep) - load-in/out crew duties are a coordination
/// tool between members, not a personal list, so there's no UserId here
/// at all. Editing the template is BandAdmin-only (it shapes every future
/// gig for this Act); the per-gig checklist it seeds is open to any
/// member to adjust for an odd venue.
/// </summary>
public class LoadCrewDefaultItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActId { get; set; }
    public Act Act { get; set; } = null!;
    public LoadCrewListType ListType { get; set; }
    public required string Text { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// One item on one Gig's shared load-in/load-out checklist - copied from
/// the Gig's Act's LoadCrewDefaultItem set the first time anyone opens it
/// (same lazy-materialize-on-first-open pattern GigPrepController already
/// uses, just band-wide instead of per-user here). AssigneeUserId is
/// optional - "who's responsible for this" is exactly the coordination
/// question this feature exists to answer, but not every line needs one
/// (e.g. a note like "share drums with the opener" may not have a single
/// owner).
/// </summary>
public class LoadCrewChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GigId { get; set; }
    public Gig Gig { get; set; } = null!;
    public LoadCrewListType ListType { get; set; }
    public required string Text { get; set; }
    public bool IsChecked { get; set; }
    public int SortOrder { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public ApplicationUser? AssigneeUser { get; set; }
}
