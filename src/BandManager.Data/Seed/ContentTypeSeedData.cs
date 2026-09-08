using BandManager.Data.Entities;

namespace BandManager.Data.Seed;

/// <summary>
/// Global reference data - what a piece of content IS and what it needs
/// to be complete, plus which content types are offered per platform.
/// Ported from the old app's src/seedCadence.js (CONTENT_TYPES /
/// PLATFORM_CONTENT_TYPES only - RECURRING_RULES/GIG_RULES were Black
/// Secrets' own example cadence content, not reference data, so those
/// don't belong here; they're part of the Phase 6 data migration instead).
/// </summary>
public static class ContentTypeSeedData
{
    public static readonly ContentType[] All =
    [
        new() { Id = "Text + Photo", RequiredArtifacts = ["caption", "photo"], SortOrder = 1 },
        // Facebook has no Events API access here, so this is never
        // automated (Scheduler.cs sets NoApi=true unconditionally for
        // gig-only items) - "posting" always means creating the event by
        // hand on Facebook. RequiredArtifacts still matters even so: it's
        // what makes the detail modal's artifact rows (photo, caption)
        // appear at all - date/time/venue come from the linked gig itself
        // via renderGigReference in dashboard.js, not from an artifact.
        new() { Id = "Event Post", RequiredArtifacts = ["photo", "caption"], SortOrder = 2 },
        new() { Id = "Video Clip", RequiredArtifacts = ["caption", "video"], SortOrder = 3 },
        new() { Id = "Photo Album", RequiredArtifacts = ["caption", "photo"], SortOrder = 4 },
        new() { Id = "Reel", RequiredArtifacts = ["video"], SortOrder = 5 },
        new() { Id = "Feed Post", RequiredArtifacts = ["flyer"], SortOrder = 6 },
        new() { Id = "Story", RequiredArtifacts = ["photo"], SortOrder = 7 },
        new() { Id = "Short Video", RequiredArtifacts = ["video"], SortOrder = 8 },
        new() { Id = "Full Video", RequiredArtifacts = ["video"], SortOrder = 9 },
        new() { Id = "Short/Reel", RequiredArtifacts = ["video"], SortOrder = 10 },
        new() { Id = "Vlog/BTS", RequiredArtifacts = ["video"], SortOrder = 11 },
        new() { Id = "Post Update", RequiredArtifacts = ["caption"], SortOrder = 12 },
        new() { Id = "Photo Upload", RequiredArtifacts = ["photo"], SortOrder = 13 },
        new() { Id = "Event Listing", RequiredArtifacts = [], SortOrder = 14 },
        new() { Id = "Reminder Push", RequiredArtifacts = [], SortOrder = 15 },
        new() { Id = "Artist Pick/Playlist", RequiredArtifacts = ["caption"], SortOrder = 16 },
        new() { Id = "Canvas/Cover Upload", RequiredArtifacts = ["audio"], SortOrder = 17 },
        // No uploaded artifacts - "complete" is judged by the gig record's
        // own fields (venue, flyer, ticket link), edited directly, not files.
        new() { Id = "Calendar Listing", RequiredArtifacts = [], SortOrder = 18 },
        // Same idea - complete is judged by the media entry's own fields
        // (tile art, video/audio link) via its dedicated editor.
        new() { Id = "Media Item", RequiredArtifacts = [], SortOrder = 19 },
        // Same again - complete is judged by whether the gallery entry has
        // a photo, via its own editor.
        new() { Id = "Gallery Image", RequiredArtifacts = [], SortOrder = 20 },
        // Not cadence-rule-driven - generated directly by the scheduler
        // for whichever gig is currently soonest, one at a time. The
        // required 'photo' artifact is normally already attached by the
        // time the tile appears (auto-generated from that gig's flyer).
        new() { Id = "Facebook Cover Photo", RequiredArtifacts = ["photo"], SortOrder = 21 }
    ];

    public static readonly Dictionary<string, string[]> PlatformContentTypes = new()
    {
        // Facebook Cover Photo sits here (not just gated by kind) so the
        // Cadence editor's Content Type dropdown actually offers it - same
        // "listed here, but blocked from ad-hoc creation" shape as Event Post.
        ["facebook"] = ["Text + Photo", "Video Clip", "Photo Album", "Event Post", "Facebook Cover Photo"],
        ["instagram"] = ["Reel", "Story", "Feed Post", "Photo Album"],
        ["tiktok"] = ["Short Video"],
        ["youtube"] = ["Full Video", "Short/Reel", "Vlog/BTS"],
        ["googleBusiness"] = ["Post Update", "Photo Upload"],
        ["bandsintown"] = ["Event Listing", "Reminder Push"],
        ["spotify"] = ["Artist Pick/Playlist", "Canvas/Cover Upload"],
        ["website"] = ["Calendar Listing", "Media Item", "Gallery Image"]
    };

    /// <summary>Content types that only ever come from cadence/gig
    /// generation, never offered for manual ad-hoc creation - mirrors the
    /// old app's GIG_ONLY_CONTENT_TYPES in routes/api.js.</summary>
    public static readonly HashSet<string> GigOnlyContentTypes =
        ["Event Post", "Event Listing", "Reminder Push", "Facebook Cover Photo"];
}
