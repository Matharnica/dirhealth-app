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
    DateTime CreatedLocal, string Version);

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

    public static ReportModel Build(ReportInput i, ReportOptions o)
    {
        bool scoped = o.FilterScope is not null;   // findings-tab PDF (S21-4)
        var score = i.Score;
        string scoreText = score?.Display ?? i.StoredScore.ToString(CultureInfo.InvariantCulture);
        int    scoreNum  = score?.Score ?? i.StoredScore;

        var bySeverity = new[] { FindingSeverity.Critical, FindingSeverity.High, FindingSeverity.Medium, FindingSeverity.Low }
            .Select(s => (s.ToString(), i.Findings.Count(f => f.Severity == s))).ToList();

        // Category overview: every rule with a deduction, plus open findings without one (e.g. informational)
        var overviewRows = i.Findings
            .OrderByDescending(f => score?.PointsFor(f.Category) ?? 0).ThenByDescending(f => f.Severity)
            .Select(f => (IReadOnlyList<string>)new[]
            {
                f.RuleId, f.Category, f.Severity.ToString(),
                f.Objects.Count(x => !x.IsExcepted).ToString(CultureInfo.InvariantCulture),
                (score?.PointsFor(f.Category) ?? 0) is var p && p > 0 ? $"+{p}" : "—",
            })
            .ToList();
        var overview = new ReportTable(["Rule", "Category", "Severity", "Open objects", "Points if fixed"], overviewRows, 0, "");

        // "Fix first": largest score gain first (effort per rule is not modelled yet — S17-5 deferred).
        // Only rules among the given findings — the findings-tab PDF lists its filter selection only (S21-4).
        var shown = i.Findings.Select(f => f.Category).ToHashSet(StringComparer.Ordinal);
        var fixFirst = new ReportTable(["#", "Finding", "Points if fixed"],
            (score?.Rules ?? []).Where(r => r.PointsIfFixed > 0 && shown.Contains(r.Category)).Take(FixFirstCount)
                .Select((r, n) => (IReadOnlyList<string>)new[]
                {
                    (n + 1).ToString(CultureInfo.InvariantCulture),
                    i.Findings.FirstOrDefault(f => f.Category == r.Category)?.Title ?? r.Category,
                    $"+{r.PointsIfFixed}",
                }).ToList(), 0, "");

        var changes = new ReportTable(["Change", "Finding"],
            (i.Diff is null || scoped ? [] :
                i.Diff.NewFindings.Select(f => (IReadOnlyList<string>)new[] { "New", f.Title })
                .Concat(i.Diff.ResolvedFindings.Select(f => (IReadOnlyList<string>)new[] { "Resolved", f.Title }))
                .Concat(i.Diff.ChangedFindings.Select(c => (IReadOnlyList<string>)new[] { c.Delta > 0 ? $"+{c.Delta}" : $"{c.Delta}", c.Finding.Title })))
            .ToList(), 0, "");

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
                    f.Objects.Select(x => (IReadOnlyList<string>)new[] { x.Name, x.Type.ToString(), x.Detail, x.ExceptionLabel }));
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

        var model = new ReportModel(o.Kind, i.Domain, i.CreatedLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), i.Version,
            scoreText, "Higher is better (0–100)", ScoreLabel(scoreNum), scoreNum,
            score?.CoverageText ?? "", score is { IsUpperBound: true } ? "Upper bound — some checks could not run." : null,
            delta, bySeverity, overview, fixFirst, changes,
            RiskMatrix(i.Findings, FindingCatalog.All), o.FilterScope,
            findings, exceptions, failed, appendix,
            o.Kind == ReportKind.Full ? CheckedRules(i.Checks, i.Findings, FindingCatalog.All) : [],
            Redacted: false);

        return o.RedactUserNames ? RedactorFor(i).Apply(model) : model;
    }

    /// <summary>All person identifiers the input knows: users (display name, SAM, mail, DN) and
    /// user objects of findings (name, DN). Computers/groups/policies are not people and stay.</summary>
    public static Redactor RedactorFor(ReportInput i)
    {
        var people = new List<IReadOnlyCollection<string>>();
        foreach (var u in i.InactiveUsers.Concat(i.ExpiringPasswords))
            people.Add([u.DisplayName, u.SamAccountName, u.Email, u.DistinguishedName]);
        foreach (var o in i.Findings.SelectMany(f => f.Objects).Where(o => o.Type == AffectedObjectType.User))
            people.Add([o.Name, o.DistinguishedName]);
        foreach (var e in i.Exceptions)
            people.Add([e.ObjectName]);
        foreach (var a in i.DomainAdmins)
            people.Add([a]);
        return new Redactor(people);
    }
}
