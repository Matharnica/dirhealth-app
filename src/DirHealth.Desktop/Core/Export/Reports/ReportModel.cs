namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>Which report the user picked in the export dialog (S18-1).</summary>
public enum ReportKind { Summary, Full }

/// <param name="FilterScope">Set for the findings-tab PDF (S21-4): describes the filter the user applied
/// ("severity=High · 3 of 12 findings"). The report then shows only the given findings and leaves out the
/// score delta, the changes table and the user lists (inactive users, passwords, admins) — those belong
/// to the whole scan, not to a filtered selection. Null = the dashboard report of the whole scan.</param>
public record ReportOptions(ReportKind Kind, bool RedactUserNames, string? FilterScope = null);

/// <summary>A simple table for the PDF. Six or more columns render on a landscape page (S18-2).</summary>
public record ReportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, int HiddenRows, string HiddenHint);

public record ReportFinding(
    string RuleId, string Severity, string Title, string Description,
    string What, string Why, string HowToFix,
    string Fix, string FixRisk, string Revert,
    int PointsIfFixed, ReportTable Objects,
    IReadOnlyList<ReportReference> References);         // S21-2: catalog references, printed as links

/// <summary>A catalog reference of a rule (S21-2). Catalog text only — never contains a user name.</summary>
public record ReportReference(string Label, string Url);

/// <summary>
/// Risk matrix (S21-1): open findings per area (RuleId prefix, e.g. DH-KRB) and severity. Every area of
/// the catalog is a row, even when empty, so a reader sees where nothing was found. Counts findings
/// (rules), not objects; excepted findings are not open and do not count. The score formula is not involved.
/// </summary>
public record ReportRiskMatrix(IReadOnlyList<string> Severities, IReadOnlyList<ReportRiskRow> Rows)
{
    /// <summary>Cells with at least one open finding.</summary>
    public int OccupiedCells => Rows.Sum(r => r.Counts.Count(c => c > 0));
}

/// <param name="Counts">One count per entry of <see cref="ReportRiskMatrix.Severities"/>, same order.</param>
public record ReportRiskRow(string Area, string Label, IReadOnlyList<int> Counts)
{
    public int Total => Counts.Sum();
}

/// <summary>One catalog rule in the "What was checked" appendix (S21-3) with its scan status.</summary>
public record ReportCheckedRule(string RuleId, string Category, string Status, string Reason);

public record ReportException(string RuleId, string Object, string Reason, string Expires, string CreatedBy);

public record ReportFailedCheck(string RuleId, string Category, string Reason);

/// <summary>
/// Everything a report shows, as plain strings (S18). Built by <see cref="ReportBuilder"/>, redacted by
/// <see cref="Redactor"/>, drawn by the PDF renderer — the renderer adds no data of its own, so what is
/// in the model is exactly what can end up in the PDF.
/// </summary>
public record ReportModel(
    ReportKind Kind,
    string Domain, string CreatedLocal, string ToolVersion,
    // summary (S18-1)
    string ScoreText, string ScoreDirection, string ScoreLabel, int ScoreForColour, string Coverage, string? ScoreNote,
    string? DeltaText,
    IReadOnlyList<(string Severity, int Count)> SeverityCounts,
    ReportTable CategoryOverview,
    ReportTable FixFirst,
    ReportTable Changes,                                // new / resolved / changed since the last scan (S11-3)
    ReportRiskMatrix RiskMatrix,                        // S21-1, Summary and Full
    string? FilterScope,                                // S21-4: set for the findings-tab PDF
    // technical (Full only)
    IReadOnlyList<ReportFinding> Findings,
    IReadOnlyList<ReportException> Exceptions,          // S18-3
    IReadOnlyList<ReportFailedCheck> FailedChecks,      // S18-3
    IReadOnlyList<(string Title, ReportTable Table)> Appendix,
    IReadOnlyList<ReportCheckedRule> CheckedRules,      // S21-3 "What was checked" (Full only)
    bool Redacted);
