namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>Which report the user picked in the export dialog (S18-1).</summary>
public enum ReportKind { Summary, Full }

public record ReportOptions(ReportKind Kind, bool RedactUserNames);

/// <summary>A simple table for the PDF. Six or more columns render on a landscape page (S18-2).</summary>
public record ReportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, int HiddenRows, string HiddenHint);

public record ReportFinding(
    string RuleId, string Severity, string Title, string Description,
    string What, string Why, string HowToFix,
    string Fix, string FixRisk, string Revert,
    int PointsIfFixed, ReportTable Objects);

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
    // technical (Full only)
    IReadOnlyList<ReportFinding> Findings,
    IReadOnlyList<ReportException> Exceptions,          // S18-3
    IReadOnlyList<ReportFailedCheck> FailedChecks,      // S18-3
    IReadOnlyList<(string Title, ReportTable Table)> Appendix,
    bool Redacted);
