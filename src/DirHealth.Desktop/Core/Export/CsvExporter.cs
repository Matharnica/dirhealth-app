using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Export;

/// <summary>
/// CSV export (S13-5): UTF-8 with BOM so Excel shows umlauts correctly, the Windows
/// region's list separator as delimiter (English Excel expects ",", German ";"), and
/// every text field routed through <see cref="SafeField"/> against formula injection.
/// </summary>
public class CsvExporter
{
    private readonly string _delimiter;

    /// <param name="delimiter">Null = the current Windows region's list separator.</param>
    public CsvExporter(string? delimiter = null)
    {
        _delimiter = string.IsNullOrWhiteSpace(delimiter) ? RegionListSeparator() : delimiter;
    }

    public string Delimiter => _delimiter;

    /// <summary>What Excel uses to split a double-clicked CSV on this machine.</summary>
    public static string RegionListSeparator()
    {
        var sep = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        return string.IsNullOrWhiteSpace(sep) ? "," : sep;
    }

    // The CsvWriter owns (and disposes) the StreamWriter.
    private CsvWriter OpenCsv(string filePath)
    {
        // encoderShouldEmitUTF8Identifier: true → BOM; without it Excel reads the file as ANSI
        var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        // InvariantCulture keeps number/date formatting stable; only the delimiter is regional.
        return new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = _delimiter });
    }

    public void ExportFindings(List<AdFinding> findings, string filePath)
    {
        using var csv    = OpenCsv(filePath);
        csv.WriteHeader<FindingRow>();
        csv.NextRecord();
        foreach (var f in findings)
        {
            csv.WriteRecord(new FindingRow(
                SafeField(f.Category), SafeField(f.Title), f.Severity.ToString(),
                f.Count, string.Join("; ", f.AffectedObjects.Select(SafeField))));
            csv.NextRecord();
        }
    }

    public void ExportPasswordReport(IEnumerable<AdUser> users, string filePath)
    {
        using var csv    = OpenCsv(filePath);
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
                SafeField(DnHelper.OuFromDn(u.DistinguishedName))));
            csv.NextRecord();
        }
    }

    public void ExportInactiveUsers(IEnumerable<AdUser> users, string filePath)
    {
        using var csv    = OpenCsv(filePath);
        csv.WriteHeader<InactiveUserRow>();
        csv.NextRecord();
        foreach (var u in users)
        {
            csv.WriteRecord(new InactiveUserRow(
                SafeField(u.DisplayName),
                SafeField(u.SamAccountName),
                SafeField(u.Email),
                u.LastLogon?.ToString("yyyy-MM-dd") ?? "Never",
                SafeField(DnHelper.OuFromDn(u.DistinguishedName))));
            csv.NextRecord();
        }
    }

    public void ExportDataQuality(IEnumerable<AdAttributeCompleteness> attributes, string filePath)
    {
        using var csv    = OpenCsv(filePath);
        csv.WriteHeader<DataQualityRow>();
        csv.NextRecord();
        foreach (var a in attributes)
        {
            csv.WriteRecord(new DataQualityRow(
                SafeField(a.AttributeName), SafeField(a.LdapName),
                a.FilledCount, a.TotalCount, a.PercentLabel));
            csv.NextRecord();
        }
    }

    // Characters that make Excel/LibreOffice/Sheets treat a cell as a formula. Includes the
    // full-width forms (U+FF1D/FF0B/FF0D/FF20), which some spreadsheet apps normalise to ASCII.
    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '＝', '＋', '－', '＠'];

    /// <summary>
    /// Prefixes an apostrophe when the value could be read as a formula (OWASP CSV
    /// injection): it starts with a formula character, with tab/CR/LF, or with
    /// whitespace followed by a formula character (spreadsheets trim leading blanks).
    /// </summary>
    public static string SafeField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '\t' or '\r' or '\n') return "'" + value;
        var firstVisible = value.TrimStart(' ', '\u00A0', '\u3000');
        return firstVisible.Length > 0 && FormulaStarters.Contains(firstVisible[0]) ? "'" + value : value;
    }

    private record FindingRow(string Category, string Title, string Severity, int Count, string AffectedObjects);
    private record PasswordReportRow(string DisplayName, string SamAccountName, string Email, string PasswordExpires, string DaysRemaining, string OU);
    private record InactiveUserRow(string DisplayName, string SamAccountName, string Email, string LastLogon, string OU);
    private record DataQualityRow(string Attribute, string LdapName, int Filled, int Total, string Percent);
}
