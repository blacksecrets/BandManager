using BandManager.Data.Services;
using Xunit;

namespace BandManager.Data.Tests;

/// <summary>
/// SiteTextEditing is the highest-risk code in the app: it hand-edits a
/// live band's public site repo (calendar.js/media.js/gallery.js) via
/// quote/brace-aware string splicing rather than parse+reserialize, so a
/// subtle bug here can corrupt a real site's file for every gig, not just
/// the one being edited. These tests exist because that risk is real, not
/// theoretical - see WithActJsonRegressionTests for a bug in this same
/// feature area that was only caught by hand-testing against a live site.
/// </summary>
public class SiteTextEditingTests
{
    private const string SampleGigBlock = """
            {
                id: "blue-fox-2026-10-05",
                date: "Friday, October 5, 2026",
                title: "Live at Blue Fox",
                venue: "Blue Fox Billiards Bar & Grill",
                address: "123 Main St"
            }
        """;

    private const string SampleArrayFile = """
        // calendar.js
        const gigs = [
            {
                id: "gig-1",
                title: "First Gig"
            },
            {
                id: "gig-2",
                title: "Second Gig"
            }
        ];
        """;

    // --- FindBlock ---

    [Fact]
    public void FindBlock_LocatesEntryById()
    {
        var block = SiteTextEditing.FindBlock(SampleArrayFile, "id", "gig-2");

        Assert.NotNull(block);
        Assert.Contains("\"Second Gig\"", block!.Text);
        Assert.DoesNotContain("First Gig", block.Text);
    }

    [Fact]
    public void FindBlock_ReturnsNull_WhenFieldValueNotFound()
    {
        var block = SiteTextEditing.FindBlock(SampleArrayFile, "id", "does-not-exist");
        Assert.Null(block);
    }

    [Fact]
    public void FindBlock_DoesNotMiscountBraceInsideStringValue()
    {
        // A title containing literal { } characters must never be mistaken
        // for the block's own opening/closing brace.
        var code = """
            {
                id: "weird-1",
                title: "A {Band} Plays Live",
                venue: "Test Hall"
            }
            """;
        var block = SiteTextEditing.FindBlock(code, "id", "weird-1");

        Assert.NotNull(block);
        Assert.Contains("venue: \"Test Hall\"", block!.Text);
        Assert.Equal(code, block.Text);
    }

    // --- FindArrayBounds ---

    [Fact]
    public void FindArrayBounds_FindsTopLevelArray()
    {
        var bounds = SiteTextEditing.FindArrayBounds(SampleArrayFile, "gigs");

        Assert.NotNull(bounds);
        var arrayText = SampleArrayFile[bounds!.Value.Start..bounds.Value.End];
        Assert.StartsWith("[", arrayText);
        Assert.EndsWith("]", arrayText);
        Assert.Contains("gig-1", arrayText);
        Assert.Contains("gig-2", arrayText);
    }

    [Fact]
    public void FindArrayBounds_ReturnsNull_WhenArrayNameNotFound()
    {
        Assert.Null(SiteTextEditing.FindArrayBounds(SampleArrayFile, "notARealArray"));
    }

    // --- SetField ---

    [Fact]
    public void SetField_ReplacesExistingQuotedValue()
    {
        var result = SiteTextEditing.SetField(SampleGigBlock, "venue", "New Venue Name");

        Assert.Contains("venue: \"New Venue Name\"", result);
        Assert.DoesNotContain("Blue Fox Billiards", result);
    }

    [Fact]
    public void SetField_NeverAddsAMissingField()
    {
        var result = SiteTextEditing.SetField(SampleGigBlock, "ticketsUrl", "https://tickets.example.com");

        // Unchanged - SetField's whole contract is "replace only", per its
        // own doc comment ("never adds a field that isn't there").
        Assert.Equal(SampleGigBlock, result);
        Assert.DoesNotContain("ticketsUrl", result);
    }

    [Fact]
    public void SetField_EscapesQuotesAndBackslashesInNewValue()
    {
        var result = SiteTextEditing.SetField(SampleGigBlock, "title", "The \"Big\" Show \\ Tour");

        Assert.Contains("title: \"The \\\"Big\\\" Show \\\\ Tour\"", result);
    }

    // --- HasField ---

    [Theory]
    [InlineData("venue", true)]
    [InlineData("ticketsUrl", false)]
    public void HasField_DetectsPresenceCorrectly(string field, bool expected)
    {
        Assert.Equal(expected, SiteTextEditing.HasField(SampleGigBlock, field));
    }

    // --- AppendField ---

    [Fact]
    public void AppendField_AddsNewFieldBeforeClosingBrace_WithLeadingComma()
    {
        var result = SiteTextEditing.AppendField(SampleGigBlock, "ticketsUrl", "\"https://tickets.example.com\"");

        Assert.Contains("ticketsUrl: \"https://tickets.example.com\"", result);
        // Must still be a syntactically single, balanced object literal.
        Assert.Equal(1, CountOccurrences(result, "{"));
        Assert.Equal(1, CountOccurrences(result, "}"));
    }

