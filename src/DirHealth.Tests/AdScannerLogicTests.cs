using DirHealth.Desktop.Core.AD;
using Xunit;

namespace DirHealth.Tests;

// Pure scan-core logic (ScoreCalculator / EolMatcher / UacFlags) — no AD or Windows dependency.
public class AdScannerLogicTests
{
    // ── ScoreCalculator ───────────────────────────────────────────────────────
    [Fact]
    public void Score_NoFindings_Is100()
    {
        Assert.Equal(100, ScoreCalculator.Compute(new ScoreInputs()));
    }

    [Fact]
    public void Score_MixedFindings_SubtractsExpected()
    {
        // NoOs: min(5,3)=3, Kerberoastable: min(12,1*4)=4  →  100 - 3 - 4 = 93
        var score = ScoreCalculator.Compute(new ScoreInputs { NoOs = 3, Kerberoastable = 1 });
        Assert.Equal(93, score);
    }

    [Fact]
    public void Score_ProportionalPenalty_ScalesWithRatio()
    {
        // 30 of 100 inactive users saturates the 20-point penalty at 30% → 80
        var score = ScoreCalculator.Compute(new ScoreInputs { TotalUsers = 100, InactiveUsers = 30 });
        Assert.Equal(80, score);
    }

    [Fact]
    public void Score_ClampsAtMinimum10()
    {
        var score = ScoreCalculator.Compute(new ScoreInputs
        {
            StaleDomainAdmins       = 100,
            UnconstrainedDelegation = 100,
            EolDcCount              = 100,
            AsRepRoastable          = 100,
            Kerberoastable          = 100,
            PasswordNotRequired     = 100,
            SidHistory              = 100,
        });
        Assert.Equal(ScoreCalculator.Min, score);
        Assert.Equal(10, score);
    }

    [Theory]
    [InlineData(0, 100, 0)]    // no affected → no penalty
    [InlineData(50, 0, 0)]     // no population → no penalty (divide-by-zero guard)
    [InlineData(30, 100, 20)]  // 30% saturates the 20-point cap
    [InlineData(15, 100, 10)]  // 15% is half of the 30% saturation point → 10
    public void Score_PctPenalty_Cases(int count, int total, int expected)
    {
        Assert.Equal(expected, ScoreCalculator.PctPenalty(count, total, maxPenalty: 20, fullAtPct: 30));
    }

    // ── EolMatcher ────────────────────────────────────────────────────────────
    [Fact]
    public void Eol_Windows81_MatchesBefore_Windows8()
    {
        // If "Windows 8" matched first this would return 2016-01-12; the ordering rule gives 8.1's date.
        Assert.True(EolMatcher.TryGetEolDate("Windows 8.1 Enterprise", out var date));
        Assert.Equal(new DateTime(2023, 1, 10), date);
    }

    [Fact]
    public void Eol_Server2012R2_MatchesBefore_Server2012()
    {
        Assert.True(EolMatcher.TryGetEolDate("Windows Server 2012 R2 Standard", out var date));
        Assert.Equal(new DateTime(2023, 10, 10), date);
    }

    [Fact]
    public void Eol_Server2008R2_And_Windows7_AreMatched()
    {
        Assert.True(EolMatcher.TryGetEolDate("Windows Server 2008 R2 Datacenter", out var server));
        Assert.Equal(new DateTime(2020, 1, 14), server);
        Assert.True(EolMatcher.TryGetEolDate("Windows 7 Professional", out var client));
        Assert.Equal(new DateTime(2020, 1, 14), client);
    }

    [Theory]
    [InlineData("Windows 11 Pro")]
    [InlineData("Windows 10 Enterprise")]
    [InlineData("Windows Server 2022 Standard")]
    [InlineData("Windows Server 2019 Datacenter")]
    public void Eol_CurrentOs_NoMatch(string os)
    {
        Assert.False(EolMatcher.TryGetEolDate(os, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Eol_EmptyOrNull_NoMatch(string? os)
    {
        Assert.False(EolMatcher.TryGetEolDate(os!, out _));
    }

    // ── UacFlags ──────────────────────────────────────────────────────────────
    [Fact]
    public void Uac_Disabled_Bit2()
    {
        Assert.True(UacFlags.IsDisabled(0x0202));   // NORMAL_ACCOUNT (512) + DISABLE (2)
        Assert.False(UacFlags.IsDisabled(0x0200));
        Assert.True(UacFlags.IsEnabled(0x0200));
    }

    [Fact]
    public void Uac_DomainController_Bit8192()
    {
        Assert.True(UacFlags.IsDomainController(UacFlags.ServerTrustAccount | 0x0200));
        Assert.False(UacFlags.IsDomainController(0x0200));
    }

    [Fact]
    public void Uac_PasswordNotRequired_Bit32()
    {
        Assert.True(UacFlags.PasswordNotRequired(UacFlags.PasswdNotReqd | 0x0200));
        Assert.False(UacFlags.PasswordNotRequired(0x0200));
    }

    [Fact]
    public void Uac_AsRepRoastable_Bit4194304()
    {
        Assert.True(UacFlags.AsRepRoastable(UacFlags.DontRequirePreauth | 0x0200));
        Assert.False(UacFlags.AsRepRoastable(0x0200));
    }

    [Fact]
    public void Uac_PasswordNeverExpires_Bit65536()
    {
        Assert.True(UacFlags.PasswordNeverExpires(UacFlags.DontExpirePassword | 0x0200));
        Assert.False(UacFlags.PasswordNeverExpires(0x0200));
    }

    [Fact]
    public void Uac_CombinedFlags_ReportedIndependently()
    {
        int uac = UacFlags.Disabled | UacFlags.DontExpirePassword | UacFlags.PasswdNotReqd;
        Assert.True(UacFlags.IsDisabled(uac));
        Assert.True(UacFlags.PasswordNeverExpires(uac));
        Assert.True(UacFlags.PasswordNotRequired(uac));
        Assert.False(UacFlags.IsDomainController(uac));
        Assert.False(UacFlags.AsRepRoastable(uac));
    }
}
