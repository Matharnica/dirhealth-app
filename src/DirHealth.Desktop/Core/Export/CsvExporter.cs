using System.Globalization;
using System.IO;
using CsvHelper;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Export;

public class CsvExporter
{
    public void ExportFindings(List<AdFinding> findings, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        using var csv    = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteHeader<FindingRow>();
        csv.NextRecord();
        foreach (var f in findings)
        {
            csv.WriteRecord(new FindingRow(
                f.Category, f.Title, f.Severity.ToString(),
                f.Count, string.Join("; ", f.AffectedObjects.Select(SafeField))));
            csv.NextRecord();
        }
    }

    public void ExportPasswordReport(IEnumerable<AdUser> users, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        using var csv    = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteHeader<PasswordReportRow>();
        csv.NextRecord();
        foreach (var u in users)
        {
            csv.WriteRecord(new PasswordReportRow(
                SafeField(u.DisplayName),
                SafeField(u.SamAccountName),
                SafeField(u.Email),
                u.PasswordExpires?.ToString("yyyy-MM-dd") ?? "",
                u.DaysUntilPasswordExpiry?.ToString() ?? "",
                DnHelper.OuFromDn(u.DistinguishedName)));
            csv.NextRecord();
        }
    }

    public void ExportInactiveUsers(IEnumerable<AdUser> users, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        using var csv    = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteHeader<InactiveUserRow>();
        csv.NextRecord();
        foreach (var u in users)
        {
            csv.WriteRecord(new InactiveUserRow(
                SafeField(u.DisplayName),
                SafeField(u.SamAccountName),
                SafeField(u.Email),
                u.LastLogon?.ToString("yyyy-MM-dd") ?? "Never",
                DnHelper.OuFromDn(u.DistinguishedName)));
            csv.NextRecord();
        }
    }

    // Prefix formula-starting characters to prevent CSV injection when the file is opened in spreadsheet software
    private static string SafeField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    private record FindingRow(string Category, string Title, string Severity, int Count, string AffectedObjects);
    private record PasswordReportRow(string DisplayName, string SamAccountName, string Email, string PasswordExpires, string DaysRemaining, string OU);
    private record InactiveUserRow(string DisplayName, string SamAccountName, string Email, string LastLogon, string OU);
}