    [Fact]
    public void AppendField_IsShapeAgnostic_CanAppendARawArrayLiteral()
    {
        // AppendField takes pre-formatted raw JS text - this is what makes
        // it usable for a repeatable With-acts array, unlike SetField
        // which is hard-limited to a single quoted scalar.
        var raw = "[{ name: \"Opener\", url: \"\" }]";
        var result = SiteTextEditing.AppendField(SampleGigBlock, "with", raw);

        Assert.Contains("with: [{ name: \"Opener\", url: \"\" }]", result);
    }

    // --- FindFieldArrayBounds / SetArrayField (new this session - the riskiest addition) ---

    [Fact]
    public void SetArrayField_NoOps_WhenFieldDoesNotExistYet()
    {
        // Confirms the "never guess at structure that isn't there" contract:
        // a legacy gig with no `with` array must not be silently mutated by
        // SetArrayField - the caller (GigsSiteEditor) is responsible for
        // falling back to AppendField in this case.
        var result = SiteTextEditing.SetArrayField(SampleGigBlock, "with", "[{ name: \"X\", url: \"\" }]");

        Assert.Equal(SampleGigBlock, result);
    }

    [Fact]
    public void SetArrayField_NoOps_WhenFieldIsStillALegacyScalar()
    {
        var legacyBlock = """
            {
                id: "legacy-1",
                withArtists: "Old Band",
                withArtistsUrl: "https://old.example.com"
            }
            """;

        // "withArtists" is a scalar string here, not an array - SetArrayField
        // must not corrupt it by trying to splice array bounds into a
        // quoted-string value.
        var result = SiteTextEditing.SetArrayField(legacyBlock, "withArtists", "[{ name: \"New\", url: \"\" }]");

        Assert.Equal(legacyBlock, result);
    }

    [Fact]
    public void SetArrayField_ReplacesExistingArrayInPlace_ExactlyOnce()
    {
        var blockWithArray = """
            {
                id: "gig-1",
                with: [{ name: "Old Opener", url: "" }],
                venue: "Test Hall"
            }
            """;

        var result = SiteTextEditing.SetArrayField(blockWithArray, "with",
            "[{ name: \"New Opener\", url: \"https://a.example.com\" }, { name: \"Second Act\", url: \"\" }]");

        Assert.Contains("New Opener", result);
        Assert.DoesNotContain("Old Opener", result);
        Assert.Equal(1, CountOccurrences(result, "with: ["));
        // Sibling fields on either side must survive untouched.
        Assert.Contains("id: \"gig-1\"", result);
        Assert.Contains("venue: \"Test Hall\"", result);
    }

    [Fact]
    public void SetArrayField_HandlesQuotesAndBracesInsideArrayValues()
    {
        var blockWithArray = """
            {
                id: "gig-1",
                with: [{ name: "Old", url: "" }]
            }
            """;
        var rawArray = "[{ name: \"The \\\"Weird\\\" Band {feat. someone}\", url: \"https://example.com/a\\\"b\" }]";

        var result = SiteTextEditing.SetArrayField(blockWithArray, "with", rawArray);

        Assert.Contains("The \\\"Weird\\\" Band {feat. someone}", result);
        // The block must remain a single balanced object - the embedded
        // braces inside the string value must not have confused bracket
        // depth tracking into truncating the block early.
        Assert.EndsWith("}", result.TrimEnd());
        Assert.Equal(1, CountOccurrences(result, "with: ["));
    }

    [Fact]
    public void SetArrayField_ThenAppendFallback_RoundTripsALegacyGigToTheNewFormat()
    {
        // The exact two-step sequence GigsSiteEditor.UpdateGigWithAsync
        // performs: try SetArrayField (no-op on a legacy gig), detect the
        // no-op, fall back to AppendField.
        var legacyBlock = """
            {
                id: "legacy-1",
                withArtists: "Old Band",
                withArtistsUrl: "https://old.example.com",
                venue: "Test Hall"
            }
            """;
        var rawArray = "[{ name: \"New Band A\", url: \"https://a.example.com\" }, { name: \"New Band B\", url: \"\" }]";

        var afterSet = SiteTextEditing.SetArrayField(legacyBlock, "with", rawArray);
        Assert.Equal(legacyBlock, afterSet); // confirms the no-op branch is what actually triggers the fallback

        var afterAppend = SiteTextEditing.AppendField(afterSet, "with", rawArray);

        Assert.Contains("with: [{ name: \"New Band A\"", afterAppend);
        // Legacy scalar fields must be left alone - see Gig.EffectiveWith's
        // doc comment for why both are allowed to coexist.
        Assert.Contains("withArtists: \"Old Band\"", afterAppend);
        Assert.Contains("withArtistsUrl: \"https://old.example.com\"", afterAppend);

        // A second edit against the now-upgraded block must go through
        // SetArrayField's replace-in-place branch, not append a duplicate.
        var secondEdit = SiteTextEditing.SetArrayField(afterAppend, "with", "[{ name: \"Replaced\", url: \"\" }]");
        Assert.Equal(1, CountOccurrences(secondEdit, "with: ["));
        Assert.Contains("Replaced", secondEdit);
        Assert.DoesNotContain("New Band A", secondEdit);
    }

