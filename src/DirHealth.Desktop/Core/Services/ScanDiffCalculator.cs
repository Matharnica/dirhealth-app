using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Storage;
using System.Text.RegularExpressions;

namespace DirHealth.Desktop.Core.Services;

public record ScanChange(AdFinding Finding, int Delta);

public class ScanDiff
{
    public int ScoreDelta { get; init; }
    /// <summary>False when the two scans used different score formulas — the delta is then 0 and meaningless.</summary>
    public bool ScoreComparable { get; init; } = true;
    public List<AdFinding>  NewFindings      { get; init; } = [];
    public List<AdFinding>  ResolvedFindings { get; init; } = [];
    public List<ScanChange> ChangedFindings  { get; init; } = [];

    public bool HasChanges =>
        NewFindings.Any() || ResolvedFindings.Any() || ChangedFindings.Any() || ScoreDelta != 0;
}

public static class ScanDiffCalculator
{
    public static ScanDiff Calculate(ScanCache previous, ScanCache current)
    {
        // One category can carry several findings (e.g. up to three FineGrainedPasswordPolicy
        // findings per PSO) — group instead of ToDictionary, which threw on the duplicate key.
        var prevByCategory = ByCategory(previous.Findings);
        var currByCategory = ByCategory(current.Findings);

        var newFindings = currByCategory
            .Where(kv => !prevByCategory.ContainsKey(kv.Key))
            .Select(kv => Merge(kv.Value))
            .ToList();

        var resolved = prevByCategory
            .Where(kv => !currByCategory.ContainsKey(kv.Key))
            .Select(kv => Merge(kv.Value))
            .ToList();

        var changed = currByCategory
            .Where(kv => prevByCategory.ContainsKey(kv.Key))
            .Select(kv => (Curr: kv.Value, Prev: prevByCategory[kv.Key]))
            .Select(x => (x.Curr, x.Prev, Delta: x.Curr.Sum(f => f.Count) - x.Prev.Sum(f => f.Count)))
            .Where(x => x.Delta != 0)
            // Neutral title as soon as either scan had several findings: the delta is about the
            // category, so naming the one remaining PSO would be misleading.
            .Select(x => new ScanChange(Merge(x.Curr, neutralTitle: x.Prev.Count > 1), x.Delta))
            .ToList();

        return new ScanDiff
        {
            ScoreComparable  = current.FormulaVersion == previous.FormulaVersion,
            ScoreDelta       = current.FormulaVersion == previous.FormulaVersion
                                 ? current.ComplianceScore - previous.ComplianceScore : 0,
            NewFindings      = newFindings,
            ResolvedFindings = resolved,
            ChangedFindings  = changed
        };
    }

    /// <summary>Findings per category, in first-seen order (keeps the display order of the scan).</summary>
    private static Dictionary<string, List<AdFinding>> ByCategory(IEnumerable<AdFinding>? findings)
    {
        var map = new Dictionary<string, List<AdFinding>>(StringComparer.Ordinal);
        foreach (var f in findings ?? Enumerable.Empty<AdFinding>())
            (map.TryGetValue(f.Category, out var list) ? list : map[f.Category] = []).Add(f);
        return map;
    }

    /// <summary>
    /// One finding per category for the diff. A single finding is returned unchanged; several are
    /// summed (Count), take the highest severity, and get a neutral title naming the category in
    /// words — a title like "PSO 'X': …" would describe only one of them.
    /// </summary>
    private static AdFinding Merge(List<AdFinding> group, bool neutralTitle = false)
    {
        if (group.Count == 1 && !neutralTitle) return group[0];
        var top = group.MaxBy(f => f.Severity)!;
        return new AdFinding
        {
            Category    = top.Category,
            Title       = $"{ReadableCategory(top.Category)} ({group.Count} finding{(group.Count == 1 ? "" : "s")})",
            Description = top.Description,
            Severity    = top.Severity,
            Count       = group.Sum(f => f.Count),
            Objects     = group.SelectMany(f => f.Objects).ToList(),
        };
    }

    /// <summary>
    /// "FineGrainedPasswordPolicy" → "Fine Grained Password Policy", "PreWindows2000Access" →
    /// "Pre Windows 2000 Access". There is no display name per category anywhere in the app
    /// (views, report and exports print the key), so the key is split rather than a second
    /// name list being maintained.
    /// </summary>
    public static string ReadableCategory(string category) =>
        WordBoundary.Replace(category, " ");

    private static readonly Regex WordBoundary =
        new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[A-Za-z])(?=[0-9])", RegexOptions.Compiled);
}
