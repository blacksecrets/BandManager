using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data.Seed;

/// <summary>
/// Applies the global reference-data seed (Platforms, ContentTypes,
/// PlatformContentTypes) on every startup - upserts, not insert-only, so
/// an edit to the seed data (new instructions text, a platform gaining
/// credential_fields) takes effect on the next restart instead of being
/// stuck at whatever was first seeded. Mirrors the old app's
/// seedPlatforms.js/seedCadence.js "upserts every run" comment - this is
/// static reference data, never hand-edited by a user anywhere.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        foreach (var platform in PlatformSeedData.All)
        {
            var existing = await db.Platforms.FindAsync(platform.Id);
            if (existing is null)
            {
                db.Platforms.Add(platform);
            }
            else
            {
                existing.DisplayName = platform.DisplayName;
                existing.SupportsPosting = platform.SupportsPosting;
                existing.CredentialFields = platform.CredentialFields;
                existing.SetupInstructions = platform.SetupInstructions;
                existing.SortOrder = platform.SortOrder;
            }
        }

        foreach (var contentType in ContentTypeSeedData.All)
        {
            var existing = await db.ContentTypes.FindAsync(contentType.Id);
            if (existing is null)
            {
                db.ContentTypes.Add(contentType);
            }
            else
            {
                existing.RequiredArtifacts = contentType.RequiredArtifacts;
                existing.SortOrder = contentType.SortOrder;
            }
        }

        await db.SaveChangesAsync();

        var existingPairs = await db.PlatformContentTypes
            .Select(x => new { x.PlatformId, x.ContentTypeId })
            .ToListAsync();
        var existingPairSet = existingPairs.Select(x => (x.PlatformId, x.ContentTypeId)).ToHashSet();

        foreach (var (platformId, contentTypeIds) in ContentTypeSeedData.PlatformContentTypes)
        {
            foreach (var contentTypeId in contentTypeIds)
            {
                if (existingPairSet.Contains((platformId, contentTypeId))) continue;
                db.PlatformContentTypes.Add(new PlatformContentType { PlatformId = platformId, ContentTypeId = contentTypeId });
            }
        }

        await db.SaveChangesAsync();
    }
}
