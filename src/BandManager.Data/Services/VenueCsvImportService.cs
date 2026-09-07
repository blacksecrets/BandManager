using System.Globalization;
using BandManager.Data.Entities;
using CsvHelper;
using CsvHelper.Configuration;

namespace BandManager.Data.Services;

public record ParsedVenueRow(
    string Name, string? AddressLine1, string? City, string? State, string? PostalCode,
    string? Phone, string? Website, string? Notes,
    string? ContactName, string? ContactEmail, string? ContactPhone);

public record ParsedCommunicationRow(
    string VenueName, DateTime OccurredAt, CommunicationType Type,
    string? To, string? Cc, string? Bcc, string? From, string? Subject, string Body, string? OutcomeNotes);

public record VenueCsvParseResult<T>(List<T> Rows, List<CsvRowError> Errors);

/// <summary>
/// Parses/validates the two venue-outreach bulk-upload CSVs (venues+
/// primary contact, and historical communications against venues that
/// already exist) - same "pure, no ASP.NET types, whole file rejected
/// together if anything's wrong" shape as SongCsvImportService.
/// </summary>
public static class VenueCsvImportService
{
    public static readonly string[] VenueHeaders =
        ["Name", "AddressLine1", "City", "State", "PostalCode", "Phone", "Website", "Notes", "ContactName", "ContactEmail", "ContactPhone"];

    public static readonly string[] CommunicationHeaders =
        ["VenueName", "Date", "Type", "To", "Cc", "Bcc", "From", "Subject", "Body", "OutcomeNotes"];

