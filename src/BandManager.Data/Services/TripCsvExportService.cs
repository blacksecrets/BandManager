using System.Globalization;
using CsvHelper;

namespace BandManager.Data.Services;

public record TripExportRow(string Date, string From, string To, bool RoundTrip, string Reason, double? TotalMiles);

/// <summary>
/// The mileage-log counterpart to ExpenseCsvExportService, for the same
/// annual tax-prep download - see TravelController.ExportTrips. No dollar
/// amount column: this app doesn't track or compute an IRS mileage rate
/// anywhere (rates change yearly and vary by purpose - getting that wrong
/// in an exported tax record is worse than not offering it), so the
/// export stays scoped to what's actually tracked, miles.
/// </summary>
public static class TripCsvExportService
{
    public static string BuildCsv(IEnumerable<TripExportRow> rows)
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Date");
        csv.WriteField("From");
        csv.WriteField("To");
        csv.WriteField("Round Trip");
        csv.WriteField("Reason");
        csv.WriteField("Miles");
        csv.NextRecord();

        foreach (var r in rows)
        {
            csv.WriteField(r.Date);
            csv.WriteField(r.From);
            csv.WriteField(r.To);
            csv.WriteField(r.RoundTrip ? "Yes" : "No");
            csv.WriteField(r.Reason);
            csv.WriteField(r.TotalMiles?.ToString("F1", CultureInfo.InvariantCulture) ?? "");
            csv.NextRecord();
        }

        return writer.ToString();
    }
}
