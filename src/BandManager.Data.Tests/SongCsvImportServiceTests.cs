using System.Text;
using BandManager.Data.Services;
using Xunit;

namespace BandManager.Data.Tests;

/// <summary>
/// SongCsvImportService is the format-validation gate for both SuperAdmin's
/// catalog-wide import and a Band Admin's repertoire import - a bug here
/// either lets bad data through unvalidated, or wrongly rejects a good
/// file. The "reject the whole file if any row fails" contract (confirmed
/// with the user, not assumed) is tested explicitly.
/// </summary>
public class SongCsvImportServiceTests
{
    private static byte[] Csv(string content) => Encoding.UTF8.GetBytes(content);

    private const string ValidHeader = "Title,OriginalArtist,Album,Key,Length,YouTubeUrl,SpotifyUrl,SongsterrUrl";

    [Fact]
    public void Parse_ValidFile_ReturnsRowsAndNoErrors()
    {
        var csv = Csv($"{ValidHeader}\nSweet Child O' Mine,Guns N' Roses,Appetite for Destruction,Eb,5:56,https://www.youtube.com/watch?v=1,https://open.spotify.com/track/1,https://www.songsterr.com/a/wsa/x\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("Sweet Child O' Mine", row.Title);
        Assert.Equal("Guns N' Roses", row.OriginalArtist);
        Assert.Equal(356, row.LengthSeconds); // 5:56 -> 356 seconds
    }

    [Fact]
    public void Parse_MinimalValidFile_OnlyRequiredFieldsFilled()
    {
        var csv = Csv($"{ValidHeader}\nA Song,An Artist,,,,,,\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Null(row.Album);
        Assert.Null(row.LengthSeconds);
        Assert.Null(row.YouTubeUrl);
    }

    [Fact]
    public void Parse_MissingHeaderColumn_RejectsWithSingleHeaderError()
    {
        var csv = Csv("Title,OriginalArtist,Album,Key,Length,YouTubeUrl,SpotifyUrl\nA Song,An Artist,,,,,\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Rows); // whole file rejected
        var error = Assert.Single(result.Errors);
        Assert.Equal(1, error.Row);
        Assert.Contains("SongsterrUrl", error.Message);
    }

    [Fact]
    public void Parse_ExtraUnexpectedColumn_IsRejected()
    {
        var csv = Csv($"{ValidHeader},ExtraColumn\nA Song,An Artist,,,,,,,extra\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Contains("ExtraColumn", error.Message);
    }

    [Fact]
    public void Parse_HeaderOrderDoesNotMatter()
    {
        var reordered = "OriginalArtist,Title,SongsterrUrl,SpotifyUrl,YouTubeUrl,Length,Key,Album";
        var csv = Csv($"{reordered}\nAn Artist,A Song,,,,,,\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("A Song", row.Title);
        Assert.Equal("An Artist", row.OriginalArtist);
    }

    [Fact]
    public void Parse_MissingRequiredTitle_ProducesRowError_AndRejectsWholeFile()
    {
        var csv = Csv($"{ValidHeader}\n,An Artist,,,,,,\nGood Song,Good Artist,,,,,,\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Rows); // the whole file, including the otherwise-good row 3
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.Row); // header is row 1, first data row is row 2
        Assert.Equal("Title", error.Column);
    }

    [Fact]
    public void Parse_MissingRequiredArtist_ProducesRowError()
    {
        var csv = Csv($"{ValidHeader}\nA Song,,,,,,,\n");

        var result = SongCsvImportService.Parse(csv);

        var error = Assert.Single(result.Errors);
        Assert.Equal("OriginalArtist", error.Column);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://youtube.com/watch?v=1")]
    [InlineData("https://vimeo.com/12345")] // valid URL, wrong host
    public void Parse_InvalidYouTubeUrl_IsRejected(string badUrl)
    {
        var csv = Csv($"{ValidHeader}\nA Song,An Artist,,,,{badUrl},,\n");

        var result = SongCsvImportService.Parse(csv);

        var error = Assert.Single(result.Errors);
        Assert.Equal("YouTubeUrl", error.Column);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("https://youtu.be/abc")]
    public void Parse_ValidYouTubeUrlHosts_AreAccepted(string goodUrl)
    {
        var csv = Csv($"{ValidHeader}\nA Song,An Artist,,,,{goodUrl},,\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_InvalidLengthFormat_IsRejected()
    {
        var csv = Csv($"{ValidHeader}\nA Song,An Artist,,,3 minutes,,,\n");

        var result = SongCsvImportService.Parse(csv);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Length", error.Column);
    }

    [Fact]
    public void Parse_LengthOverTwoHours_IsRejected()
    {
        var csv = Csv($"{ValidHeader}\nA Song,An Artist,,,121:00,,,\n"); // 121:00 = 2h1m = 7260s, over the 7200s cap

        var result = SongCsvImportService.Parse(csv);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Length", error.Column);
        Assert.Contains("2 hours", error.Message);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("F#m")]
    [InlineData("Bb major")]
    [InlineData("C minor")]
    public void Parse_ValidKeyFormats_AreAccepted(string key)
    {
        var csv = Csv($"{ValidHeader}\nA Song,An Artist,,{key},,,,\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_InvalidKeyFormat_IsRejected()
    {
        var csv = Csv($"{ValidHeader}\nA Song,An Artist,,not a key at all,,,,\n");

        var result = SongCsvImportService.Parse(csv);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Key", error.Column);
    }

    [Fact]
    public void Parse_MultipleBadRows_ReportsEveryError_NotJustTheFirst()
    {
        var csv = Csv($"{ValidHeader}\n,An Artist,,,,,,\nA Song,,,,,,,\n");

        var result = SongCsvImportService.Parse(csv);

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Row == 2 && e.Column == "Title");
        Assert.Contains(result.Errors, e => e.Row == 3 && e.Column == "OriginalArtist");
    }

    [Fact]
    public void Parse_MalformedCsv_ProducesAFileLevelError_RatherThanThrowing()
    {
        // An unterminated quote is the classic CSV corruption case -
        // Parse must degrade to a clear error, never throw out to the
        // caller (SongCsvImportController has no try/catch around Parse).
        var csv = Csv($"{ValidHeader}\n\"Unterminated,An Artist,,,,,,\n");

        var exception = Record.Exception(() => SongCsvImportService.Parse(csv));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildTemplateCsv_HasExactlyTheExpectedHeaderRow()
    {
        var template = SongCsvImportService.BuildTemplateCsv();
        var firstLine = template.Split('\n')[0].TrimEnd('\r');

        Assert.Equal(string.Join(",", SongCsvImportService.ExpectedHeaders), firstLine);
    }

    [Fact]
    public void BuildTemplateCsv_ExampleRowParsesCleanlyThroughParse()
    {
        // The template is only useful if re-uploading it unmodified
        // actually passes validation.
        var template = SongCsvImportService.BuildTemplateCsv();

        var result = SongCsvImportService.Parse(Encoding.UTF8.GetBytes(template));

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
    }
}
