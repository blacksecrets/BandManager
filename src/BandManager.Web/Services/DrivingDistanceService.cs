using System.Text.Json;

namespace BandManager.Web.Services;

/// <summary>
/// Best-effort one-way driving distance between two addresses, for the
/// Travel expense tracker's Trip grid - same shape as AddressLookupService
/// (SuperAdmin-registered credential, encrypted at rest, swallow-and-
/// return-null on any failure/unconfigured state, caller always saves the
/// trip either way rather than blocking on this). Backed by Google's
/// Distance Matrix API (a single API key, free-tier eligible for the
/// volume a mileage log like this generates) - chosen over a full
/// geocoding+routing build-out since it's a single call that already
/// returns driving miles directly.
/// </summary>
public class DrivingDistanceService(IHttpClientFactory httpClientFactory, BandManager.Data.ApplicationDbContext db, BandManager.Data.Crypto.ICredentialCipher cipher)
{
    public const string CredentialKey = "google_maps_credentials";

    /// <summary>One-way driving miles from origin to destination, or null
    /// if no API key is configured, the addresses couldn't be matched, or
    /// the request failed for any reason.</summary>
    public async Task<double?> ComputeMilesAsync(string origin, string destination)
    {
        var creds = await SongSearchService.GetCredentialAsync(db, cipher, CredentialKey);
        if (creds is null || !creds.TryGetValue("apiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            var client = httpClientFactory.CreateClient();
            var url = $"https://maps.googleapis.com/maps/api/distancematrix/json" +
                      $"?units=imperial&origins={Uri.EscapeDataString(origin)}&destinations={Uri.EscapeDataString(destination)}&key={Uri.EscapeDataString(apiKey)}";
            var res = await client.GetAsync(url);
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "OK") return null;

            var element = root.GetProperty("rows")[0].GetProperty("elements")[0];
            if (element.GetProperty("status").GetString() != "OK") return null;

            var meters = element.GetProperty("distance").GetProperty("value").GetDouble();
            return Math.Round(meters / 1609.344, 1);
        }
        catch
        {
            return null;
        }
    }
}
