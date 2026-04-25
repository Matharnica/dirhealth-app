using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.Core.Services;

public record ScanChange(AdFinding Finding, int Delta);

public class ScanDiff
{
    public int ScoreDelta { get; init; }
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
        var prevByCategory = previous.Findings.ToDictionary(f => f.Category);
        var currByCategory = current.Findings.ToDictionary(f => f.Category);

        var newFindings = currByCategory.Values
            .Where(f => !prevByCategory.ContainsKey(f.Category))
            .ToList();

        var resolved = prevByCategory.Values
            .Where(f => !currByCategory.ContainsKey(f.Category))
            .ToList();

        var changed = currByCategory.Values
            .Where(f => prevByCategory.TryGetValue(f.Category, out var p) && p.Count != f.Count)
            .Select(f => new ScanChange(f, f.Count - prevByCategory[f.Category].Count))
            .ToList();

        return new ScanDiff
        {
            ScoreDelta       = current.ComplianceScore - previous.ComplianceScore,
            NewFindings      = newFindings,
            ResolvedFindings = resolved,
            ChangedFindings  = changed
        };
    }
}
