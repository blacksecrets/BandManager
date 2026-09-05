using BandManager.Data;
using BandManager.Data.Services;
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

        var bands = await db.Bands.ToListAsync(ct);
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
        }
    }
}