    private static CsvReader OpenReader(byte[] bytes, out StreamReader streamReader)
    {
        var stream = new MemoryStream(bytes);
        streamReader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null, BadDataFound = null };
        return new CsvReader(streamReader, config);
    }

    private static List<CsvRowError>? ValidateHeader(CsvReader csv, string[] expectedHeaders)
    {
        bool headerOk;
        try { headerOk = csv.Read() && csv.ReadHeader(); }
        catch (Exception ex) { return [new CsvRowError(1, "(file)", $"Could not parse the file as CSV: {ex.Message}")]; }

        if (!headerOk || csv.HeaderRecord is null) return [new CsvRowError(1, "(header)", "The file has no header row.")];

        var actual = csv.HeaderRecord.Select(h => h.Trim()).ToArray();
        var missing = expectedHeaders.Where(h => !actual.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        var extra = actual.Where(h => !expectedHeaders.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            var parts = new List<string>();
            if (missing.Count > 0) parts.Add($"missing column(s): {string.Join(", ", missing)}");
            if (extra.Count > 0) parts.Add($"unexpected column(s): {string.Join(", ", extra)}");
            return [new CsvRowError(1, "(header)", $"The header row must have exactly these columns: {string.Join(", ", expectedHeaders)} - {string.Join("; ", parts)}.")];
        }
        return null;
    }

    public static VenueCsvParseResult<ParsedVenueRow> ParseVenues(byte[] bytes)
    {
        var errors = new List<CsvRowError>();
        var rows = new List<ParsedVenueRow>();
        using var csv = OpenReader(bytes, out var sr);
        using var _ = sr;

        if (ValidateHeader(csv, VenueHeaders) is { } headerErrors) return new VenueCsvParseResult<ParsedVenueRow>([], headerErrors);

        var rowNum = 1;
        try
        {
            while (csv.Read())
            {
                rowNum++;
                string Field(string name) => (csv.GetField(name) ?? "").Trim();
                string? Opt(string name) => Field(name) is { Length: > 0 } v ? v : null;

                var name = Field("Name");
                if (string.IsNullOrEmpty(name)) errors.Add(new CsvRowError(rowNum, "Name", "Name is required."));
                else if (name.Length > 300) errors.Add(new CsvRowError(rowNum, "Name", "Name must be 300 characters or fewer."));

                var contactEmail = Opt("ContactEmail");
                if (contactEmail is not null && !contactEmail.Contains('@'))
                    errors.Add(new CsvRowError(rowNum, "ContactEmail", "ContactEmail must look like an email address."));

                rows.Add(new ParsedVenueRow(
                    name, Opt("AddressLine1"), Opt("City"), Opt("State"), Opt("PostalCode"),
                    Opt("Phone"), Opt("Website"), Opt("Notes"),
                    Opt("ContactName"), contactEmail, Opt("ContactPhone")));
            }
        }
        catch (Exception ex)
        {
            errors.Add(new CsvRowError(rowNum + 1, "(file)", $"Could not parse the file as CSV: {ex.Message}"));
        }

        return new VenueCsvParseResult<ParsedVenueRow>(errors.Count == 0 ? rows : [], errors);
    }

    public static VenueCsvParseResult<ParsedCommunicationRow> ParseCommunications(byte[] bytes)
    {
        var errors = new List<CsvRowError>();
        var rows = new List<ParsedCommunicationRow>();
        using var csv = OpenReader(bytes, out var sr);
        using var _ = sr;

        if (ValidateHeader(csv, CommunicationHeaders) is { } headerErrors) return new VenueCsvParseResult<ParsedCommunicationRow>([], headerErrors);

        var rowNum = 1;
        try
        {
            while (csv.Read())
            {
                rowNum++;
                string Field(string name) => (csv.GetField(name) ?? "").Trim();
                string? Opt(string name) => Field(name) is { Length: > 0 } v ? v : null;

                var venueName = Field("VenueName");
                if (string.IsNullOrEmpty(venueName)) errors.Add(new CsvRowError(rowNum, "VenueName", "VenueName is required."));

                var dateRaw = Field("Date");
                DateTime occurredAt = default;
                if (string.IsNullOrEmpty(dateRaw)) errors.Add(new CsvRowError(rowNum, "Date", "Date is required."));
                else if (!DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out occurredAt))
                    errors.Add(new CsvRowError(rowNum, "Date", "Date must be a valid date, e.g. 2026-03-14."));
                else
                    occurredAt = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc);

                var typeRaw = Field("Type");
                CommunicationType type = CommunicationType.Email;
                if (!Enum.TryParse(typeRaw, ignoreCase: true, out type))
                    errors.Add(new CsvRowError(rowNum, "Type", "Type must be \"Email\" or \"Phone\"."));

                var body = Field("Body");
                if (string.IsNullOrEmpty(body)) errors.Add(new CsvRowError(rowNum, "Body", "Body (email body or phone notes) is required."));

                rows.Add(new ParsedCommunicationRow(
                    venueName, occurredAt, type,
                    Opt("To"), Opt("Cc"), Opt("Bcc"), Opt("From"), Opt("Subject"), body, Opt("OutcomeNotes")));
            }
        }
        catch (Exception ex)
        {
            errors.Add(new CsvRowError(rowNum + 1, "(file)", $"Could not parse the file as CSV: {ex.Message}"));
        }

        return new VenueCsvParseResult<ParsedCommunicationRow>(errors.Count == 0 ? rows : [], errors);
    }

    public static string BuildVenueTemplateCsv()
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        foreach (var h in VenueHeaders) csv.WriteField(h);
        csv.NextRecord();
        foreach (var v in new[]
        {
            "The Blue Room", "123 Main St", "Springfield", "VA", "22150", "555-0100", "https://blueroom.example.com", "200 cap, has house PA",
            "Jamie Rivera", "booking@blueroom.example.com", "555-0101"
        }) csv.WriteField(v);
        csv.NextRecord();
        return writer.ToString();
    }

    public static string BuildCommunicationsTemplateCsv()
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        foreach (var h in CommunicationHeaders) csv.WriteField(h);
        csv.NextRecord();
        foreach (var v in new[]
        {
            "The Blue Room", "2026-01-15", "Email", "booking@blueroom.example.com", "", "", "band@example.com",
            "Booking inquiry", "Hi, we'd love to play a show at The Blue Room...", "No response yet"
        }) csv.WriteField(v);
        csv.NextRecord();
        return writer.ToString();
    }
}
