using System.IO;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Export;

/// <summary>One line of the work list (S22-5): one rule × one affected object.</summary>
public sealed record WorklistRow(
    string RuleId, string Category, string Severity, int Weight,
    string Object, string DistinguishedName, string Sid, string Exception, string HowToFix);

/// <summary>
/// Flat work list (S22-5) and the full workbook (S22-6). Rows come from the findings of the last scan
/// plus the rule catalog (RuleId, weight, fix text) — nothing is hard-coded here, so new rules and a new
/// score formula appear without touching this file. CSV goes through <see cref="CsvExporter.ExportTable"/>
/// (OpenCsv + SafeField), XLSX through <see cref="XlsxExporter"/> (typed cells, no SafeField — ADR 0003).
/// </summary>
public static class WorklistExporter
{
    public const string SummarySheet = "Summary";
    public const string InfoSheet    = "Info";

    public static readonly IReadOnlyList<ITableColumn> Columns =
    [
        new TableColumn<WorklistRow>("RuleId",            r => r.RuleId),
        new TableColumn<WorklistRow>("Finding",           r => r.Category),
        new TableColumn<WorklistRow>("Severity",          r => r.Severity),
        new TableColumn<WorklistRow>("Weight",            r => r.Weight),
        new TableColumn<WorklistRow>("Object",            r => r.Object),
        new TableColumn<WorklistRow>("DistinguishedName", r => r.DistinguishedName),
        new TableColumn<WorklistRow>("SID",               r => r.Sid),
        new TableColumn<WorklistRow>("Exception",         r => r.Exception),
        new TableColumn<WorklistRow>("HowToFix",          r => r.HowToFix),
    ];

    /// <summary>Exception state of one object as text: "" / "Active — until … — reason" / "Expired — …" (ADR 0001).</summary>
    public static string ExceptionText(AffectedObject o) => o.Exception switch
    {
        ExceptionState.Active  => string.IsNullOrEmpty(o.ExceptionLabel) ? "Active" : $"Active — {o.ExceptionLabel}",
        ExceptionState.Expired => string.IsNullOrEmpty(o.ExceptionLabel) ? "Expired" : $"Expired — {o.ExceptionLabel}",
        _                      => "",
    };

    /// <summary>Rows of one finding. A finding without an object list (old cache, policy finding) still
    /// gets one row with an empty object, so no rule silently drops out of the work list.</summary>
    public static IEnumerable<WorklistRow> RowsOf(AdFinding f)
    {
        var rule = f.Rule;
        string ruleId = rule?.RuleId ?? "", fix = rule?.HowToFix ?? "";
        int weight = rule?.Weight ?? 0;
        if (f.Objects.Count == 0)
        {
            yield return new WorklistRow(ruleId, f.Category, f.Severity.ToString(), weight, "", "", "", "", fix);
            yield break;
        }
        foreach (var o in f.Objects.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = o.Detail.Length > 0 ? $"{o.Name} — {o.Detail}" : o.Name;
            yield return new WorklistRow(ruleId, f.Category, f.Severity.ToString(), weight,
                                         name, o.DistinguishedName, o.Sid, ExceptionText(o), fix);
        }
    }

