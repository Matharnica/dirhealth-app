using System.Globalization;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>Everything the report is built from (all already in memory after a scan).</summary>
public record ReportInput(
    string Domain,
    IReadOnlyList<AdFinding> Findings,          // exception states already applied
    ScoreResult? Score, int StoredScore,
    IReadOnlyList<CheckResult> Checks,
    IReadOnlyList<ObjectException> Exceptions,
    ScanDiff? Diff, int? ScoreDelta,
    IReadOnlyList<AdUser> InactiveUsers,
    IReadOnlyList<AdUser> ExpiringPasswords,
    IReadOnlyList<string> DomainAdmins,
    DateTime CreatedLocal, string Version,
    // scan scope for page 1 (Paket 3, V-11) — optional: unknown values are left out, never guessed
    ScanTotals? Totals = null, string DomainController = "", string ScanTime = "",
    // score history for the trend line of the executive summary — optional, oldest first
    IReadOnlyList<ScoreEntry>? History = null);

/// <summary>Builds the report model (S18-1..S18-4). Pure: no PDF, no LDAP, no clock.</summary>
public static class ReportBuilder
{
    /// <summary>Object lists in the PDF stop here; the rest is in the CSV (S18-2).</summary>
    public const int ObjectCap = 25;
    public const int FixFirstCount = 10;

    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public static string ScoreLabel(int score) => score >= 80 ? "Good" : score >= 60 ? "Review" : "Critical";

    public static ReportTable Capped(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, int cap = ObjectCap)
    {
        var all = rows.ToList();
        int hidden = Math.Max(0, all.Count - cap);
        return new ReportTable(headers, all.Take(cap).ToList(), hidden,
            hidden > 0 ? $"{hidden} more — see the CSV export of this finding" : "");
    }

    /// <summary>Readable names of the RuleId areas (S21-1). An area missing here shows its raw prefix.</summary>
    private static readonly IReadOnlyDictionary<string, string> AreaLabels = new Dictionary<string, string>
    {
        ["DH-USR"] = "Users",            ["DH-CMP"] = "Computers",       ["DH-GRP"] = "Groups",
        ["DH-PRV"] = "Privileged access", ["DH-KRB"] = "Kerberos",       ["DH-POL"] = "Password policy",
        ["DH-DOM"] = "Domain settings",  ["DH-TRU"] = "Trusts",
    };

    /// <summary>"DH-KRB-001" → "DH-KRB"; anything else → "Other".</summary>
    public static string AreaOf(string ruleId)
    {
        int cut = ruleId.LastIndexOf('-');
        return ruleId.StartsWith("DH-", StringComparison.Ordinal) && cut > 3 ? ruleId[..cut] : "Other";
    }

    private static readonly FindingSeverity[] MatrixSeverities =
        [FindingSeverity.Critical, FindingSeverity.High, FindingSeverity.Medium, FindingSeverity.Low];

