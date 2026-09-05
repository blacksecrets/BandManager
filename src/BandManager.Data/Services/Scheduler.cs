using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data.Services;

/// <summary>
/// Generates ScheduleItems from CadenceRules (and, for the gig-driven
/// kinds, the Band's own live gig/media/gallery data) for one Band -
/// ported from the old app's src/scheduler.js. Idempotent: re-running
/// never duplicates a row (checked via existence query rather than
/// relying on a DB unique-constraint catch, since Postgres treats each
/// NULL as distinct in a unique index the same way SQLite does, and
/// several fields here are legitimately nullable). uploadsRootPath is a
/// plain constructor parameter (same pattern as CatalogStore's
/// catalogRootPath), resolved by the Web project's DI registration.
/// </summary>
public class Scheduler(
    ApplicationDbContext db,
    GigsSource gigsSource,
    MediaSource mediaSource,
    GallerySource gallerySource,
    FlyerCache flyerCache,
    CatalogStore catalogStore,
    string uploadsRootPath)
{
    private static readonly string[] Weekdays =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private const int GigCountdownMaxWeeksOut = 12;

    private static DateOnly NextOccurrenceOnOrAfter(DateOnly baseDate, string weekdayName)
    {
        var target = Array.IndexOf(Weekdays, weekdayName);
        var d = baseDate;
        while ((int)d.DayOfWeek != target) d = d.AddDays(1);
        return d;
    }

    /// <summary>Rolling window: today, plus the next weeksAhead weeks, so
    /// items appear before they're due and generation can just be re-run
    /// often (e.g. on a timer, or on every Dashboard load).</summary>
    internal static List<DateOnly> WeekdayDueDates(string weekdayName, DateOnly today, int weeksAhead = 3)
    {
        var dates = new List<DateOnly>();
        var d = NextOccurrenceOnOrAfter(today, weekdayName);
        for (var i = 0; i <= weeksAhead; i++)
        {
            dates.Add(d);
            d = d.AddDays(7);
        }
        return dates;
    }

    /// <summary>dayOfMonth: "1".."31", or "end-of-month".</summary>
    internal static DateOnly? MonthlyDueDate(string dayOfMonth, DateOnly today)
    {
        if (dayOfMonth == "end-of-month")
        {
            return new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
        }
        if (!int.TryParse(dayOfMonth, out var day) || day < 1 || day > 31) return null;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        if (day > daysInMonth) return null;
        return new DateOnly(today.Year, today.Month, day);
    }

    private static bool TryParseGigDate(Gig gig, out DateTime date) => DateTime.TryParse(gig.Date, out date);

    private async Task<Account?> GetAccountAsync(Guid bandId, string platformId) =>
        await db.Accounts.FirstOrDefaultAsync(a => a.BandId == bandId && a.PlatformId == platformId);

    public async Task GenerateRecurringItemsAsync(Guid bandId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var rules = await db.CadenceRules
            .Include(r => r.Account).ThenInclude(a => a.Platform)
            .Where(r => r.Account.BandId == bandId && r.Kind == CadenceKind.Recurring && r.Active)
            .ToListAsync();

        foreach (var rule in rules)
        {
            foreach (var day in rule.ScheduleDays ?? [])
            {
                var dueDates = rule.ScheduleType == ScheduleType.Monthly
                    ? (MonthlyDueDate(day, today) is { } d ? [d] : new List<DateOnly>())
                    : WeekdayDueDates(day, today);

                foreach (var dueDate in dueDates)
                {
                    var templateKey = $"cadence-{rule.Id}";
                    var exists = await db.ScheduleItems.AnyAsync(s =>
                        s.BandId == bandId && s.TemplateKey == templateKey && s.DueDate == dueDate && s.GigRef == null);
                    if (exists) continue;

                    db.ScheduleItems.Add(new ScheduleItem
                    {
                        BandId = bandId,
                        AccountId = rule.AccountId,
                        TemplateKey = templateKey,
                        Platform = rule.Account.Platform.DisplayName,
                        Owner = rule.Owner ?? "",
                        ContentType = rule.ContentTypeId,
                        Category = rule.Category,
                        Example = rule.Description,
                        DueDate = dueDate,
                        NoApi = !rule.Account.Platform.SupportsPosting,
                        AutoHandled = false,
                        GigRef = null
                    });
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<string?> FindEventUrlAsync(Guid bandId, string gigRef)
    {
        var artifact = await db.Artifacts
            .Where(a => a.ArtifactType == "event_url" && a.ScheduleItem.BandId == bandId && a.ScheduleItem.GigRef == gigRef)
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefaultAsync();
        return artifact?.TextValue;
    }

    /// <summary>One instance per upcoming gig per active gig_countdown
    /// rule, once per week counting backward from the show date, capped
    /// at GigCountdownMaxWeeksOut so a show booked months out doesn't
    /// immediately queue up 20+ near-identical items at once.</summary>
    public async Task GenerateGigCountdownItemsAsync(Guid bandId, Band band, List<Gig> gigs)
    {
        var rules = await db.CadenceRules
            .Include(r => r.Account).ThenInclude(a => a.Platform)
            .Where(r => r.Account.BandId == bandId && r.Kind == CadenceKind.GigCountdown && r.Active)
            .ToListAsync();
        if (rules.Count == 0) return;

        var today = DateTime.UtcNow.Date;

        foreach (var gig in gigs)
        {
            if (!TryParseGigDate(gig, out var showDate) || showDate <= today) continue;
            var gigRef = SiteContentRef.GigRef(gig);

            foreach (var rule in rules)
            {
                var eventUrl = await FindEventUrlAsync(bandId, gigRef);
                var weeksOut = Math.Min(GigCountdownMaxWeeksOut, (int)Math.Floor((showDate - today).TotalDays / 7));

                for (; weeksOut >= 1; weeksOut--)
                {
                    var dueDate = showDate.AddDays(-7 * weeksOut);
                    if (dueDate < today) continue;

                    var template = (rule.MessageTemplates?.GetValueOrDefault(weeksOut.ToString())
                        ?? rule.MessageTemplates?.GetValueOrDefault("default"))
                        ?? rule.Description;
                    var message = eventUrl is not null
                        ? template.Replace("{event_url}", eventUrl)
                        : System.Text.RegularExpressions.Regex.Replace(template, @"\{event_url\}\s*", "");

                    var templateKey = $"cadence-{rule.Id}-{gigRef}-w{weeksOut}";
                    var dueDateOnly = DateOnly.FromDateTime(dueDate);
                    var exists = await db.ScheduleItems.AnyAsync(s =>
                        s.BandId == bandId && s.TemplateKey == templateKey && s.DueDate == dueDateOnly && s.GigRef == gigRef);
                    if (exists) continue;

                    db.ScheduleItems.Add(new ScheduleItem
                    {
                        BandId = bandId,
                        AccountId = rule.AccountId,
                        TemplateKey = templateKey,
                        Platform = rule.Account.Platform.DisplayName,
                        Owner = rule.Owner ?? "",
                        ContentType = rule.ContentTypeId,
                        Category = rule.Category,
                        Example = $"{message} - {gig.Title}",
                        DueDate = dueDateOnly,
                        NoApi = false,
                        AutoHandled = false,
                        GigRef = gigRef
                    });
                }
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>One instance per gig per active gig_event rule - always
    /// no-api, since no Facebook Event can ever be created via API.</summary>
    public async Task GenerateGigEventItemsAsync(Guid bandId, List<Gig> gigs)
    {
        var rules = await db.CadenceRules
            .Include(r => r.Account).ThenInclude(a => a.Platform)
            .Where(r => r.Account.BandId == bandId && r.Kind == CadenceKind.GigEvent && r.Active)
            .ToListAsync();
        if (rules.Count == 0) return;

        foreach (var gig in gigs)
        {
            var gigRef = SiteContentRef.GigRef(gig);
            foreach (var rule in rules)
            {
                var templateKey = $"cadence-{rule.Id}-{gigRef}";
                var exists = await db.ScheduleItems.AnyAsync(s =>
                    s.BandId == bandId && s.TemplateKey == templateKey && s.DueDate == null && s.GigRef == gigRef);
                if (exists) continue;

                db.ScheduleItems.Add(new ScheduleItem
                {
                    BandId = bandId,
                    AccountId = rule.AccountId,
                    TemplateKey = templateKey,
                    Platform = rule.Account.Platform.DisplayName,
                    Owner = rule.Owner ?? "",
                    ContentType = rule.ContentTypeId,
                    Category = rule.Category,
                    Example = $"{rule.Description} - {gig.Title}",
                    DueDate = null,
                    NoApi = true,
                    AutoHandled = false,
                    GigRef = gigRef
                });
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>One "keep the site listing accurate" tile per upcoming
    /// gig - shares one TemplateKey across every gig (disambiguated by
    /// GigRef, same as the old app relied on).</summary>
    public async Task GenerateWebsiteItemsAsync(Guid bandId, List<Gig> gigs)
    {
        var websiteAccount = await GetAccountAsync(bandId, "website");
        if (websiteAccount is null) return;

        foreach (var gig in gigs)
        {
            var gigRef = SiteContentRef.GigRef(gig);
            const string templateKey = "website-listing";
            var exists = await db.ScheduleItems.AnyAsync(s =>
                s.BandId == bandId && s.TemplateKey == templateKey && s.DueDate == null && s.GigRef == gigRef);
            if (exists) continue;

            db.ScheduleItems.Add(new ScheduleItem
            {
                BandId = bandId,
                AccountId = websiteAccount.Id,
                TemplateKey = templateKey,
                Platform = "Website",
                Owner = "",
                ContentType = "Calendar Listing",
                Category = "Show Details",
                Example = $"Keep the calendar listing accurate - {gig.Title}",
                DueDate = null,
                NoApi = true,
                AutoHandled = false,
                GigRef = gigRef
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task GenerateMediaItemsAsync(Guid bandId, List<MediaItem> mediaList)
    {
        var websiteAccount = await GetAccountAsync(bandId, "website");
        if (websiteAccount is null) return;

        foreach (var media in mediaList)
        {
            var mediaRef = SiteContentRef.MediaRef(media);
            var templateKey = $"media-item-{mediaRef}";
            var exists = await db.ScheduleItems.AnyAsync(s => s.BandId == bandId && s.TemplateKey == templateKey);
            if (exists) continue;

            db.ScheduleItems.Add(new ScheduleItem
            {
                BandId = bandId,
                AccountId = websiteAccount.Id,
                TemplateKey = templateKey,
                Platform = "Website",
                Owner = "",
                ContentType = "Media Item",
                Category = "Media",
                Example = $"Keep the media entry accurate - {media.Title}",
                DueDate = null,
                NoApi = true,
                AutoHandled = false,
                GigRef = null,
                MediaRef = mediaRef
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task GenerateGalleryItemsAsync(Guid bandId, List<GalleryImage> galleryImages)
    {
        var websiteAccount = await GetAccountAsync(bandId, "website");
        if (websiteAccount is null) return;

        foreach (var image in galleryImages)
        {
            var galleryRef = SiteContentRef.GalleryRef(image);
            var templateKey = $"gallery-image-{galleryRef}";
            var exists = await db.ScheduleItems.AnyAsync(s => s.BandId == bandId && s.TemplateKey == templateKey);
            if (exists) continue;

            db.ScheduleItems.Add(new ScheduleItem
            {
                BandId = bandId,
                AccountId = websiteAccount.Id,
                TemplateKey = templateKey,
                Platform = "Website",
                Owner = "",
                ContentType = "Gallery Image",
                Category = "Gallery",
                Example = $"Keep the gallery entry accurate - {image.Alt}",
                DueDate = null,
                NoApi = true,
                AutoHandled = false,
                GigRef = null,
                GalleryRef = galleryRef
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Bandsintown's two gig-driven rows are booking-triggered/
    /// platform-automatic, not something a human tunes a cadence for -
    /// kept as simple hardcoded generation rather than editable rules,
    /// same as the old app.</summary>
    public async Task GenerateBandsintownItemsAsync(Guid bandId, List<Gig> gigs)
    {
        var account = await GetAccountAsync(bandId, "bandsintown");
        if (account is null) return;

        foreach (var gig in gigs)
        {
            var gigRef = SiteContentRef.GigRef(gig);

            const string listingKey = "bandsintown-listing";
            if (!await db.ScheduleItems.AnyAsync(s => s.BandId == bandId && s.TemplateKey == listingKey && s.DueDate == null && s.GigRef == gigRef))
            {
                db.ScheduleItems.Add(new ScheduleItem
                {
                    BandId = bandId, AccountId = account.Id, TemplateKey = listingKey, Platform = "Bandsintown/Songkick",
                    Owner = "Bobby", ContentType = "Event Listing", Category = "Show Promotion",
                    Example = $"New tour date added w/ venue, ticket link - {gig.Title}",
                    DueDate = null, NoApi = true, AutoHandled = false, GigRef = gigRef
                });
            }

            const string reminderKey = "bandsintown-reminder";
            if (!await db.ScheduleItems.AnyAsync(s => s.BandId == bandId && s.TemplateKey == reminderKey && s.DueDate == null && s.GigRef == gigRef))
            {
                db.ScheduleItems.Add(new ScheduleItem
                {
                    BandId = bandId, AccountId = account.Id, TemplateKey = reminderKey, Platform = "Bandsintown/Songkick",
                    Owner = "Bobby", ContentType = "Reminder Push", Category = "Show Promotion",
                    Example = $"Automated fan notification reminder - {gig.Title}",
                    DueDate = null, NoApi = false, AutoHandled = true, GigRef = gigRef
                });
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>One "update the cover photo to match the next show" tile
    /// per active gig_cover_photo rule (Facebook-only, enforced at rule
    /// creation), always for whichever gig is currently soonest. Once a
    /// tile's been generated for a given gig under a given rule it's
    /// never generated again for that gig - the moment a closer gig comes
    /// along, a fresh tile appears automatically for the new next gig.</summary>
    public async Task GenerateCoverPhotoItemsAsync(Guid bandId, Band band, List<Gig> gigs)
    {
        var rules = await db.CadenceRules
            .Include(r => r.Account).ThenInclude(a => a.Platform)
            .Where(r => r.Account.BandId == bandId && r.Kind == CadenceKind.GigCoverPhoto && r.Active)
            .ToListAsync();
        if (rules.Count == 0) return;

        var today = DateTime.UtcNow.Date;
        Gig? nextGig = null;
        DateTime nextGigDate = default;
        foreach (var gig in gigs)
        {
            if (!TryParseGigDate(gig, out var d) || d <= today) continue;
            if (nextGig is null || d < nextGigDate) { nextGig = gig; nextGigDate = d; }
        }
        if (nextGig is null) return;

        var gigRef = SiteContentRef.GigRef(nextGig);

        foreach (var rule in rules)
        {
            var templateKey = $"cadence-{rule.Id}-{gigRef}";
            var item = await db.ScheduleItems.Include(s => s.Artifacts)
                .FirstOrDefaultAsync(s => s.BandId == bandId && s.TemplateKey == templateKey);

            if (item is null)
            {
                item = new ScheduleItem
                {
                    BandId = bandId,
                    AccountId = rule.AccountId,
                    TemplateKey = templateKey,
                    Platform = rule.Account.Platform.DisplayName,
                    Owner = rule.Owner ?? "",
                    ContentType = rule.ContentTypeId,
                    Category = rule.Category,
                    Example = $"{rule.Description} - {nextGig.Title}",
                    DueDate = null,
                    NoApi = false,
                    AutoHandled = false,
                    GigRef = gigRef
                };
                db.ScheduleItems.Add(item);
                await db.SaveChangesAsync();
            }

            // Auto-prepare the cover image from the gig's flyer, once - a
            // no-op if already attached, or if the gig has no flyer yet
            // (a later run picks it up the moment one's added).
            var hasPhoto = item.Artifacts.Any(a => a.ArtifactType == "photo");
            if (hasPhoto || string.IsNullOrEmpty(nextGig.FlyerMain)) continue;

            var cachedPath = await flyerCache.EnsureCachedAsync(band, nextGig.FlyerMain);
            if (cachedPath is null) continue;

            var flyerBytes = await File.ReadAllBytesAsync(cachedPath);
            var coverBytes = ImageTools.MakeFacebookCoverPhoto(flyerBytes);

            var dir = Path.Combine(uploadsRootPath, item.Id.ToString());
            Directory.CreateDirectory(dir);
            var fileName = $"photo-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.jpg";
            await File.WriteAllBytesAsync(Path.Combine(dir, fileName), coverBytes);

            db.Artifacts.Add(new Artifact
            {
                ScheduleItemId = item.Id,
                ArtifactType = "photo",
                FilePath = $"data/uploads/{item.Id}/{fileName}",
                UploadedBy = "auto (next-gig cover photo)"
            });
            await db.SaveChangesAsync();

            try
            {
                await catalogStore.RegisterCatalogItemAsync(bandId, coverBytes, "image/jpeg", fileName,
                    CatalogSource.CoverPhoto, sourceUrl: null, uploadedBy: "auto (next-gig cover photo)");
            }
            catch { /* best-effort, doesn't block generation */ }
        }
    }

    /// <summary>Everything gig/media/gallery-driven for one Band, plus the
    /// "auto-seed the Instagram flyer artifact from the gig's own flyer"
    /// step. Ported from the old app's generateGigDrivenItems + the tail
    /// end of generateAll.</summary>
    public async Task GenerateGigDrivenItemsAsync(Guid bandId, Band band)
    {
        var gigs = await gigsSource.LoadGigsAsync(band);

        await GenerateGigCountdownItemsAsync(bandId, band, gigs);
        await GenerateGigEventItemsAsync(bandId, gigs);
        await GenerateBandsintownItemsAsync(bandId, gigs);
        await GenerateWebsiteItemsAsync(bandId, gigs);
        await GenerateCoverPhotoItemsAsync(bandId, band, gigs);

        // If a gig_countdown item is the Instagram flyer post and the gig
        // already has a flyer, auto-seed that artifact from the local
        // cache so it doesn't ask for a duplicate upload of something
        // already available.
        var flyerCandidates = await db.ScheduleItems
            .Include(s => s.Artifacts)
            .Where(s => s.BandId == bandId && s.ContentType == "Feed Post" && s.GigRef != null)
            .ToListAsync();
        var gigsByRef = gigs.ToDictionary(SiteContentRef.GigRef);

        foreach (var item in flyerCandidates)
        {
            if (item.Artifacts.Any(a => a.ArtifactType == "flyer")) continue;
            if (!gigsByRef.TryGetValue(item.GigRef!, out var gig) || string.IsNullOrEmpty(gig.FlyerMain)) continue;

            var cachedPath = await flyerCache.EnsureCachedAsync(band, gig.FlyerMain);
            if (cachedPath is null) continue;

            var dir = Path.Combine(uploadsRootPath, item.Id.ToString());
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(cachedPath).TrimStart('.');
            var fileName = $"flyer-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{(string.IsNullOrEmpty(ext) ? "jpg" : ext)}";
            File.Copy(cachedPath, Path.Combine(dir, fileName), overwrite: true);

            db.Artifacts.Add(new Artifact
            {
                ScheduleItemId = item.Id,
                ArtifactType = "flyer",
                FilePath = $"data/uploads/{item.Id}/{fileName}",
                UploadedBy = "auto (from calendar)"
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Everything for one Band - recurring cadence, gig-driven
    /// items, and the media/gallery tiles. Ported from the old app's
    /// generateAll(), now looped per-Band by the background service
    /// instead of running once for the single implicit tenant.</summary>
    public async Task GenerateAllAsync(Guid bandId, Band band)
    {
        await GenerateRecurringItemsAsync(bandId);
        await GenerateGigDrivenItemsAsync(bandId, band);

        var mediaList = await mediaSource.LoadMediaItemsAsync(band);
        await GenerateMediaItemsAsync(bandId, mediaList);

        var galleryList = await gallerySource.LoadGalleryImagesAsync(band);
        await GenerateGalleryItemsAsync(bandId, galleryList);
    }
}
