using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BandManager.Web.Services;

public record AddressCandidate(string StreetAddress, string? SecondaryAddress, string City, string State, string ZipCode, string? ZipPlus4);

/// <summary>
/// USPS Addresses API v3 (apis.usps.com) - OAuth2 client-credentials, same
/// shape as SongSearchService's Spotify integration (credential storage,
/// token caching with a lock, swallow-and-return-null on any failure).
/// Requires a SuperAdmin-registered USPS Web Tools/API account (free) -
/// see SuperAdminController's address-lookup-credentials endpoints.
/// Standardizes/corrects a US address rather than doing as-you-type
/// autocomplete (USPS's API validates one specific address at a time,
/// it doesn't do partial-text suggestions).
/// </summary>
public class AddressLookupService(IHttpClientFactory httpClientFactory, BandManager.Data.ApplicationDbContext db, BandManager.Data.Crypto.ICredentialCipher cipher)
{
    public const string CredentialKey = "usps_credentials";

    private static string? _cachedToken;
    private static DateTime _cachedTokenExpiresAt;
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private async Task<string?> GetTokenAsync()
    {
        var creds = await SongSearchService.GetCredentialAsync(db, cipher, CredentialKey);
        if (creds is null || !creds.TryGetValue("clientId", out var clientId) || !creds.TryGetValue("clientSecret", out var clientSecret)
            || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        if (_cachedToken is not null && DateTime.UtcNow < _cachedTokenExpiresAt) return _cachedToken;

        await TokenLock.WaitAsync();
        try
        {
            if (_cachedToken is not null && DateTime.UtcNow < _cachedTokenExpiresAt) return _cachedToken;

            var client = httpClientFactory.CreateClient();
            var payload = JsonSerializer.Serialize(new { client_id = clientId, client_secret = clientSecret, grant_type = "client_credentials" });
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://apis.usps.com/oauth2/v3/token")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            var res = await client.SendAsync(request);
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            var token = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
            _cachedToken = token;
            _cachedTokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);
            return token;
        }
        catch
        {
            return null;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    /// <summary>Standardizes/validates one US address. Returns the single
    /// USPS-standardized candidate, or null if unconfigured, the lookup
    /// failed, or USPS couldn't match anything - the caller always falls
    /// back to saving whatever the user actually typed.</summary>
    public async Task<AddressCandidate?> ValidateAsync(string streetAddress, string? secondaryAddress, string? city, string? state, string? zipCode)
    {
        var token = await GetTokenAsync();
        if (token is null) return null;

        try
        {
            var client = httpClientFactory.CreateClient();
            var query = new List<string> { $"streetAddress={Uri.EscapeDataString(streetAddress)}" };
            if (!string.IsNullOrWhiteSpace(secondaryAddress)) query.Add($"secondaryAddress={Uri.EscapeDataString(secondaryAddress)}");
            if (!string.IsNullOrWhiteSpace(city)) query.Add($"city={Uri.EscapeDataString(city)}");
            if (!string.IsNullOrWhiteSpace(state)) query.Add($"state={Uri.EscapeDataString(state)}");
            if (!string.IsNullOrWhiteSpace(zipCode)) query.Add($"ZIPCode={Uri.EscapeDataString(zipCode)}");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://apis.usps.com/addresses/v3/address?{string.Join("&", query)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await client.SendAsync(request);
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            if (!doc.RootElement.TryGetProperty("address", out var addr)) return null;

            string? Get(string name) => addr.TryGetProperty(name, out var v) ? v.GetString() : null;
            var street = Get("streetAddress");
            var cityOut = Get("city");
            var stateOut = Get("state");
            var zip = Get("ZIPCode");
            if (street is null || cityOut is null || stateOut is null || zip is null) return null;

            return new AddressCandidate(street, Get("secondaryAddress"), cityOut, stateOut, zip, Get("ZIPPlus4"));
        }
        catch
        {
            return null;
        }
    }
}
