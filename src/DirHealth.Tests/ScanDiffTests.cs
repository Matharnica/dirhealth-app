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

    // ── Review 2026-11-16, core finding 1: one category can carry several findings ──
    // AdScanner emits up to three FineGrainedPasswordPolicy findings per PSO. ToDictionary on
    // Category threw from the second scan on, so the scan ended "failed" and the cache was never saved.

    private static AdFinding Pso(string title, FindingSeverity sev) =>
        new() { Category = "FineGrainedPasswordPolicy", Title = title, Severity = sev, Count = 1 };

    private static ScanCache CacheOf(int score, params AdFinding[] findings) =>
        new() { ComplianceScore = score, Findings = findings.ToList() };

    [Fact]
    public void Calculate_DuplicateCategoryInBothScans_DoesNotThrow_AndDiffsTheSum()
    {
        var prev = CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High),
                             Pso("PSO 'A': No Account Lockout Configured", FindingSeverity.High));
        var curr = CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High),
                             Pso("PSO 'A': No Account Lockout Configured", FindingSeverity.High),
                             Pso("PSO 'B': Low Minimum Password Length (10 chars)", FindingSeverity.Medium));

        var diff = ScanDiffCalculator.Calculate(prev, curr);

        Assert.Empty(diff.NewFindings);
        Assert.Empty(diff.ResolvedFindings);
        var change = Assert.Single(diff.ChangedFindings);
        Assert.Equal("FineGrainedPasswordPolicy", change.Finding.Category);
        Assert.Equal(1, change.Delta);                               // 3 - 2
        Assert.Equal(3, change.Finding.Count);
        Assert.Equal(FindingSeverity.High, change.Finding.Severity); // highest of the group
        Assert.DoesNotContain("PSO '", change.Finding.Title);        // never names a single PSO for the sum
        Assert.Equal("Fine Grained Password Policy (3 findings)", change.Finding.Title);
    }

    [Fact]
    public void Calculate_DuplicateCategory_SameSum_IsNoChange()
    {
        var prev = CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High),
                             Pso("PSO 'A': No Account Lockout Configured", FindingSeverity.High));
        var curr = CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High),
                             Pso("PSO 'A': No Account Lockout Configured", FindingSeverity.High));

        var diff = ScanDiffCalculator.Calculate(prev, curr);

        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Calculate_DuplicateCategory_NewAndResolved_AreOneEntryEach()
    {
        var withPso = CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High),
                                Pso("PSO 'B': Low Minimum Password Length (10 chars)", FindingSeverity.Medium));
        var without = CacheOf(60, new AdFinding { Category = "InactiveUsers", Title = "Inactive", Count = 4 });

        var appeared = ScanDiffCalculator.Calculate(without, withPso);
        var nf = Assert.Single(appeared.NewFindings);
        Assert.Equal(2, nf.Count);
        Assert.Equal(FindingSeverity.High, nf.Severity);
        Assert.DoesNotContain("PSO '", nf.Title);
        Assert.Equal("InactiveUsers", Assert.Single(appeared.ResolvedFindings).Category);

        var gone = ScanDiffCalculator.Calculate(withPso, without);
        var rf = Assert.Single(gone.ResolvedFindings);
        Assert.Equal("FineGrainedPasswordPolicy", rf.Category);
        Assert.Equal(2, rf.Count);
    }

    [Fact]
    public void Calculate_GroupShrinksToOneFinding_ChangeDoesNotNameTheRemainingPso()
    {
        // The -1 is about the category, not about PSO 'A' — the title must not suggest otherwise.
        var prev = CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High),
                             Pso("PSO 'B': No Account Lockout Configured", FindingSeverity.High));
        var curr = CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High));

        var change = Assert.Single(ScanDiffCalculator.Calculate(prev, curr).ChangedFindings);

        Assert.Equal(-1, change.Delta);
        Assert.DoesNotContain("PSO '", change.Finding.Title);
    }

    [Fact]
    public void Calculate_MinLengthChangedWithinOnePso_IsNotNewPlusResolved()
    {
        // Same PSO, minimum length 6 → 10: the diff stays per category (no "new + resolved" pair).
        var prev = CacheOf(60, Pso("PSO 'A': Weak Minimum Password Length (6 chars)", FindingSeverity.High));
        var curr = CacheOf(60, Pso("PSO 'A': Low Minimum Password Length (10 chars)", FindingSeverity.Medium));

        var diff = ScanDiffCalculator.Calculate(prev, curr);

        Assert.Empty(diff.NewFindings);
        Assert.Empty(diff.ResolvedFindings);
    }

    [Fact]
    public void Calculate_SingleFindingPerCategory_KeepsOriginalFindingAndTitle()
    {
        var f = Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High);
        var diff = ScanDiffCalculator.Calculate(CacheOf(60), CacheOf(60, f));

        Assert.Same(f, Assert.Single(diff.NewFindings));
    }

    [Fact]
    public void Calculate_MergedTitle_IsReadable_SingleKeepsOwnTitle()
    {
        var merged = Assert.Single(ScanDiffCalculator.Calculate(CacheOf(60),
            CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High),
                        Pso("PSO 'B': No Account Lockout Configured", FindingSeverity.High))).NewFindings);
        Assert.Equal("Fine Grained Password Policy (2 findings)", merged.Title);

        var single = Assert.Single(ScanDiffCalculator.Calculate(CacheOf(60),
            CacheOf(60, Pso("PSO 'A': Reversible Encryption Enabled", FindingSeverity.High))).NewFindings);
        Assert.Equal("PSO 'A': Reversible Encryption Enabled", single.Title);
    }

    [Theory]
    [InlineData("FineGrainedPasswordPolicy", "Fine Grained Password Policy")]
    [InlineData("PreWindows2000Access",      "Pre Windows 2000 Access")]
    [InlineData("EolOperatingSystems",       "Eol Operating Systems")]
    [InlineData("LAPSNotDeployed",           "LAPS Not Deployed")]
    [InlineData("InactiveUsers",             "Inactive Users")]
    public void ReadableCategory_SplitsPascalCase(string key, string expected) =>
        Assert.Equal(expected, ScanDiffCalculator.ReadableCategory(key));
}
