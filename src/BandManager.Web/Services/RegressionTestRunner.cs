using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Services;

public record RegressionCheckResult(string Name, bool Passed, string Message, long DurationMs);

/// <summary>
/// SuperAdmin's "Run Regression Tests" button - true black-box checks:
/// logs in as a dedicated, fixed regression-test account over real HTTP
/// (self-referential call to this same running app, since the app always
/// listens on :8080 inside its own container - see docker-compose.yml)
/// and drives the exact same API endpoints a browser would, rather than
/// re-implementing the app's logic here. Everything runs against one
/// fixed, dedicated "Regression Test Band" - never a real tenant - which
/// is wiped and rebuilt at the start of every run so re-running never
/// accumulates junk data. Deliberately never exercises a live-site-publish
/// checkbox path with an actual site connected (this band always has none
/// configured, so those code paths correctly no-op even if a check did
/// flip one) - this suite verifies app behavior, not the real bands' live
/// sites. This is a regression net, not a substitute for a human clicking
/// through the app - see the manual Test Suite tab for that.
/// </summary>
public class RegressionTestRunner(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
{
    private static readonly Guid FixtureBandId = Guid.Parse("00000000-0000-0000-0000-0000000baed1");
    private static readonly Guid FixtureUserId = Guid.Parse("00000000-0000-0000-0000-00000005ee57");
    private const string FixtureUsername = "regression-tests@bandmanager.internal";
    private const string FixturePassword = "Reg-Test-P@ssw0rd-Fixed";
    private const string BaseUrl = "http://localhost:8080";

    private async Task<(Band Band, ApplicationUser User)> EnsureFixtureAsync()
    {
        // Wipe this band's own data (never touches any other band) so
        // every run starts from a known-empty state.
        var oldGigIds = await db.Gigs.Where(g => g.BandId == FixtureBandId).Select(g => g.Id).ToListAsync();
        await db.GigPayoutRecipients.Where(r => oldGigIds.Contains(r.GigId)).ExecuteDeleteAsync();
        await db.GigPayouts.Where(p => oldGigIds.Contains(p.GigId)).ExecuteDeleteAsync();
        await db.Trips.Where(t => t.BandId == FixtureBandId).ExecuteDeleteAsync();
        await db.BandLocations.Where(l => l.BandId == FixtureBandId).ExecuteDeleteAsync();
        await db.Gigs.Where(g => g.BandId == FixtureBandId).ExecuteDeleteAsync();
        await db.Venues.Where(v => v.BandId == FixtureBandId).ExecuteDeleteAsync();
        await db.PayoutRecipients.Where(p => p.BandId == FixtureBandId).ExecuteDeleteAsync();

        var band = await db.Bands.FindAsync(FixtureBandId);
        if (band is null)
        {
            band = new Band { Id = FixtureBandId, Name = "Regression Test Band", Slug = "regression-test-band", IsOnboarded = true };
            db.Bands.Add(band);
        }

        var user = await userManager.FindByIdAsync(FixtureUserId.ToString());
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = FixtureUserId,
                UserName = FixtureUsername,
                Email = FixtureUsername,
                EmailConfirmed = true,
                MustChangePassword = false,
                IsSuperAdmin = true,
                // Pre-filled so Travel's home-address-snapshot checks don't
                // have to also exercise the "complete your address" modal
                // flow - that's covered separately by a manual test case.
                AddressLine1 = "1 Regression Test Way",
                City = "Testville",
                State = "VA",
                PostalCode = "20000"
            };
            await userManager.CreateAsync(user, FixturePassword);
        }

        await db.SaveChangesAsync();

        if (!await db.BandMemberships.AnyAsync(m => m.BandId == FixtureBandId && m.UserId == FixtureUserId))
        {
            db.BandMemberships.Add(new BandMembership { BandId = FixtureBandId, UserId = FixtureUserId, Role = BandRole.BandAdmin });
            await db.SaveChangesAsync();
        }

        return (band, user);
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(text); } catch { return null; }
    }

    public async Task<List<RegressionCheckResult>> RunAllAsync()
    {
        var results = new List<RegressionCheckResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        async Task Check(string name, Func<Task<string>> body)
        {
            sw.Restart();
            try
            {
                var message = await body();
                results.Add(new RegressionCheckResult(name, true, message, sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                results.Add(new RegressionCheckResult(name, false, ex.Message, sw.ElapsedMilliseconds));
            }
        }

        try
        {
            await Check("Set up fixture band and user", async () =>
            {
                await EnsureFixtureAsync();
                return "Regression Test Band and fixture account are ready.";
            });
        }
        catch (Exception ex)
        {
            results.Add(new RegressionCheckResult("Set up fixture band and user", false, ex.Message, 0));
            return results; // nothing else can run without the fixture
        }

        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        var loggedIn = false;
        await Check("Log in as the regression-test account", async () =>
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = FixtureUsername, ["Password"] = FixturePassword });
            var res = await http.PostAsync("/login", form);
            if (res.StatusCode != HttpStatusCode.Redirect && res.StatusCode != HttpStatusCode.Found && !res.IsSuccessStatusCode)
                throw new Exception($"Login returned unexpected status {(int)res.StatusCode}.");
            var meRes = await http.GetAsync("/api/profile/me");
            var me = await ReadJsonAsync(meRes);
            if (meRes.IsSuccessStatusCode && me is { } m && m.TryGetProperty("username", out var uname) && uname.GetString() == FixtureUsername)
            {
                loggedIn = true;
                return "Session cookie established; /api/profile/me confirms identity.";
            }
            throw new Exception("Logged in but /api/profile/me didn't reflect the expected account.");
        });

        if (!loggedIn)
            return results; // every check below needs an authenticated session

        await Check("Switch active band to Regression Test Band", async () =>
        {
            var res = await http.PostAsync("/api/bands/active",
                new StringContent(JsonSerializer.Serialize(new { bandId = FixtureBandId }), Encoding.UTF8, "application/json"));
            if (!res.IsSuccessStatusCode) throw new Exception($"POST /api/bands/active returned {(int)res.StatusCode}.");

            var meRes = await http.GetAsync("/api/profile/me");
            var me = await ReadJsonAsync(meRes);
            var activeName = me?.GetProperty("activeBandName").GetString();
            if (activeName != "Regression Test Band") throw new Exception($"Active band shows as '{activeName}', expected 'Regression Test Band'.");
            return "Active band correctly switched.";
        });

        Guid? venueId = null;
        await Check("Create a venue", async () =>
        {
            var payload = new { name = "Regression Test Venue", addressLine1 = "500 Test St", city = "Testville", state = "VA", postalCode = "20000" };
            var res = await http.PostAsync("/api/venues", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!res.IsSuccessStatusCode) throw new Exception($"POST /api/venues returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
            var body = await ReadJsonAsync(res);
            venueId = body?.GetProperty("id").GetGuid();
            return $"Venue created (id {venueId}).";
        });

        string? gigRef = null;
        await Check("Create a gig", async () =>
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent("Regression Test Gig"), "title" },
                { new StringContent("Regression Test Venue"), "venue" },
                { new StringContent("500 Test St, Testville, VA 20000"), "address" },
                { new StringContent(DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")), "date" },
            };
            if (venueId is { } vid) form.Add(new StringContent(vid.ToString()), "venueId");
            var res = await http.PostAsync("/api/gigs", form);
            if (!res.IsSuccessStatusCode) throw new Exception($"POST /api/gigs returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
            var body = await ReadJsonAsync(res);
            gigRef = body?.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(gigRef)) throw new Exception("Response had no gig ref.");
            return $"Gig created (ref {gigRef}).";
        });

        await Check("New gig appears in the gig list", async () =>
        {
            var res = await http.GetAsync("/api/gig-sets/gigs");
            var body = await ReadJsonAsync(res);
            var found = body?.EnumerateArray().Any(g => g.GetProperty("gigRef").GetString() == gigRef) ?? false;
            if (!found) throw new Exception("Gig not found in /api/gig-sets/gigs.");
            return "Gig is listed.";
        });

        await Check("Archive the gig (no site checkbox) and confirm it's archived", async () =>
        {
            var res = await http.PostAsync($"/api/gigs/{gigRef}/archive",
                new StringContent(JsonSerializer.Serialize(new { removeFromWebsiteCalendar = false }), Encoding.UTF8, "application/json"));
            if (!res.IsSuccessStatusCode) throw new Exception($"Archive returned {(int)res.StatusCode}.");

            var archivedRes = await http.GetAsync("/api/gig-sets/gigs/archived");
            var archived = await ReadJsonAsync(archivedRes);
            var found = archived?.EnumerateArray().Any(g => g.GetProperty("gigRef").GetString() == gigRef) ?? false;
            if (!found) throw new Exception("Gig not found in the archived list after archiving.");
            return "Gig archived and correctly listed as archived.";
        });

        await Check("Unarchive the gig (no site checkbox) and confirm it's restored", async () =>
        {
            var res = await http.PostAsync($"/api/gigs/{gigRef}/unarchive",
                new StringContent(JsonSerializer.Serialize(new { addToWebsiteCalendar = false }), Encoding.UTF8, "application/json"));
            if (!res.IsSuccessStatusCode) throw new Exception($"Unarchive returned {(int)res.StatusCode}.");

            var listRes = await http.GetAsync("/api/gig-sets/gigs");
            var list = await ReadJsonAsync(listRes);
            var found = list?.EnumerateArray().Any(g => g.GetProperty("gigRef").GetString() == gigRef) ?? false;
            if (!found) throw new Exception("Gig not found back in the active gig list after unarchiving.");
            return "Gig unarchived and correctly restored.";
        });

        await Check("Set payout roster and a valid 100% split", async () =>
        {
            var rosterRes = await http.PutAsync("/api/accounting/receivables/roster",
                new StringContent(JsonSerializer.Serialize(new { userIds = new[] { FixtureUserId } }), Encoding.UTF8, "application/json"));
            if (!rosterRes.IsSuccessStatusCode) throw new Exception($"Set roster returned {(int)rosterRes.StatusCode}.");

            var pctRes = await http.PutAsync("/api/accounting/receivables/percentages",
                new StringContent(JsonSerializer.Serialize(new { percentages = new[] { new { userId = FixtureUserId, percentage = 100m } } }), Encoding.UTF8, "application/json"));
            if (!pctRes.IsSuccessStatusCode) throw new Exception($"Set percentages (100%) returned {(int)pctRes.StatusCode}, expected success.");
            return "Roster and a valid 100% split both saved.";
        });

        await Check("Reject a payout split that doesn't total 100%", async () =>
        {
            var res = await http.PutAsync("/api/accounting/receivables/percentages",
                new StringContent(JsonSerializer.Serialize(new { percentages = new[] { new { userId = FixtureUserId, percentage = 50m } } }), Encoding.UTF8, "application/json"));
            if (res.IsSuccessStatusCode) throw new Exception("A 50% split was accepted - it should have been rejected for not summing to 100%.");
            return $"Correctly rejected with status {(int)res.StatusCode}.";
        });

        await Check("Save vehicle info and read it back", async () =>
        {
            var res = await http.PutAsync("/api/travel/vehicle",
                new StringContent(JsonSerializer.Serialize(new { vehicleMake = "Regression", vehicleModel = "Runner", vehicleYear = 2024, startingMileage = 1000 }), Encoding.UTF8, "application/json"));
            if (!res.IsSuccessStatusCode) throw new Exception($"Set vehicle returned {(int)res.StatusCode}.");

            var getRes = await http.GetAsync("/api/travel/profile");
            var body = await ReadJsonAsync(getRes);
            var make = body?.GetProperty("vehicleMake").GetString();
            if (make != "Regression") throw new Exception($"Vehicle make read back as '{make}', expected 'Regression'.");
            return "Vehicle saved and confirmed on read-back.";
        });

        await Check("Create a trip with two manual addresses", async () =>
        {
            var payload = new
            {
                date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                from = new { isHome = false, name = "Regression From", addressLine1 = "1 From St", city = "Testville", state = "VA", postalCode = "20000" },
                to = new { isHome = false, name = "Regression To", addressLine1 = "2 To St", city = "Testville", state = "VA", postalCode = "20001" },
                roundTrip = true,
                reason = "Rehearsal",
                otherReasonText = (string?)null,
                gigRef = (string?)null
            };
            var res = await http.PostAsync("/api/travel/trips", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!res.IsSuccessStatusCode) throw new Exception($"POST /api/travel/trips returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");

            var listRes = await http.GetAsync("/api/travel/trips");
            var list = await ReadJsonAsync(listRes);
            var count = list?.GetArrayLength() ?? 0;
            if (count < 1) throw new Exception("Trip didn't appear in the trips list after saving.");
            return "Trip saved and appears in the list (distance may be blank if no Google Maps key is configured - that's expected, not a failure).";
        });

        await Check("Notifications unread-count endpoint responds", async () =>
        {
            var res = await http.GetAsync("/api/notifications/unread-count");
            if (!res.IsSuccessStatusCode) throw new Exception($"Returned {(int)res.StatusCode}.");
            var body = await ReadJsonAsync(res);
            if (body is null || !body.Value.TryGetProperty("count", out _)) throw new Exception("Response had no 'count' field.");
            return "Responded with a well-formed unread count.";
        });

        return results;
    }
}
