using Microsoft.AspNetCore.Identity;

namespace BandManager.Data.Entities;

/// <summary>
/// SuperAdmin is a single global flag, not a per-band membership - it
/// bypasses band-scoping checks entirely and sees every Band without
/// needing a BandMembership row (see BandMemberRequirementHandler).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public bool IsSuperAdmin { get; set; }

    // Set whenever an admin creates an account or resets someone's
    // password on their behalf (they know the temp password, the account
    // owner doesn't yet have their own) - checked at login to redirect
    // straight to Profile with a message instead of the dashboard, and
    // cleared the moment ChangePassword succeeds.
    public bool MustChangePassword { get; set; }

    // Self-service only (Profile), never collected at account creation -
    // shown in the song-edit-review workflow ("who proposed this change")
    // and wherever a band member picks/displays another member (task
    // assignment, availability, rehearsals). Null until the user sets it -
    // see DisplayName below for the fallback every caller should use
    // instead of reading this directly.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // The one place "what do we call this person" is decided - was
    // duplicated locally in SongEditRequestsController before being
    // promoted here for the band-member picker (task assignment) to reuse
    // too, rather than triplicating the same fallback a third time.
    public string DisplayName => FirstName ?? UserName!.Split('@')[0];

    // Self-service contact/address, same "nobody's forced to fill this
    // in" spirit as FirstName. Address fields are US-shaped (see
    // AddressLookupService's USPS integration) - AddressLine2 is the
    // only optional one (apartment/suite/unit).
    public string? CellNumber { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    // "thumbnails" or "details" - which grid layout Images and Flyers
    // opens to. Null (never chosen yet) is treated as "thumbnails" by the
    // frontend, not defaulted here, matching CellNumber/etc.'s "nobody's
    // forced to have an opinion yet" pattern.
    public string? CatalogViewMode { get; set; }

    // Separate from CatalogViewMode above - the Flyers sub-tab gets its
    // own independent Thumbnails/Details choice rather than sharing one
    // value with the General sub-tab (they're different-sized libraries
    // with different natural defaults).
    public string? CatalogViewModeFlyers { get; set; }

    public ICollection<BandMembership> BandMemberships { get; set; } = new List<BandMembership>();
    public ICollection<Gear> Gear { get; set; } = new List<Gear>();
}
