using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

/// <summary>
/// Global reference data (identical for every Band) - field names are
/// deliberately snake_case to match what the reused wwwroot/assets/settings.js
/// and cadence.js (from the old app) already expect (platform.display_name,
/// platform.credential_fields, platform.account_id, etc.), rather than
/// ASP.NET's camelCase default, so those files need no change to read this.
///
/// account_id is band-contextualized (not itself global) - the old app
/// auto-created one Account "slot" per platform for every install
/// (seedPlatformsAndAccounts' insertAccount), so cadence.js has always
/// assumed platform.account_id exists even before that platform's
/// credentials are configured. Same behavior here, just band-scoped: an
/// empty (no credentials) Account is auto-provisioned per platform for
/// the active Band on first request, so creating a cadence rule never
/// has to wait on Setup being finished first.
/// </summary>
[ApiController]
[Route("/api/platforms")]
[Authorize(Policy = "BandMember")]
public class PlatformsController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var platforms = await db.Platforms.OrderBy(p => p.SortOrder).ToListAsync();

        Dictionary<string, Guid> accountIdByPlatform = [];
        if (activeBand.GetActiveBandId() is { } bandId)
        {
            var accounts = await db.Accounts.Where(a => a.BandId == bandId).ToListAsync();
            accountIdByPlatform = accounts.ToDictionary(a => a.PlatformId, a => a.Id);

            var missing = platforms.Where(p => !accountIdByPlatform.ContainsKey(p.Id)).ToList();
            if (missing.Count > 0)
            {
                foreach (var p in missing)
                {
                    var account = new Account { BandId = bandId, PlatformId = p.Id, Label = p.DisplayName };
                    db.Accounts.Add(account);
                    accountIdByPlatform[p.Id] = account.Id;
                }
                await db.SaveChangesAsync();
            }
        }

        return Ok(platforms.Select(p => new
        {
            id = p.Id,
            display_name = p.DisplayName,
            supports_posting = p.SupportsPosting,
            credential_fields = p.CredentialFields,
            setup_instructions = p.SetupInstructions,
            sort_order = p.SortOrder,
            account_id = accountIdByPlatform.GetValueOrDefault(p.Id)
        }));
    }
}
