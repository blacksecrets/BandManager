using System.Globalization;
using CsvHelper;

namespace BandManager.Data.Services;

public record ExpenseExportRow(
    string PurchaseDate, string? MemberName, string Purpose, string? Vendor,
    string? VendorUrl, decimal Amount, bool IsReimbursed, string? ReceiptUrl);

/// <summary>
/// Builds the annual expense-export spreadsheet ("Annual Tax prep download
/// spreadsheet with receipts links" from Richard's fast-follow list) -
/// same CsvWriter pattern as VenueCsvImportService/SongCsvImportService's
/// own template builders. includeMemberColumn is on for a BandAdmin's
/// band-wide export and off for a member's own (every row is already
/// theirs, so a repeated name column would just be noise).
/// </summary>
public static class ExpenseCsvExportService
{
    public static string BuildCsv(IEnumerable<ExpenseExportRow> rows, bool includeMemberColumn)
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Date");
        if (includeMemberColumn) csv.WriteField("Member");
        csv.WriteField("Purpose");
        csv.WriteField("Vendor");
        csv.WriteField("Vendor URL");
        csv.WriteField("Amount");
        csv.WriteField("Reimbursed");
        csv.WriteField("Receipt Link");
        csv.NextRecord();

        foreach (var r in rows)
        {
            csv.WriteField(r.PurchaseDate);
            if (includeMemberColumn) csv.WriteField(r.MemberName ?? "");
            csv.WriteField(r.Purpose);
            csv.WriteField(r.Vendor ?? "");
            csv.WriteField(r.VendorUrl ?? "");
            csv.WriteField(r.Amount.ToString("F2", CultureInfo.InvariantCulture));
            csv.WriteField(r.IsReimbursed ? "Yes" : "No");
            csv.WriteField(r.ReceiptUrl ?? "");
            csv.NextRecord();
        }

        return writer.ToString();
    }
}
