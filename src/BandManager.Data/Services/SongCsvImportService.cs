using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace BandManager.Data.Services;

/// <summary>One validation failure, 1-based row numbers so row 1 is the
/// header and the first data row is row 2 - matches what a user sees when
/// they open the file in a spreadsheet app.</summary>
public record CsvRowError(int Row, string Column, string Message);

public record ParsedSongRow(
    string Title, string OriginalArtist, string? Album, string? Key,
    int? LengthSeconds, string? YouTubeUrl, string? SpotifyUrl, string? SongsterrUrl);

public record CsvParseResult(List<ParsedSongRow> Rows, List<CsvRowError> Errors);

/// <summary>
/// Parses/validates a SuperAdmin's bulk song-catalog CSV upload - pure,
/// no ASP.NET types (the Web layer converts its own IFormFile to plain
/// bytes via FormFileExtensions/UploadedFilePayload before calling this,
/// same split CatalogStore.ResolveMediaInputAsync already uses). Every row
/// is checked before any pass/fail decision - the whole file is rejected
/// together if anything's wrong (confirmed with the user: no partial
/// import), so the caller can fix everything in one pass and re-upload.
/// </summary>
public static class SongCsvImportService
{
    public static readonly string[] ExpectedHeaders =
        ["Title", "OriginalArtist", "Album", "Key", "Length", "YouTubeUrl", "SpotifyUrl", "SongsterrUrl"];

    private static readonly Regex KeyPattern = new(@"^[A-Ga-g][#b]?\s*(major|minor|maj|min|m)?$", RegexOptions.Compiled);
    private static readonly Regex LengthPattern = new(@"^(\d{1,3}):([0-5]\d)$", RegexOptions.Compiled);

    public static CsvParseResult Parse(byte[] bytes)
    {
        var errors = new List<CsvRowError>();
        var rows = new List<ParsedSongRow>();

        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);

        bool headerOk;
        try
        {
            headerOk = csv.Read() && csv.ReadHeader();
        }
        catch (Exception ex)
        {
            return new CsvParseResult([], [new CsvRowError(1, "(file)", $"Could not parse the file as CSV: {ex.Message}")]);
        }

        if (!headerOk || csv.HeaderRecord is null)
            return new CsvParseResult([], [new CsvRowError(1, "(header)", "The file has no header row.")]);

        var actualHeaders = csv.HeaderRecord.Select(h => h.Trim()).ToArray();
        var missing = ExpectedHeaders.Where(h => !actualHeaders.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        var extra = actualHeaders.Where(h => !ExpectedHeaders.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            var parts = new List<string>();
            if (missing.Count > 0) parts.Add($"missing column(s): {string.Join(", ", missing)}");
            if (extra.Count > 0) parts.Add($"unexpected column(s): {string.Join(", ", extra)}");
            return new CsvParseResult([], [new CsvRowError(1, "(header)",
                $"The header row must have exactly these columns: {string.Join(", ", ExpectedHeaders)} - {string.Join("; ", parts)}.")]);
        }

        var rowNum = 1; // header
        try
        {
            while (csv.Read())
            {
                rowNum++;
                string Field(string name) => (csv.GetField(name) ?? "").Trim();

                var title = Field("Title");
                var artist = Field("OriginalArtist");
                var album = Field("Album");
                var key = Field("Key");
                var length = Field("Length");
                var youTube = Field("YouTubeUrl");
                var spotify = Field("SpotifyUrl");
                var songsterr = Field("SongsterrUrl");

                if (string.IsNullOrEmpty(title))
                    errors.Add(new CsvRowError(rowNum, "Title", "Title is required."));
                else if (title.Length > 300)
                    errors.Add(new CsvRowError(rowNum, "Title", "Title must be 300 characters or fewer."));

                if (string.IsNullOrEmpty(artist))
                    errors.Add(new CsvRowError(rowNum, "OriginalArtist", "Original Artist is required."));
                else if (artist.Length > 300)
                    errors.Add(new CsvRowError(rowNum, "OriginalArtist", "Original Artist must be 300 characters or fewer."));

                if (album.Length > 300)
                    errors.Add(new CsvRowError(rowNum, "Album", "Album must be 300 characters or fewer."));

                if (key.Length > 0 && (key.Length > 20 || !KeyPattern.IsMatch(key)))
                    errors.Add(new CsvRowError(rowNum, "Key", "Key must look like a musical key, e.g. \"A\", \"F#m\", \"Bb major\"."));

                int? lengthSeconds = null;
                if (length.Length > 0)
                {
                    var m = LengthPattern.Match(length);
                    if (!m.Success)
                    {
                        errors.Add(new CsvRowError(rowNum, "Length", "Length must be in m:ss or mm:ss format, e.g. \"3:45\"."));
                    }
                    else
                    {
                        lengthSeconds = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
                        if (lengthSeconds > 7200)
                            errors.Add(new CsvRowError(rowNum, "Length", "Length can't be more than 2 hours."));
                    }
                }

                ValidateUrl(youTube, rowNum, "YouTubeUrl", ["youtube.com", "youtu.be"], errors);
                ValidateUrl(spotify, rowNum, "SpotifyUrl", ["spotify.com"], errors);
                ValidateUrl(songsterr, rowNum, "SongsterrUrl", ["songsterr.com"], errors);

                rows.Add(new ParsedSongRow(
                    title, artist,
                    album.Length > 0 ? album : null,
                    key.Length > 0 ? key : null,
                    lengthSeconds,
                    youTube.Length > 0 ? youTube : null,
                    spotify.Length > 0 ? spotify : null,
                    songsterr.Length > 0 ? songsterr : null));
            }
        }
        catch (Exception ex)
        {
            errors.Add(new CsvRowError(rowNum + 1, "(file)", $"Could not parse the file as CSV: {ex.Message}"));
        }

        return new CsvParseResult(errors.Count == 0 ? rows : [], errors);
    }

    private static void ValidateUrl(string value, int row, string column, string[] allowedHostFragments, List<CsvRowError> errors)
    {
        if (value.Length == 0) return;
        if (value.Length > 500)
        {
            errors.Add(new CsvRowError(row, column, $"{column} must be 500 characters or fewer."));
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            errors.Add(new CsvRowError(row, column, $"{column} must be a valid http(s) URL."));
            return;
        }
        if (!allowedHostFragments.Any(f => uri.Host.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(new CsvRowError(row, column, $"{column} must be a {string.Join(" or ", allowedHostFragments)} link."));
        }
    }

    /// <summary>Header row + one filled example row, served by the
    /// template-download endpoint.</summary>
    public static string BuildTemplateCsv()
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        foreach (var h in ExpectedHeaders) csv.WriteField(h);
        csv.NextRecord();
        csv.WriteField("Sweet Child O' Mine");
        csv.WriteField("Guns N' Roses");
        csv.WriteField("Appetite for Destruction");
        csv.WriteField("Eb");
        csv.WriteField("5:56");
        csv.WriteField("https://www.youtube.com/watch?v=1w7OgIMMRc4");
        csv.WriteField("https://open.spotify.com/track/7o2CTH4ctstm8TNelqjb51");
        csv.WriteField("https://www.songsterr.com/a/wsa/guns-n-roses-sweet-child-o-mine-tab");
        csv.NextRecord();
        return writer.ToString();
    }
}
