using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;
using Xunit;

namespace DirHealth.Tests;

public class ScanDiffTests
{
    private static ScanCache Cache(int score, params (string Category, int Count)[] findings) => new()
    {
        ComplianceScore = score,
        Findings = findings.Select(f => new AdFinding { Category = f.Category, Count = f.Count }).ToList()
    };

    [Fact]
    public void Calculate_DetectsNewResolvedChanged_AndScoreDelta()
    {
        var prev = Cache(70, ("InactiveUsers", 5), ("EmptyGroups", 3));
        var curr = Cache(75, ("InactiveUsers", 8), ("SidHistory", 2));

        var diff = ScanDiffCalculator.Calculate(prev, curr);

        Assert.Equal(5, diff.ScoreDelta);
        Assert.Single(diff.NewFindings);
        Assert.Equal("SidHistory", diff.NewFindings[0].Category);
        Assert.Single(diff.ResolvedFindings);
        Assert.Equal("EmptyGroups", diff.ResolvedFindings[0].Category);
        Assert.Single(diff.ChangedFindings);
        Assert.Equal("InactiveUsers", diff.ChangedFindings[0].Finding.Category);
        Assert.Equal(3, diff.ChangedFindings[0].Delta);   // 8 - 5
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Calculate_IdenticalScans_HasNoChanges()
    {
        var a = Cache(80, ("InactiveUsers", 5));
        var b = Cache(80, ("InactiveUsers", 5));

        var diff = ScanDiffCalculator.Calculate(a, b);

        Assert.Empty(diff.NewFindings);
        Assert.Empty(diff.ResolvedFindings);
        Assert.Empty(diff.ChangedFindings);
        Assert.Equal(0, diff.ScoreDelta);
        Assert.False(diff.HasChanges);
    }
}
