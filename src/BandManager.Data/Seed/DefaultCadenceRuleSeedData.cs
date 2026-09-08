using BandManager.Data.Entities;

namespace BandManager.Data.Seed;

/// <summary>
/// Source data for DefaultCadenceRuleTemplate, upserted by DbSeeder into
/// the DB (not applied to any band directly from here). Modeled on Black
/// Secrets' own real, currently-running cadence - same platform mix,
/// content types, categories, and posting rhythm - with descriptions
/// genericized for any band rather than copied verbatim.
/// </summary>
public static class DefaultCadenceRuleSeedData
{
    public static readonly DefaultCadenceRuleTemplate[] All =
    [
        new() {
            Key = "facebook-recurring-photo-album-bts", SortOrder = 1,
            PlatformId = "facebook", ContentTypeId = "Photo Album", Kind = CadenceKind.Recurring,
            Category = "Behind-the-Scenes", Description = "Rehearsal or gear/stage setup photos",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Saturday"]
        },
        new() {
            Key = "facebook-recurring-text-photo-news", SortOrder = 2,
            PlatformId = "facebook", ContentTypeId = "Text + Photo", Kind = CadenceKind.Recurring,
            Category = "Band News/Updates", Description = "Setlist teaser or band update post",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Monday"]
        },
        new() {
            Key = "facebook-recurring-video-clip-live", SortOrder = 3,
            PlatformId = "facebook", ContentTypeId = "Video Clip", Kind = CadenceKind.Recurring,
            Category = "Live Performance Clip", Description = "30-60 sec clip from a recent show",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Friday"]
        },
        new() {
            Key = "facebook-gig-event-post", SortOrder = 4,
            PlatformId = "facebook", ContentTypeId = "Event Post", Kind = CadenceKind.GigEvent,
            Category = "Show Promotion", Description = "Upcoming show event w/ venue tag, ticket link",
            ManualInstructions = "Create the Event on Facebook (Page > Events > Create Event) for {title} at {venue} on {date}. Paste the Event URL when marking this done."
        },
        new() {
            Key = "facebook-gig-cover-photo", SortOrder = 5,
            PlatformId = "facebook", ContentTypeId = "Facebook Cover Photo", Kind = CadenceKind.GigCoverPhoto,
            Category = "Show Promotion", Description = "Update the Facebook cover photo to the flyer for the next show",
            ManualInstructions = "Get your cover photo from the Catalog. It's your flyer, edited to fit the space for the Facebook Cover Photo, and change your cover photo on Facebook to that one."
        },
        new() {
            Key = "instagram-recurring-reel-live", SortOrder = 6,
            PlatformId = "instagram", ContentTypeId = "Reel", Kind = CadenceKind.Recurring,
            Category = "Live Performance Clip", Description = "High-energy clip, trending audio overlay",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Monday"]
        },
        new() {
            Key = "instagram-recurring-story-bts", SortOrder = 7,
            PlatformId = "instagram", ContentTypeId = "Story", Kind = CadenceKind.Recurring,
            Category = "Behind-the-Scenes", Description = "Rehearsal, van/gear load-in, band banter",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Thursday"]
        },
        new() {
            Key = "instagram-recurring-reel-teaser", SortOrder = 8,
            PlatformId = "instagram", ContentTypeId = "Reel", Kind = CadenceKind.Recurring,
            Category = "Cover Snippet/Teaser", Description = "15-30 sec teaser of a fan-favorite song",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Saturday"]
        },
        new() {
            Key = "instagram-gig-countdown-feed-post", SortOrder = 9,
            PlatformId = "instagram", ContentTypeId = "Feed Post", Kind = CadenceKind.GigCountdown,
            Category = "Flyer/Show Promotion", Description = "Show flyer graphic w/ date, venue, ticket link in bio",
            MessageTemplates = new() { ["default"] = "Show flyer graphic w/ date, venue, ticket link in bio" }
        },
        new() {
            Key = "tiktok-recurring-trend", SortOrder = 10,
            PlatformId = "tiktok", ContentTypeId = "Short Video", Kind = CadenceKind.Recurring,
            Category = "Trend/Challenge Tie-in", Description = "A relevant trend or sound, with the band's own spin on it",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Saturday"],
            ManualInstructions = "Post this clip to TikTok directly from the app - no API path exists for TikTok posting. Mark done once it's live."
        },
        new() {
            Key = "tiktok-recurring-live-clip", SortOrder = 11,
            PlatformId = "tiktok", ContentTypeId = "Short Video", Kind = CadenceKind.Recurring,
            Category = "Live Performance Clip", Description = "Best crowd-reaction moment from the last show",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Tuesday"],
            ManualInstructions = "Post this clip to TikTok directly from the app - no API path exists for TikTok posting. Mark done once it's live."
        },
        new() {
            Key = "tiktok-recurring-bts", SortOrder = 12,
            PlatformId = "tiktok", ContentTypeId = "Short Video", Kind = CadenceKind.Recurring,
            Category = "Behind-the-Scenes/Personality", Description = "Band member intro or a funny tour moment",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Thursday"],
            ManualInstructions = "Post this clip to TikTok directly from the app - no API path exists for TikTok posting. Mark done once it's live."
        },
        new() {
            Key = "youtube-recurring-highlight-reel", SortOrder = 13,
            PlatformId = "youtube", ContentTypeId = "Short/Reel", Kind = CadenceKind.Recurring,
            Category = "Highlight Reel", Description = "3-5 min highlight reel from the most recent show",
            ScheduleType = Entities.ScheduleType.Monthly, ScheduleDays = ["15"],
            ManualInstructions = "Upload via YouTube Studio (studio.youtube.com > Create > Upload). Mark done once it's live."
        },
        new() {
            Key = "youtube-recurring-full-song", SortOrder = 14,
            PlatformId = "youtube", ContentTypeId = "Full Video", Kind = CadenceKind.Recurring,
            Category = "Full Song Performance", Description = "Full-length HQ video of one live song",
            ScheduleType = Entities.ScheduleType.Monthly, ScheduleDays = ["1"],
            ManualInstructions = "Upload via YouTube Studio (studio.youtube.com > Create > Upload). Mark done once it's live."
        },
        new() {
            Key = "youtube-recurring-vlog-bts", SortOrder = 15,
            PlatformId = "youtube", ContentTypeId = "Vlog/BTS", Kind = CadenceKind.Recurring,
            Category = "Behind-the-Scenes", Description = "Load-in, soundcheck, tour life footage",
            ScheduleType = Entities.ScheduleType.Monthly, ScheduleDays = ["end-of-month"],
            ManualInstructions = "Upload via YouTube Studio (studio.youtube.com > Create > Upload). Mark done once it's live."
        },
        new() {
            Key = "googleBusiness-recurring-photo-upload", SortOrder = 16,
            PlatformId = "googleBusiness", ContentTypeId = "Photo Upload", Kind = CadenceKind.Recurring,
            Category = "Live Performance/Venue Photos", Description = "Fresh photos from the most recent gig",
            ScheduleType = Entities.ScheduleType.Monthly, ScheduleDays = ["1"]
        },
        new() {
            Key = "googleBusiness-recurring-post-update", SortOrder = 17,
            PlatformId = "googleBusiness", ContentTypeId = "Post Update", Kind = CadenceKind.Recurring,
            Category = "Show Promotion/News", Description = "Upcoming show or band update post",
            ScheduleType = Entities.ScheduleType.Weekly, ScheduleDays = ["Monday"]
        },
        new() {
            Key = "spotify-recurring-artist-pick", SortOrder = 18,
            PlatformId = "spotify", ContentTypeId = "Artist Pick/Playlist", Kind = CadenceKind.Recurring,
            Category = "Band Presence", Description = "Curate an artist playlist mixing originals and influences",
            ScheduleType = Entities.ScheduleType.Monthly, ScheduleDays = ["end-of-month"],
            ManualInstructions = "Update directly in Spotify for Artists / Apple Music for Artists - no API path exists for this. Mark done once it's live."
        }
    ];
}
