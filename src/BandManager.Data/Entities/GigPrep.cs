namespace BandManager.Data.Entities;

public enum GigPrepListType { Pre = 0, Packing = 1, Post = 2 }

/// <summary>
/// One user's personal default gig-prep item - the starting content for
/// every new gig's checklist (see GigPrepChecklistItem). ActId null means
/// a global default (the set edited on the Profile page, follows the
/// person the same way Gear does); non-null scopes it to one of the
/// person's Acts, e.g. a different Packing list for an electric set vs.
/// an unplugged one - created via the Gig Prep checklist's own "save as
/// your default" checkbox, not on the Profile page (see
/// GigPrepController.GetEffectiveDefaultsAsync for how the two combine:
/// per list type, an Act-scoped set wins if the person has one, else the
/// global set is used). SetNull on Act delete rather than Restrict - a
/// stale personal checklist preference shouldn't block deleting an Act,
/// it just falls back to the global set. Packing-type items are usually
/// added by dragging from the user's own Gear list, but the text is a
/// plain snapshot at that moment (see GigPrepChecklistItem's doc comment
/// for why), not a live reference back to a Gear row.
/// </summary>
public class GigPrepDefaultItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid? ActId { get; set; }
    public Act? ActEntity { get; set; }
    public GigPrepListType ListType { get; set; }
    public required string Text { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// One item on one user's gig-prep checklist for one specific Gig - Pre-
/// gig, Packing, or Post-gig, per GigPrepListType. Per-user, not shared
/// with the rest of the band (each member preps their own gear/tasks).
/// Text is a plain snapshot, whether typed by hand, picked from the
/// user's GigPrepDefaultItem library, or dragged from their Gear list -
/// never a live FK back to Gear, so a checklist from a past gig still
/// reads correctly even if that gear item is later renamed or sold.
/// GigPrepController lazily copies the user's current default set into
/// a Gig the first time they open its checklist (if they have one) -
/// nothing is materialized until then.
/// </summary>
public class GigPrepChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GigId { get; set; }
    public Gig Gig { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public GigPrepListType ListType { get; set; }
    public required string Text { get; set; }
    public bool IsChecked { get; set; }
    public int SortOrder { get; set; }
}