    /// <summary>Findings in work order: severity, then weight, then rule id.</summary>
    public static List<AdFinding> Ordered(IEnumerable<AdFinding> findings) =>
        findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.Weight)
                .ThenBy(f => f.RuleId, StringComparer.Ordinal).ThenBy(f => f.Category, StringComparer.Ordinal).ToList();

    /// <summary>The flat work list: one row per rule × object (S22-5).</summary>
    public static List<WorklistRow> Build(IEnumerable<AdFinding> findings) =>
        Ordered(findings).SelectMany(RowsOf).ToList();

    /// <summary>Writes the work list as CSV (SafeField per cell) and its .meta.txt. Returns the data row count.</summary>
    public static int ExportCsv(IEnumerable<AdFinding> findings, string path, ExportContext ctx, string? delimiter = null)
    {
        var rows = Build(findings);
        int n = new CsvExporter(delimiter).ExportTable(rows, Columns, path);
        ExportMetadata.WriteFor(path, ctx, "Work list", "", n);
        return n;
    }

    /// <summary>Writes the work list as XLSX ("Data" + "Info") and its .meta.txt. Returns the data row count.</summary>
    public static int ExportXlsx(IEnumerable<AdFinding> findings, string path, ExportContext ctx)
    {
        var rows = Build(findings);
        var info = ExportMetadata.Fields(ctx, "Work list", "", rows.Count, Path.GetFileName(path), DateTime.UtcNow, ExportMetadata.AppVersion);
        int n = new XlsxExporter().Export(rows, Columns, path, info);
        ExportMetadata.WriteFor(path, ctx, "Work list", "", n);
        return n;
    }

    // ── S22-6: full workbook ─────────────────────────────────────────────────

    /// <summary>One line of the "Summary" sheet.</summary>
    public sealed record SummaryRow(string RuleId, string Finding, string Title, string Severity, int Weight,
                                    int Objects, int Excepted, int PointsIfFixed, string Sheet);

    private static readonly IReadOnlyList<ITableColumn> SummaryColumns =
    [
        new TableColumn<SummaryRow>("RuleId",          r => r.RuleId),
        new TableColumn<SummaryRow>("Finding",         r => r.Finding),
        new TableColumn<SummaryRow>("Title",           r => r.Title),
        new TableColumn<SummaryRow>("Severity",        r => r.Severity),
        new TableColumn<SummaryRow>("Weight",          r => r.Weight),
        new TableColumn<SummaryRow>("Objects",         r => r.Objects),
        new TableColumn<SummaryRow>("Excepted",        r => r.Excepted),
        new TableColumn<SummaryRow>("Points if fixed", r => r.PointsIfFixed),
        new TableColumn<SummaryRow>("Sheet",           r => r.Sheet),
    ];

    /// <summary>
    /// The workbook's sheets (pure, testable): "Summary", one sheet per finding with ≥ 1 object, "Info".
    /// Sheet names are made Excel-safe and unique by <see cref="XlsxExporter.SheetNames"/>.
    /// </summary>
    public static List<XlsxSheet> BuildWorkbook(IEnumerable<AdFinding> findings, ScoreResult? score,
                                                IReadOnlyList<(string Key, string Value)> info)
    {
        var ordered = Ordered(findings);
        var withObjects = ordered.Where(f => f.Objects.Count > 0).ToList();
        // Summary and Info are fixed; finding sheets get the remaining names (reserved names are skipped).
        var names = XlsxExporter.SheetNames(
            [SummarySheet, InfoSheet, .. withObjects.Select(f => string.IsNullOrEmpty(f.RuleId) ? f.Category : $"{f.RuleId} {f.Category}")]);
        var sheetOf = new Dictionary<AdFinding, string>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < withObjects.Count; i++) sheetOf[withObjects[i]] = names[i + 2];

        var summary = ordered.Select(f => new SummaryRow(
            f.RuleId, f.Category, f.Title, f.Severity.ToString(), f.Weight,
            f.Objects.Count, f.Objects.Count(o => o.IsExcepted), score?.PointsFor(f.Category) ?? 0,
            sheetOf.TryGetValue(f, out var s) ? s : "")).ToList();

        var sheets = new List<XlsxSheet> { new(names[0], summary, SummaryColumns) };
        sheets.AddRange(withObjects.Select(f => new XlsxSheet(sheetOf[f], RowsOf(f).ToList(), Columns)));
        sheets.Add(XlsxSheet.Info(names[1], info));
        return sheets;
    }

    /// <summary>Writes the full workbook and its .meta.txt. Returns the number of sheets.</summary>
    public static int ExportWorkbook(IEnumerable<AdFinding> findings, ScoreResult? score, string path, ExportContext ctx)
    {
        var list = findings.ToList();
        int rows = list.Sum(f => Math.Max(1, f.Objects.Count));
        var info = ExportMetadata.Fields(ctx, "Workbook", "", rows, Path.GetFileName(path), DateTime.UtcNow, ExportMetadata.AppVersion);
        info.Add(("Score", score?.Display ?? "n/a"));
        info.Add(("Score formula version", ScoreModel.FormulaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var sheets = BuildWorkbook(list, score, info);
        new XlsxExporter().ExportSheets(sheets, path);
        ExportMetadata.WriteFor(path, ctx, "Workbook", "", rows);
        return sheets.Count;
    }
}
