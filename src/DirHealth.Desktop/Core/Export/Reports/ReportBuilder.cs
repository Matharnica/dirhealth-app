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

    public static ReportModel Build(ReportInput i, ReportOptions o)
    {
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

        // "Fix first": largest score gain first (effort per rule is not modelled yet — S17-5 deferred)
        var fixFirst = new ReportTable(["#", "Finding", "Points if fixed"],
            (score?.Rules ?? []).Where(r => r.PointsIfFixed > 0).Take(FixFirstCount)
                .Select((r, n) => (IReadOnlyList<string>)new[]
                {
                    (n + 1).ToString(CultureInfo.InvariantCulture),
                    i.Findings.FirstOrDefault(f => f.Category == r.Category)?.Title ?? r.Category,
                    $"+{r.PointsIfFixed}",
                }).ToList(), 0, "");

        var changes = new ReportTable(["Change", "Finding"],
            (i.Diff is null ? [] :
                i.Diff.NewFindings.Select(f => (IReadOnlyList<string>)new[] { "New", f.Title })
                .Concat(i.Diff.ResolvedFindings.Select(f => (IReadOnlyList<string>)new[] { "Resolved", f.Title }))
                .Concat(i.Diff.ChangedFindings.Select(c => (IReadOnlyList<string>)new[] { c.Delta > 0 ? $"+{c.Delta}" : $"{c.Delta}", c.Finding.Title })))
            .ToList(), 0, "");

        string? delta = i.Diff is { ScoreComparable: false } ? "Score model changed since the last scan — no comparison."
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
                    score?.PointsFor(f.Category) ?? 0, objects);
            }).ToList();

        var nowUtcForState = i.CreatedLocal.ToUniversalTime();
        var exceptions = o.Kind == ReportKind.Summary ? [] : i.Exceptions
            .Where(e => e.IsActive(nowUtcForState))
            .OrderBy(e => e.ExpiresUtc)
            .Select(e => new ReportException(e.RuleId, e.ObjectName, e.Reason, D(e.ExpiresUtc), e.CreatedBy))
            .ToList();

        var failed = i.Checks.Where(c => c.Status == CheckStatus.Failed)
            .Select(c => new ReportFailedCheck(c.RuleId, c.Category, c.Reason)).ToList();

        var appendix = new List<(string, ReportTable)>();
        if (o.Kind == ReportKind.Full)
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
            delta, bySeverity, overview, fixFirst, changes, findings, exceptions, failed, appendix, Redacted: false);

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