    /// <summary>
    /// Risk matrix (S21-1): rows = every area of the catalog (plus "Other" only if needed), columns =
    /// severities, cells = open findings. Pure counting — the score formula is not touched.
    /// </summary>
    public static ReportRiskMatrix RiskMatrix(IEnumerable<AdFinding> findings, IEnumerable<FindingRule> catalog)
    {
        var open  = findings.Where(f => !f.IsAcknowledged).ToList();     // all objects excepted = not open
        var areas = catalog.Select(r => AreaOf(r.RuleId))
            .Concat(open.Select(f => AreaOf(f.RuleId)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a == "Other" ? 1 : 0).ThenBy(a => a, StringComparer.Ordinal)
            .ToList();
        var rows = areas.Select(a => new ReportRiskRow(a, AreaLabels.TryGetValue(a, out var l) ? l : a,
            MatrixSeverities.Select(sev => open.Count(f => f.Severity == sev && AreaOf(f.RuleId) == a)).ToList())).ToList();
        return new ReportRiskMatrix(MatrixSeverities.Select(x => x.ToString()).ToList(), rows);
    }

    /// <summary>
    /// "What was checked" (S21-3): every catalog rule with its status from this scan. A rule without a
    /// check result (e.g. a scan made by an older DirHealth) is reported as Failed, never as Passed.
    /// </summary>
    public static IReadOnlyList<ReportCheckedRule> CheckedRules(IReadOnlyList<CheckResult> checks, IReadOnlyList<AdFinding> findings, IEnumerable<FindingRule> catalog)
    {
        var byRule = checks.GroupBy(c => c.RuleId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return catalog.OrderBy(r => r.RuleId, StringComparer.Ordinal).Select(r =>
        {
            if (byRule.TryGetValue(r.RuleId, out var c)) return new ReportCheckedRule(r.RuleId, r.Category, c.StatusLabel, c.Reason);
            if (findings.Any(f => f.Category == r.Category))
                return new ReportCheckedRule(r.RuleId, r.Category, Label(CheckStatus.Found), "");
            return new ReportCheckedRule(r.RuleId, r.Category, Label(CheckStatus.Failed), "No result in this scan — run a new scan.");
        }).ToList();
    }

    /// <summary>The app's own status wording (single source: <see cref="CheckResult.StatusLabel"/>).</summary>
    public static string Label(CheckStatus s) => new CheckResult("", "", s, "", 0).StatusLabel;

    /// <summary>What Good/Review/Critical mean — printed next to the score (V-11). Same thresholds as <see cref="ScoreLabel"/>.</summary>
    public const string ScoreLegendText = "Good ≥ 80 · Review 60–79 · Critical < 60 — higher is better";

    /// <summary>Printed on page 1 of a redacted report (F-04): what the redaction covers and what it cannot.</summary>
    public const string RedactionNoteText =
        "Names redacted: accounts are replaced by aliases (user-001, account-001 …); built-in accounts and groups " +
        "(krbtgt, Guest, Administrator, Domain Admins …) and computers stay readable. Exception reasons are free text: " +
        "known account names in them are replaced, other names cannot be detected.";

    /// <summary>At most this many failed checks are listed on page 1 (F-07); the rest is counted.</summary>
    public const int FailedChecksOnPageOne = 5;

    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    public static ReportModel Build(ReportInput i, ReportOptions o)
    {
        bool scoped = o.FilterScope is not null;   // findings-tab PDF (S21-4)
        var score = i.Score;
        bool available = score?.Available ?? true;
        int  scoreNum  = score?.Score ?? i.StoredScore;
        int  failedForScore = score is null ? 0 : score.TotalRules - score.EvaluatedRules;
        // F-05: an unavailable score is not a red "n/a / 100 · Critical" — it is neutral and says why
        string scoreText = !available
            ? (failedForScore > 0 ? $"Score not available — {failedForScore} of {ReportText.Count(score!.TotalRules, "check")} failed"
                                  : "Score not available — too few checks ran")
            : score?.Display ?? i.StoredScore.ToString(CultureInfo.InvariantCulture);

        // F-06: one definition on page 1 — open findings (rules) per severity, exactly what the risk matrix
        // counts; fully excepted findings are named separately, not mixed in.
        var open = i.Findings.Where(f => !f.IsAcknowledged).ToList();
        int fullyExcepted = i.Findings.Count - open.Count;
        var bySeverity = MatrixSeverities.Select(s => (s.ToString(), open.Count(f => f.Severity == s))).ToList();

        // Category overview (V-12: readable title instead of the internal key): every rule with a deduction,
        // plus open findings without one (e.g. informational)
        var overviewRows = i.Findings
            .OrderByDescending(f => score?.PointsFor(f.Category) ?? 0).ThenByDescending(f => f.Severity)
            .Select(f => (IReadOnlyList<string>)new[]
            {
                f.RuleId, f.Title, f.Severity.ToString(),
                f.Objects.Count(x => !x.IsExcepted).ToString(CultureInfo.InvariantCulture),
                (score?.PointsFor(f.Category) ?? 0) is var p && p > 0 ? $"+{p}" : "—",
            })
            .ToList();
        var overview = new ReportTable(["Rule", "Finding", "Severity", "Open objects", "Points if fixed"], overviewRows, 0, "",
            Weights: [0.13, 0.50, 0.11, 0.12, 0.14]);

        // "Fix first": largest score gain first (effort per rule is not modelled yet — S17-5 deferred).
        // Only rules among the given findings — the findings-tab PDF lists its filter selection only (S21-4).
        var shown = i.Findings.Select(f => f.Category).ToHashSet(StringComparer.Ordinal);
        var fixFirst = new ReportTable(["#", "Finding", "Points if fixed", "Severity"],
            (score?.Rules ?? []).Where(r => r.PointsIfFixed > 0 && shown.Contains(r.Category)).Take(FixFirstCount)
                .Select((r, n) => (IReadOnlyList<string>)new[]
                {
                    (n + 1).ToString(CultureInfo.InvariantCulture),
                    i.Findings.FirstOrDefault(f => f.Category == r.Category)?.Title ?? r.Category,
                    $"+{r.PointsIfFixed}",
                    i.Findings.FirstOrDefault(f => f.Category == r.Category)?.Severity.ToString() ?? "",
                }).ToList(), 0, "",
            Weights: [0.06, 0.64, 0.14, 0.16]);   // F-02: the title gets the width, "#" does not

        var changes = new ReportTable(["Change", "Finding"],
            (i.Diff is null || scoped ? [] :
                i.Diff.NewFindings.Select(f => (IReadOnlyList<string>)new[] { "New", f.Title })
                .Concat(i.Diff.ResolvedFindings.Select(f => (IReadOnlyList<string>)new[] { "Resolved", f.Title }))
                .Concat(i.Diff.ChangedFindings.Select(c => (IReadOnlyList<string>)new[] { c.Delta > 0 ? $"+{c.Delta}" : $"{c.Delta}", c.Finding.Title })))
            .ToList(), 0, "",
            Weights: [0.18, 0.82]);

        string? delta = scoped ? null
                      : i.Diff is { ScoreComparable: false } ? "Score model changed since the last scan — no comparison."
                      : i.ScoreDelta is int d && d != 0 ? (d > 0 ? $"▲ +{d} since the last scan" : $"▼ {d} since the last scan")
                      : i.ScoreDelta == 0 ? "Unchanged since the last scan" : null;

        var findings = o.Kind == ReportKind.Summary ? [] : i.Findings
            .OrderByDescending(f => f.Severity).ThenByDescending(f => score?.PointsFor(f.Category) ?? 0)
            .Select(f =>
            {
                var rule = f.Rule;
                var objects = Capped(["Object", "Type", "Detail", "Exception"],
                    f.Objects.Select(x => (IReadOnlyList<string>)new[] { x.Name, x.Type.ToString(), x.Detail, x.ExceptionLabel })) with
                    { Weights = [0.30, 0.12, 0.33, 0.25] };
                return new ReportFinding(f.RuleId, f.Severity.ToString(), f.Title, f.Description,
                    rule?.What ?? "", rule?.Why ?? "", rule?.HowToFix ?? f.Remediation,
                    rule?.Fix ?? "", rule?.FixRisk ?? "", rule?.Revert ?? "",
                    score?.PointsFor(f.Category) ?? 0, objects,
                    (rule?.References ?? []).Select(r => new ReportReference(r.Label, r.Url)).ToList());   // S21-2
            }).ToList();

        var nowUtcForState = i.CreatedLocal.ToUniversalTime();
        var exceptions = o.Kind == ReportKind.Summary ? [] : i.Exceptions
            .Where(e => e.IsActive(nowUtcForState))
            .Where(e => !scoped || i.Findings.Any(f => f.RuleId == e.RuleId))     // S21-4: only the selection
            .OrderBy(e => e.ExpiresUtc)
            .Select(e => new ReportException(e.RuleId, e.ObjectName, e.Reason, D(e.ExpiresUtc), e.CreatedBy))
            .ToList();

        var failed = i.Checks.Where(c => c.Status == CheckStatus.Failed)
            .Select(c => new ReportFailedCheck(c.RuleId, c.Category, c.Reason)).ToList();

        var appendix = new List<(string, ReportTable)>();
        if (o.Kind == ReportKind.Full && !scoped)   // user lists belong to the whole scan, not a filter (S21-4)
        {
            appendix.Add(("Inactive users", Capped(["Name", "sAMAccountName", "Last logon (±14 d)", "OU"],
                i.InactiveUsers.Select(u => (IReadOnlyList<string>)new[] { u.DisplayName, u.SamAccountName, D(u.LastLogon), u.Ou }), 200)));
            appendix.Add(("Expiring passwords", Capped(["Name", "sAMAccountName", "Expires", "Days"],
                i.ExpiringPasswords.Select(u => (IReadOnlyList<string>)new[] { u.DisplayName, u.SamAccountName, D(u.PasswordExpires), u.DaysUntilPasswordExpiry?.ToString(CultureInfo.InvariantCulture) ?? "" }), 200)));
            appendix.Add(("Domain Admins", Capped(["Account"], i.DomainAdmins.Select(a => (IReadOnlyList<string>)new[] { a }), 200)));
        }

        // Every catalog rule with its status: the "What was checked" appendix (Full) and the check count of the
        // scope box (both kinds) come from one list, so page 1 and the appendix cannot disagree. Page 1 lists the
        // checks that ran and failed — the same ones the score leaves out (F-07); rules without any result
        // (scan of an older version, findings tab without scan context) are counted separately in the scope box.
        var checkedRules = CheckedRules(i.Checks, i.Findings, FindingCatalog.All);
        var failedShort  = failed.Take(FailedChecksOnPageOne)
            .Select(c => c.Reason.Length > 0 ? $"{c.RuleId} {c.Category}: {c.Reason}" : $"{c.RuleId} {c.Category}")
            .ToList();
        if (failed.Count > FailedChecksOnPageOne)
            failedShort.Add($"{failed.Count - FailedChecksOnPageOne} more — see \"Checks that could not run\" in the full report");

        var model = new ReportModel(o.Kind, i.Domain, ReportText.Time(i.CreatedLocal), i.Version,
            scoreText, "Higher is better (0–100)", available ? ScoreLabel(scoreNum) : "", scoreNum,
            score?.CoverageText ?? "", available && score is { IsUpperBound: true } ? "Upper bound — some checks could not run." : null,
            delta, bySeverity, overview, fixFirst, changes,
            RiskMatrix(i.Findings, FindingCatalog.All), o.FilterScope,
            findings, exceptions, failed, appendix,
            o.Kind == ReportKind.Full ? checkedRules : [],
            Redacted: false,
            ScoreAvailable: available,
            ScoreLegend: ScoreLegendText,
            SummarySentences: SummarySentences(available, scoreNum, score, open, fullyExcepted, failed.Count, fixFirst, scoped),
            Scope: ScopeLines(i, checkedRules),
            SeverityCountsLabel: "Open findings by severity (rules — same count as the risk matrix)",
            ExceptedNote: fullyExcepted > 0 ? $"+{ReportText.Count(fullyExcepted, "fully excepted finding")}, not counted above" : null,
            FailedChecksShort: failedShort,
            RedactionNote: null,
            CoverTitle: scoped ? "Findings Report" : o.Kind == ReportKind.Summary ? "Management Summary" : "Security Health Report",
            Classification: ClassificationText,
            Disclaimer: DisclaimerText,
            KeyFacts: KeyFacts(i, open, scoped, checkedRules),
            ScoreTrend: scoped ? [] : Trend(i.History),
            Methodology: o.Kind == ReportKind.Full ? MethodologyLines : []);

        return o.RedactUserNames ? RedactorFor(i).Apply(model) : model;
    }

    public const string ClassificationText = "CONFIDENTIAL";

    /// <summary>Bottom of the cover. True by design: DirHealth only reads (ReadOnlyGuardTests).</summary>
    public const string DisclaimerText =
        "DirHealth only reads Active Directory and changed nothing during this assessment. The report shows the state " +
        "at scan time. Fix scripts are suggestions: review them and run them yourself.";

    /// <summary>At most this many points of score history are drawn as the trend line.</summary>
    public const int TrendPoints = 12;

    /// <summary>
    /// "How the score works" (appendix of the full report) — describes <see cref="ScoreModel"/> (ADR 0002)
    /// with its real constants, so the text cannot drift from the formula's numbers.
    /// </summary>
    public static readonly IReadOnlyList<string> MethodologyLines =
    [
        $"The score starts at 100. Every rule that finds something deducts {ScoreModel.PointsPerWeight.ToString(CultureInfo.InvariantCulture)} × " +
        "its weight × how present the problem is (0 to 100 %). The score never drops below 0.",
        "How present a problem is depends on the rule: most rules count affected objects up to a saturation point, some measure " +
        "the share of all users, groups or computers, and setting rules count as fully present for a high or critical finding " +
        "and half present otherwise.",
        "Objects with an active exception do not count. They stay listed in the report, with the exception next to them.",
        "A check that could not run (permissions, timeouts) is left out: it can neither lower nor raise the score, so the score " +
        $"is shown as an upper bound (≤). When less than {ScoreModel.MinCoverage.ToString("P0", CultureInfo.InvariantCulture)} of the " +
        "catalog weight could be evaluated, no score is shown.",
        "\"Points if fixed\" splits the gap to 100 across the rules: the points of all rules add up exactly to 100 minus the score.",
        "Severity (Critical, High, Medium, Low) describes the impact of a single finding; the weight that drives the score is set per rule.",
        $"{ScoreLegendText}. Score model version {ScoreModel.FormulaVersion}; scores of different model versions are not compared.",
    ];

    /// <summary>
    /// 1–4 plain statements for the executive summary, from counts only (no names): the most affected area,
    /// open and excepted objects, changes since the last scan, how many checks ran.
    /// </summary>
    public static IReadOnlyList<string> KeyFacts(ReportInput i, IReadOnlyList<AdFinding> open, bool scoped, IReadOnlyList<ReportCheckedRule> rules)
    {
        var facts = new List<string>();
        var top = open.GroupBy(f => AreaOf(f.RuleId))
            .Select(g => (Area: g.Key, Count: g.Count(), Severe: g.Count(f => f.Severity >= FindingSeverity.High)))
            .OrderByDescending(a => a.Count).ThenByDescending(a => a.Severe).ThenBy(a => a.Area, StringComparer.Ordinal)
            .FirstOrDefault();
        if (top.Count > 1)
            facts.Add($"{(AreaLabels.TryGetValue(top.Area, out var l) ? l : top.Area)} is the most affected area: {top.Count} open findings" +
                      (top.Severe > 0 ? $", {top.Severe} of them critical or high." : "."));

        int objects  = open.Sum(f => f.Objects.Count(o => !o.IsExcepted));
        int excepted = i.Findings.Sum(f => f.Objects.Count(o => o.IsExcepted));
        if (objects > 0)
            facts.Add($"{ReportText.Count(objects, "affected object")} {ReportText.Noun(objects, "is", "are")} open" +
                      (excepted > 0 ? $"; {N(excepted)} more {(excepted == 1 ? "is" : "are")} covered by an active exception." : "."));

        if (!scoped && i.Diff is { } d && (d.NewFindings.Count + d.ResolvedFindings.Count + d.ChangedFindings.Count) > 0)
            facts.Add($"Since the last scan: {d.NewFindings.Count} new, {d.ResolvedFindings.Count} resolved and " +
                      $"{ReportText.Count(d.ChangedFindings.Count, "changed finding")}.");

        var ran = i.Checks.Select(c => c.RuleId).ToHashSet(StringComparer.Ordinal);
        if (!scoped && ran.Count > 0)
        {
            int completed = rules.Count(r => ran.Contains(r.RuleId) && r.Status != Label(CheckStatus.Failed));
            int passed    = rules.Count(r => r.Status == Label(CheckStatus.Passed));
            facts.Add($"{N(completed)} of {N(rules.Count)} checks ran; {N(passed)} of them passed without a finding.");
        }
        return facts;
    }

    /// <summary>Score trend: the latest run of entries with the same formula version (comparable), oldest first.</summary>
    public static IReadOnlyList<ReportTrendPoint> Trend(IReadOnlyList<ScoreEntry>? history)
    {
        if (history is null || history.Count < 2) return [];
        int version = history[^1].FormulaVersion;
        var run = history.Reverse().TakeWhile(e => e.FormulaVersion == version).Take(TrendPoints).Reverse()
            .Select(e => new ReportTrendPoint(e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.Score))
            .ToList();
        return run.Count >= 2 ? run : [];
    }

    /// <summary>
    /// The 2–3 sentences at the top of page 1 (V-11), generated from the model data only: the situation,
    /// the largest single risk, the next step. No sentence claims more than the data shows — with failed
    /// checks the first sentence says the picture is incomplete. Titles are scanner text (no person names).
    /// </summary>
    public static IReadOnlyList<string> SummarySentences(bool available, int score, ScoreResult? result,
        IReadOnlyList<AdFinding> open, int fullyExcepted, int failedChecks, ReportTable fixFirst, bool scoped)
    {
        var s = new List<string>();
        int crit = open.Count(f => f.Severity == FindingSeverity.Critical);
        int high = open.Count(f => f.Severity == FindingSeverity.High);
        string what = scoped ? "The selection contains" : "The scan found";
        string severe = crit + high == 0 ? "none of them critical or high"
                      : crit > 0 && high > 0 ? $"{crit} critical and {high} high"
                      : crit > 0 ? $"{crit} critical" : $"{high} high";

        if (!available)
            s.Add($"No score could be computed because {ReportText.Count(failedChecks, "check")} failed, so the picture below is incomplete. " +
                  $"{what} {ReportText.Count(open.Count, "open finding")}, {severe}.");
        else if (open.Count == 0)
            s.Add($"The domain scores {score}/100 ({ScoreLabel(score)}). {what} no open findings.");
        else
            s.Add($"The domain scores {(result is { IsUpperBound: true } ? "at most " : "")}{score}/100 ({ScoreLabel(score)}). " +
                  $"{what} {ReportText.Count(open.Count, "open finding")}, {severe}.");

        if (fixFirst.Rows.Count > 0)
            s.Add($"The largest single improvement is \"{fixFirst.Rows[0][1]}\" ({fixFirst.Rows[0][2]} {(fixFirst.Rows[0][2] == "+1" ? "point" : "points")} if fixed).");
        else if (open.OrderByDescending(f => f.Severity).FirstOrDefault() is { } top)
            s.Add($"The most severe open finding is \"{top.Title}\" ({top.Severity}).");

        if (failedChecks > 0)
            s.Add($"Next step: get the {ReportText.Count(failedChecks, "failed check")} to run (permissions, connectivity) and scan again; until then the score is an upper bound.");
        else if (open.Count > 0)
            s.Add("Next step: work through the \"Fix first\" list below from the top" +
                  (fullyExcepted > 0 ? $"; {ReportText.Count(fullyExcepted, "fully excepted finding")} {ReportText.Noun(fullyExcepted, "is", "are")} tracked separately." : "."));
        return s;
    }

    /// <summary>Scan-scope box of page 1 (V-11). Unknown values are left out instead of printed as "unknown".</summary>
    public static IReadOnlyList<ReportScopeLine> ScopeLines(ReportInput i, IReadOnlyList<ReportCheckedRule> rules)
    {
        var ran = i.Checks.Select(c => c.RuleId).ToHashSet(StringComparer.Ordinal);
        var lines = new List<ReportScopeLine> { new("Domain", i.Domain.Length > 0 ? i.Domain : "—") };
        if (!string.IsNullOrWhiteSpace(i.DomainController) && !string.Equals(i.DomainController, "unknown", StringComparison.OrdinalIgnoreCase))
            lines.Add(new("Domain controller", i.DomainController));
        if (!string.IsNullOrWhiteSpace(i.ScanTime) && i.ScanTime != "Never")
            lines.Add(new("Scan time", ReportText.ScanTime(i.ScanTime)));
        lines.Add(new("Report created", ReportText.Time(i.CreatedLocal)));
        // V-20: printed once, so the local times can be matched with the UTC time of the .meta.txt
        lines.Add(new("Time zone", $"{ReportText.Zone(i.CreatedLocal)} (local time)"));
        if (i.Totals is { } t && t.Users + t.Groups + t.Computers > 0)
            lines.Add(new("Objects read", $"{N(t.Users)} users · {N(t.Groups)} groups · {N(t.Computers)} computers"));
        int Of(CheckStatus st) => rules.Count(r => r.Status == Label(st) && (st != CheckStatus.Failed || ran.Contains(r.RuleId)));
        int noResult = rules.Count(r => r.Status == Label(CheckStatus.Failed) && !ran.Contains(r.RuleId));
        lines.Add(new("Checks", $"{rules.Count} rules — {Of(CheckStatus.Passed)} passed · {Of(CheckStatus.Found)} with findings · " +
                                $"{Of(CheckStatus.Failed)} failed · {Of(CheckStatus.NotApplicable)} not applicable" +
                                (noResult > 0 ? $" · {noResult} without a result in this scan" : "")));
        return lines;
    }


    /// <summary>
    /// Every account identifier the input knows, typed (F-03/F-04): users (display name, SAM, mail, DN) and
    /// user objects of findings; exception objects with the type of the object they point at; exception
    /// creators ("DOMAIN\sam" and "sam"); Domain Admins members. Built-in accounts and groups are skipped
    /// (<see cref="Redactor.IsWellKnown"/>, by SID where known). Computers, groups and policies are not
    /// people and stay readable (Kernweg K06).
    /// </summary>
    public static Redactor RedactorFor(ReportInput i)
    {
        var ids = new List<(RedactKind, IReadOnlyCollection<string>)>();
        foreach (var u in i.InactiveUsers.Concat(i.ExpiringPasswords))
            if (!Redactor.IsWellKnown(u.Sid, u.SamAccountName))
                ids.Add((RedactKind.User, new[] { u.DisplayName, u.SamAccountName, u.Email, u.DistinguishedName }));

        var objects = i.Findings.SelectMany(f => f.Objects).ToList();
        foreach (var o in objects.Where(o => o.Type is AffectedObjectType.User or AffectedObjectType.Unknown))
            if (!Redactor.IsWellKnown(o.Sid, o.Name))
                ids.Add((o.Type == AffectedObjectType.User ? RedactKind.User : RedactKind.Account, new[] { o.Name, o.DistinguishedName }));

        foreach (var e in i.Exceptions)
        {
            var target = objects.FirstOrDefault(o => o.Sid.Length > 0 && string.Equals(o.Sid, e.ObjectKey, StringComparison.OrdinalIgnoreCase))
                      ?? objects.FirstOrDefault(o => string.Equals(o.Name, e.ObjectName, StringComparison.OrdinalIgnoreCase));
            bool sidKeyed = e.ObjectKey.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase);
            if (Redactor.IsWellKnown(target?.Sid is { Length: > 0 } ts ? ts : sidKeyed ? e.ObjectKey : null, e.ObjectName)) continue;
            RedactKind? kind = target?.Type switch
            {
                AffectedObjectType.User => RedactKind.User,
                AffectedObjectType.Computer or AffectedObjectType.Group
                    or AffectedObjectType.PasswordPolicy or AffectedObjectType.DomainSetting => null,   // not people
                _ => sidKeyed ? RedactKind.Account : null,   // an account of unknown type: rather too much than too little
            };
            if (kind is { } k) ids.Add((k, new[] { e.ObjectName }));
        }

        // F-04: the admin who created an exception is a person — "DOMAIN\sam" and the bare "sam"
        foreach (var e in i.Exceptions.Where(e => !string.IsNullOrWhiteSpace(e.CreatedBy)))
        {
            int cut = e.CreatedBy.LastIndexOf('\\');
            ids.Add((RedactKind.User, cut >= 0 ? new[] { e.CreatedBy, e.CreatedBy[(cut + 1)..] } : new[] { e.CreatedBy }));
        }

        foreach (var a in i.DomainAdmins.Where(a => !Redactor.IsWellKnownName(a)))
            ids.Add((RedactKind.Account, new[] { a }));
        return new Redactor(ids);
    }
}
