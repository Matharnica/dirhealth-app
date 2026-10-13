using System.IO;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Export;

/// <summary>Everything the single-user security dossier contains (S15-5).</summary>
public record UserDossier(
    AdUser                     User,
    IReadOnlyList<DetailRow>   Attributes,
    IReadOnlyList<AdUserGroup> Groups,
    IReadOnlyList<AdFinding>   Findings,
    ExportContext              Context,
    string                     ScanTime,
    DateTime                   CreatedUtc,
    string                     Version)
{
    public bool IsTier0 => Groups.Any(g => g.IsTier0);
}

/// <summary>
/// Writes the dossier (S15-5): PDF for reading, plus one CSV per list (groups with path,
/// affected findings) — never a one-row CSV of the user itself. Every file gets a
/// .meta.txt with its SHA-256 (S14-5). Empty lists produce no CSV.
/// </summary>
public static class UserDossierExporter
{
    public static IReadOnlyList<TableColumn<AdUserGroup>> GroupColumns { get; } =
    [
        new("Group",      g => g.Name,      nameof(AdUserGroup.Name)),
        new("Membership", g => g.KindLabel, nameof(AdUserGroup.KindLabel)),
        new("Tier 0",     g => g.IsTier0,   nameof(AdUserGroup.IsTier0), yesNo: true),
        new("Path",       g => g.Path,      nameof(AdUserGroup.Path)),
        new("Distinguished Name", g => g.DistinguishedName),
    ];

    public static IReadOnlyList<TableColumn<AdFinding>> FindingColumns { get; } =
    [
        new("Finding",     f => f.Title,       nameof(AdFinding.Title)),
        new("Severity",    f => f.Severity,    nameof(AdFinding.Severity)),
        new("Category",    f => f.Category,    nameof(AdFinding.Category)),
        new("Remediation", f => f.Remediation, nameof(AdFinding.Remediation)),
    ];

    public static string GroupsCsvPath(string pdfPath)   => SiblingPath(pdfPath, "-groups.csv");
    public static string FindingsCsvPath(string pdfPath) => SiblingPath(pdfPath, "-findings.csv");

    private static string SiblingPath(string pdfPath, string suffix) =>
        Path.Combine(Path.GetDirectoryName(pdfPath) ?? "", Path.GetFileNameWithoutExtension(pdfPath) + suffix);

    /// <summary>Writes the CSV + metadata side files. Returns the paths written.</summary>
    public static List<string> WriteCsvs(UserDossier d, string pdfPath)
    {
        var written = new List<string>();
        var filter  = $"user={d.User.SamAccountName}";
        var csv     = new CsvExporter();

        if (d.Groups.Count > 0)
        {
            var path = GroupsCsvPath(pdfPath);
            var cols = GroupColumns.Cast<ITableColumn>().ToList();
            int n = csv.ExportTable(d.Groups, cols, path);
            ExportMetadata.WriteFor(path, d.Context, "User dossier — groups", filter, n);
            written.Add(path);
        }
        if (d.Findings.Count > 0)
        {
            var path = FindingsCsvPath(pdfPath);
            var cols = FindingColumns.Cast<ITableColumn>().ToList();
            int n = csv.ExportTable(d.Findings, cols, path);
            ExportMetadata.WriteFor(path, d.Context, "User dossier — findings", filter, n);
            written.Add(path);
        }
        return written;
    }

    /// <summary>PDF + CSVs + metadata. Returns all paths written.</summary>
    public static List<string> Export(UserDossier d, string pdfPath)
    {
        new PdfExporter().ExportUserDossier(d, pdfPath);
        ExportMetadata.WriteFor(pdfPath, d.Context, "User dossier", $"user={d.User.SamAccountName}", 1);
        var written = new List<string> { pdfPath };
        written.AddRange(WriteCsvs(d, pdfPath));
        return written;
    }
}