    // --- SetBooleanField ---

    [Fact]
    public void SetBooleanField_ReplacesExistingBareBoolean()
    {
        var block = "{\n    id: \"x\",\n    freeAdmission: false\n}";
        var result = SiteTextEditing.SetBooleanField(block, "freeAdmission", true);

        Assert.Contains("freeAdmission: true", result);
    }

    [Fact]
    public void SetBooleanField_AppendsWhenAbsent()
    {
        var block = "{\n    id: \"x\"\n}";
        var result = SiteTextEditing.SetBooleanField(block, "freeAdmission", true);

        Assert.Contains("freeAdmission: true", result);
    }

    // --- InsertBeforeClose ---

    [Fact]
    public void InsertBeforeClose_InsertsNewEntryBeforeTrailingBracket()
    {
        var arrayText = "[\n    { id: \"a\" },\n    { id: \"b\" }\n]";
        var result = SiteTextEditing.InsertBeforeClose(arrayText, ']', "    { id: \"c\" }");

        Assert.NotNull(result);
        Assert.Contains("{ id: \"c\" }", result);
        // Must come after b, before the closing bracket - not appended
        // past it or inserted mid-array.
        Assert.True(result!.IndexOf("id: \"b\"", StringComparison.Ordinal) < result.IndexOf("id: \"c\"", StringComparison.Ordinal));
        Assert.True(result.IndexOf("id: \"c\"", StringComparison.Ordinal) < result.LastIndexOf(']'));
    }

    [Fact]
    public void InsertBeforeClose_ReturnsNull_OnUnexpectedShape()
    {
        var malformed = "not an array at all";
        Assert.Null(SiteTextEditing.InsertBeforeClose(malformed, ']', "anything"));
    }

    // --- Slugify ---

    [Theory]
    [InlineData("Blue Fox Billiards!", "blue-fox-billiards")]
    [InlineData("  leading and trailing  ", "leading-and-trailing")]
    [InlineData("!!!", "fallback")]
    public void Slugify_ProducesUrlSafeSlugsOrFallback(string input, string expected)
    {
        Assert.Equal(expected, SiteTextEditing.Slugify(input, "fallback"));
    }

    [Fact]
    public void Slugify_TruncatesToSixtyCharacters()
    {
        var longInput = string.Concat(Enumerable.Repeat("a ", 100));
        var slug = SiteTextEditing.Slugify(longInput, "fallback");

        Assert.True(slug.Length <= 60);
    }

    // --- UniqueId ---

    [Fact]
    public void UniqueId_ReturnsBaseSlug_WhenNotTaken()
    {
        Assert.Equal("new-gig", SiteTextEditing.UniqueId(SampleArrayFile, "new-gig"));
    }

    [Fact]
    public void UniqueId_AppendsIncrementingSuffix_WhenTaken()
    {
        Assert.Equal("gig-1-2", SiteTextEditing.UniqueId(SampleArrayFile, "gig-1"));
    }

    [Fact]
    public void UniqueId_SkipsSuffixesAlreadyTaken()
    {
        var code = """
            { id: "show-1" },
            { id: "show" },
            { id: "show-2" }
            """;
        Assert.Equal("show-3", SiteTextEditing.UniqueId(code, "show"));
    }

    // --- RemoveBlock ---

    [Fact]
    public void RemoveBlock_RemovesEntryAndFollowingComma()
    {
        var block = SiteTextEditing.FindBlock(SampleArrayFile, "id", "gig-1");
        Assert.NotNull(block);

        var result = SiteTextEditing.RemoveBlock(SampleArrayFile, block!);

        Assert.DoesNotContain("gig-1", result);
        Assert.Contains("gig-2", result);
        // The array must still be syntactically valid - no dangling comma
        // immediately after the opening bracket.
        var bounds = SiteTextEditing.FindArrayBounds(result, "gigs");
        Assert.NotNull(bounds);
        var arrayText = result[bounds!.Value.Start..bounds.Value.End];
        Assert.DoesNotContain("[\n    ,", arrayText);
    }

    [Fact]
    public void RemoveBlock_RemovesLastEntry_UsingPrecedingComma()
    {
        var block = SiteTextEditing.FindBlock(SampleArrayFile, "id", "gig-2");
        Assert.NotNull(block);

        var result = SiteTextEditing.RemoveBlock(SampleArrayFile, block!);

        Assert.DoesNotContain("gig-2", result);
        Assert.Contains("gig-1", result);
        Assert.DoesNotContain(",\n]", result);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
