namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>Which report the user picked in the export dialog (S18-1).</summary>
public enum ReportKind { Summary, Full }

/// <param name="FilterScope">Set for the findings-tab PDF (S21-4): describes the filter the user applied
/// ("severity=High · 3 of 12 findings"). The report then shows only the given findings and leaves out the
/// score delta, the changes table and the user lists (inactive users, passwords, admins) — those belong
/// to the whole scan, not to a filtered selection. Null = the dashboard report of the whole scan.</param>
public record ReportOptions(ReportKind Kind, bool RedactUserNames, string? FilterScope = null);

/// <summary>A simple table for the PDF. Six or more columns render on a landscape page (S18-2).</summary>
/// <param name="Weights">Relative column widths (Paket 3, F-02). Null = the renderer weights the columns by
/// their content. Cells always wrap onto several lines — nothing is cut off.</param>
public record ReportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, int HiddenRows, string HiddenHint,
                          IReadOnlyList<double>? Weights = null);

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

/// <summary>One line of the scan-scope box on page 1 (Paket 3, V-11), e.g. ("Objects", "1,200 users · …").</summary>
public record ReportScopeLine(string Label, string Value);

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
    bool Redacted,
    // page 1 for a reader who reads nothing else (Paket 3, V-11 / F-05..F-07)
    bool ScoreAvailable,                                // false → ScoreText is the neutral "Score not available — …"
    string ScoreLegend,                                 // what Good/Review/Critical mean
    IReadOnlyList<string> SummarySentences,             // 2–3 generated sentences: situation · largest risk · next step
    IReadOnlyList<ReportScopeLine> Scope,               // domain, DC, scan time, objects, checks
    string SeverityCountsLabel,                         // what SeverityCounts count (same definition as the matrix)
    string? ExceptedNote,                               // "+2 fully excepted findings not counted"
    IReadOnlyList<string> FailedChecksShort,            // failed checks for page 1, max. 5 + "n more"
    string? RedactionNote,                              // set by the redactor: what was and was not redacted
    // report design (cover, executive summary, methodology) — built here, so the renderer only draws
    string CoverTitle = "",                             // "Security Health Report" / "Management Summary" / "Findings Report"
    string Classification = "",                         // "CONFIDENTIAL" marker on the cover and the running header
    string Disclaimer = "",                             // read-only statement at the bottom of the cover
    IReadOnlyList<string>? KeyFacts = null,             // 1–4 plain statements below the summary sentences
    IReadOnlyList<ReportTrendPoint>? ScoreTrend = null, // comparable score history (same formula), oldest first
    IReadOnlyList<string>? Methodology = null);         // "How the score works" (appendix)

/// <summary>One point of the score trend sparkline: date (yyyy-MM-dd) and score.</summary>
public record ReportTrendPoint(string Date, int Score);
