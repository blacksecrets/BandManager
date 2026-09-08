using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data.Services;

/// <summary>
/// Closes the gap between Catalog's real video posting (YouTubeController/
/// TikTokController - actual API uploads) and Cadence's own task tiles for
/// those same platforms. YouTube/TikTok have no posting API wired into
/// ScheduleItemsController.Fart (PlatformSeedData marks both
/// SupportsPosting=false, so their tiles are NoApi "mark done yourself"
/// reminders) even though a real upload path exists elsewhere in this app -
/// so previously, posting a video from Catalog never touched the matching
/// Cadence tile at all. This service is the fix: called right after a real
/// upload succeeds, it finds the oldest still-open tile for that platform/
/// content type and marks it posted, the same way Fart's own success path
/// does (PostedAt/PostedVia set, Status deliberately left Open - see
/// ScheduleItemsController.Fart's doc comment on why).
///
/// Best-effort and silent: these video tiles are Recurring-kind (no
/// GigRef - see ContentTypeSeedData/Scheduler.cs), so there's no gig to
/// match against, only "the next one due" for this Account. If there's no
/// Account for this platform yet, or no matching open tile, this does
/// nothing - the real post already succeeded regardless, so a missed link
/// here is never worth failing the request over.
/// </summary>
public class CadenceAutoLinkService(ApplicationDbContext db)
{
    public async Task<ScheduleItem?> AutoCompleteVideoTileAsync(Guid bandId, string platformId, IReadOnlyList<string> candidateContentTypes)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.BandId == bandId && a.PlatformId == platformId);
        if (account is null) return null;

        var item = await db.ScheduleItems
            .Where(i => i.AccountId == account.Id && i.Status == ScheduleItemStatus.Open
                && i.PostedAt == null && candidateContentTypes.Contains(i.ContentType))
            .OrderBy(i => i.DueDate)
            .FirstOrDefaultAsync();
        if (item is null) return null;

        item.PostedAt = DateTime.UtcNow;
        item.PostedVia = "catalog-auto-detected";
        await db.SaveChangesAsync();
        return item;
    }
}
