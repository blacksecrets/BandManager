using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandManager.Web.Controllers;

/// <summary>
/// USPS address standardization for the Profile page's address section -
/// any logged-in user, not band-scoped (an address is personal, not a
/// band concept). Returns null/"not configured" gracefully rather than
/// an error when no SuperAdmin has set up USPS credentials yet - the
/// address fields still save fine as plain typed text either way.
/// </summary>
[ApiController]
[Route("/api/address-lookup")]
[Authorize]
public class AddressLookupController(AddressLookupService addressLookup, ApplicationDbContext db, ICredentialCipher cipher) : ControllerBase
{
    // Lets the Profile page show/hide its "not set up yet" note upfront,
    // same shape as ExternalCalendarController.AvailableProviders.
    [HttpGet("configured")]
    public async Task<IActionResult> Configured()
    {
        var creds = await SongSearchService.GetCredentialAsync(db, cipher, AddressLookupService.CredentialKey);
        return Ok(new { configured = creds is not null });
    }

    [HttpGet]
    public async Task<IActionResult> Validate(
        [FromQuery] string streetAddress, [FromQuery] string? secondaryAddress,
        [FromQuery] string? city, [FromQuery] string? state, [FromQuery] string? zipCode)
    {
        if (string.IsNullOrWhiteSpace(streetAddress)) return BadRequest(new { error = "Street address is required." });

        var result = await addressLookup.ValidateAsync(streetAddress, secondaryAddress, city, state, zipCode);
        return Ok(new { match = result });
    }
}
