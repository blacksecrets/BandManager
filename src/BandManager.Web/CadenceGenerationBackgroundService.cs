using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Data.Services;
using BandManager.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web;

/// <summary>
/// Ported from the old app's server.js (generateAll() once at startup,
/// then setInterval every 30 minutes) - now looped across every Band
/// instead of running once for the single implicit tenant.
/// </summary>
public class CadenceGenerationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<CadenceGenerationBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await GenerateForAllBandsAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task GenerateForAllBandsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<Scheduler>();
        var reminders = scope.ServiceProvider.GetRequiredService<NotificationReminderService>();

        var bands = await db.Bands.Where(b => b.IsOnboarded).ToListAsync(ct);
        foreach (var band in bands)
        {
            try
            {
                await scheduler.GenerateAllAsync(band.Id, band);
            }
            catch (Exception ex)
            {
                // One Band's generation failing (e.g. a malformed cadence
                // rule, or its site being unreachable) shouldn't block
                // every other Band's - log and move on.
                logger.LogError(ex, "Cadence generation failed for band {BandId}", band.Id);
            }

            try
            {
                await reminders.GenerateDueRemindersAsync(band.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder generation failed for band {BandId}", band.Id);
            }

            try
            {
                await ReactivateDueVenueCampaignsAsync(db, band.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Venue campaign retry-date reactivation failed for band {BandId}", band.Id);
            }
        }
    }

    // A rejected VenueCampaign with a RetryDate (not NeverRetry) reopens
    // itself once that date arrives - same reset Start-over does
    // (VenueCampaignsController.Start), just triggered by time instead of
    // a click. NeverRetry campaigns are never touched here.
    private static async Task ReactivateDueVenueCampaignsAsync(ApplicationDbContext db, Guid bandId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var due = await db.VenueCampaigns
            .Where(c => c.BandId == bandId && c.Status == VenueCampaignStatus.CompleteRejected
                && !c.NeverRetry && c.RetryDate != null && c.RetryDate <= today)
            .ToListAsync();
        if (due.Count == 0) return;

        foreach (var campaign in due)
        {
            campaign.Status = VenueCampaignStatus.Active;
            campaign.CurrentStepNumber = 1;
            campaign.StartedAt = DateTime.UtcNow;
            campaign.LastCommunicationAt = null;
            campaign.RejectionReason = null;
            campaign.RetryDate = null;
        }
        await db.SaveChangesAsync();
    }
}
