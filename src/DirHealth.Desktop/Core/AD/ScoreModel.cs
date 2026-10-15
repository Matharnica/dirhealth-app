using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

/// <summary>Object populations of a scan — the denominators of Share rules (ADR 0002).</summary>
public record ScanTotals(int Users, int Groups, int Computers);

/// <summary>One rule's contribution: presence p ∈ [0,1], raw deduction, and integer "+X if fixed".</summary>
public record RuleScore(string Category, string RuleId, int Weight, double Presence, double Deduction, int PointsIfFixed);

/// <summary>Result of the score model (ADR 0002).</summary>
public record ScoreResult(
    int Score, bool Available, bool IsUpperBound,
    int EvaluatedRules, int TotalRules, double Coverage,
    IReadOnlyList<RuleScore> Rules)
{
    /// <summary>"74", "≤ 74" when checks failed (they could only lower it), or "n/a".</summary>
    public string Display => !Available ? "n/a" : IsUpperBound ? $"≤ {Score}" : Score.ToString();

    public string CoverageText => $"Score covers {EvaluatedRules} of {TotalRules} checks";

    public int PointsFor(string category) => Rules.FirstOrDefault(r => r.Category == category)?.PointsIfFixed ?? 0;
}

/// <summary>
/// Score model v2 (ADR 0002): score = max(0, 100 − Σ 2.5 × weight × presence). Weights, measures and
/// saturations come from <see cref="FindingCatalog"/> only. Pure — no LDAP, no clock, no files.
/// </summary>
public static class ScoreModel
{
    public const int    FormulaVersion  = 2;
    public const double PointsPerWeight = 2.5;
    /// <summary>Below this share of the catalog weight evaluated, no number is shown.</summary>
    public const double MinCoverage     = 0.5;

    /// <summary>Presence p of one rule from its findings; excepted objects do not count, the
    /// population (Share denominator) stays unchanged.</summary>
    public static double PresenceOf(FindingRule rule, IReadOnlyList<AdFinding> findings, ScanTotals totals)
    {
        if (findings.Count == 0) return 0;
        double CountOpen(AdFinding f) => f.Objects.Count == 0
            ? f.Count
            : f.Objects.Where(o => !o.IsExcepted).Sum(o =>
                rule.Category == "EolOperatingSystems" && o.IsDomainController ? 3 : 1);

        switch (rule.Measure)
        {
            case ScoreMeasure.Informational:
                return 0;
            case ScoreMeasure.Presence:
                var open = findings.Where(f => CountOpen(f) > 0).ToList();
                if (open.Count == 0) return 0;
                return open.Any(f => f.Severity >= FindingSeverity.High) ? 1 : 0.5;
            case ScoreMeasure.Share:
                var count = findings.Sum(CountOpen);
                if (count <= 0) return 0;
                var population = rule.Population switch
                {
                    ScorePopulation.Users     => totals.Users,
                    ScorePopulation.Groups    => totals.Groups,
                    ScorePopulation.Computers => totals.Computers,
                    _                         => 0,
                };
                // Unknown population (old cache): be conservative rather than optimistic
                if (population <= 0) return 1;
                return Math.Min(1, count / (population * rule.Saturation));
            default: // Count
                var n = findings.Sum(CountOpen);
                return n <= 0 ? 0 : Math.Min(1, n / rule.Saturation);
        }
    }

    public static ScoreResult Compute(IReadOnlyCollection<FindingRule> rules, IReadOnlyList<AdFinding> findings,
                                      IReadOnlyList<CheckResult> checks, ScanTotals totals)
    {
        var failed = checks.Where(c => c.Status == CheckStatus.Failed).Select(c => c.Category).ToHashSet();
        double totalWeight = rules.Sum(r => r.Weight);
        double evaluatedWeight = rules.Where(r => !failed.Contains(r.Category)).Sum(r => r.Weight);

        var raw = rules
            .Where(r => !failed.Contains(r.Category))          // a failed check can neither lower nor raise
            .Select(r =>
            {
                var p = PresenceOf(r, findings.Where(f => f.Category == r.Category).ToList(), totals);
                return (Rule: r, P: p, D: PointsPerWeight * r.Weight * p);
            })
            .ToList();

        double sum   = raw.Sum(x => x.D);
        int    score = (int)Math.Max(0, Math.Round(100 - sum, MidpointRounding.AwayFromZero));
        var    points = DistributePoints(raw.Select(x => (x.Rule, x.D)).ToList(), 100 - score);

        var ruleScores = raw
            .Select(x => new RuleScore(x.Rule.Category, x.Rule.RuleId, x.Rule.Weight, x.P, x.D, points[x.Rule.Category]))
            .OrderByDescending(r => r.PointsIfFixed).ThenByDescending(r => r.Weight).ThenBy(r => r.RuleId)
            .ToList();

        double coverage = totalWeight == 0 ? 0 : evaluatedWeight / totalWeight;
        return new ScoreResult(
            score,
            Available:      totalWeight > 0 && coverage >= MinCoverage,
            IsUpperBound:   failed.Count > 0,
            EvaluatedRules: rules.Count - rules.Count(r => failed.Contains(r.Category)),
            TotalRules:     rules.Count,
            Coverage:       coverage,
            Rules:          ruleScores);
    }

    /// <summary>
    /// Integer "+X if fixed" per rule summing exactly to <paramref name="target"/> (= 100 − score),
    /// by largest remainder. Deductions are scaled down when they exceed 100 (score clamped at 0).
    /// Ties: larger remainder, then higher weight, then RuleId — deterministic.
    /// </summary>
    public static Dictionary<string, int> DistributePoints(IReadOnlyList<(FindingRule Rule, double D)> items, int target)
    {
        var result = items.ToDictionary(i => i.Rule.Category, _ => 0);
        double sum = items.Sum(i => i.D);
        if (target <= 0 || sum <= 0) return result;
        double scale = target / sum;
        var parts = items.Select(i => (i.Rule, Exact: i.D * scale)).ToList();
        foreach (var p in parts) result[p.Rule.Category] = (int)Math.Floor(p.Exact);
        int remaining = target - result.Values.Sum();
        foreach (var p in parts
                     .OrderByDescending(p => p.Exact - Math.Floor(p.Exact))
                     .ThenByDescending(p => p.Rule.Weight)
                     .ThenBy(p => p.Rule.RuleId, StringComparer.Ordinal))
        {
            if (remaining <= 0) break;
            if (p.Exact - Math.Floor(p.Exact) <= 0) continue;
            result[p.Rule.Category]++;
            remaining--;
        }
        return result;
    }
}
