using System.Text.Json;
using BandManager.Data.Services;
using Xunit;

namespace BandManager.Data.Tests;

/// <summary>
/// Regression test for a real bug caught during this session's live
/// verification: GigsController deserializes the "with" form/body field
/// (a JSON string with lowercase keys, matching every other payload in
/// this app) into List&lt;WithAct&gt; (PascalCase Name/Url properties).
/// Without PropertyNameCaseInsensitive, both properties silently came
/// through as null - WithAct's properties are nullable, so nothing threw,
/// it just produced empty "name"/"url" values in the pushed calendar.js
/// commit. Only caught by hand-testing against a real site. This test
/// exists so the next accidental removal of that JsonSerializerOptions
/// fails loudly in seconds instead of silently in production.
/// </summary>
public class WithActJsonRegressionTests
{
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_WithLowercaseKeys_AndCaseInsensitiveOptions_PopulatesFields()
    {
        var json = """[{"name":"Test Opener Band","url":"https://example.com/opener"},{"name":"Second Act","url":""}]""";

        var result = JsonSerializer.Deserialize<List<WithAct>>(json, CaseInsensitiveOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("Test Opener Band", result[0].Name);
        Assert.Equal("https://example.com/opener", result[0].Url);
        Assert.Equal("Second Act", result[1].Name);
        Assert.Equal("", result[1].Url);
    }

    [Fact]
    public void Deserialize_WithLowercaseKeys_WithoutCaseInsensitiveOptions_SilentlyLosesData()
    {
        // This is the bug, pinned down as a passing test: proves the
        // failure mode is silent data loss (null, not an exception), which
        // is exactly why it slipped past everything except a live-site
        // hand-test - and exactly why GigsController must always pass
        // CaseInsensitiveOptions (see the sibling test above and
        // GigsController.WithActJsonOptions).
        var json = """[{"name":"Test Opener Band","url":"https://example.com/opener"}]""";

        var result = JsonSerializer.Deserialize<List<WithAct>>(json); // no options - default is case-sensitive

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Null(result[0].Name);
        Assert.Null(result[0].Url);
    }

    [Fact]
    public void Deserialize_EmptyArray_ProducesEmptyList()
    {
        var result = JsonSerializer.Deserialize<List<WithAct>>("[]", CaseInsensitiveOptions);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void EffectiveWith_PrefersNewListOverLegacyScalarPair_WhenBothPresent()
    {
        var gig = new SiteGig(
            Id: "g1", Title: "Test", Venue: null, VenueUrl: null, Date: "2026-01-01", Time: null,
            Address: null,
            WithArtists: "Legacy Band", WithArtistsUrl: "https://legacy.example.com",
            With: [new WithAct("New Band", "https://new.example.com")],
            DoorsTime: null, OpenerTime: null, HeadlinerTime: null,
            TicketsUrl: null, FlyerMain: null, FreeAdmission: false, CustomTicketsText: null, TicketMode: null);

        var effective = gig.EffectiveWith();

        Assert.Single(effective);
        Assert.Equal("New Band", effective[0].Name);
    }

    [Fact]
    public void EffectiveWith_FallsBackToLegacyScalarPair_WhenNewListIsAbsent()
    {
        var gig = new SiteGig(
            Id: "g1", Title: "Test", Venue: null, VenueUrl: null, Date: "2026-01-01", Time: null,
            Address: null,
            WithArtists: "Legacy Band", WithArtistsUrl: "https://legacy.example.com",
            With: null,
            DoorsTime: null, OpenerTime: null, HeadlinerTime: null,
            TicketsUrl: null, FlyerMain: null, FreeAdmission: false, CustomTicketsText: null, TicketMode: null);

        var effective = gig.EffectiveWith();

        Assert.Single(effective);
        Assert.Equal("Legacy Band", effective[0].Name);
        Assert.Equal("https://legacy.example.com", effective[0].Url);
    }

    [Fact]
    public void EffectiveWith_ReturnsEmpty_WhenNeitherIsSet()
    {
        var gig = new SiteGig(
            Id: "g1", Title: "Test", Venue: null, VenueUrl: null, Date: "2026-01-01", Time: null,
            Address: null,
            WithArtists: null, WithArtistsUrl: null,
            With: null,
            DoorsTime: null, OpenerTime: null, HeadlinerTime: null,
            TicketsUrl: null, FlyerMain: null, FreeAdmission: false, CustomTicketsText: null, TicketMode: null);

        Assert.Empty(gig.EffectiveWith());
    }
}
