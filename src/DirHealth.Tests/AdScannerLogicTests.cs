using DirHealth.Desktop.Core.AD;
using Xunit;

namespace DirHealth.Tests;

// Pure scan-core logic (EolMatcher / UacFlags) — no AD or Windows dependency.
public class AdScannerLogicTests
{
    // Score tests moved to ScoreModelTests (ADR 0002, S17-4) — ScoreCalculator v1 was removed.

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

    // S13-3: Windows 10 / 2000 / NT. "Windows 10 Enterprise" moved here from the NoMatch list
    // when Windows 10 reached end of support (2025-10-14).
    [Theory]
    [InlineData("Windows 10 Enterprise", 2025, 10, 14)]
    [InlineData("Windows 10 Pro",        2025, 10, 14)]
    [InlineData("Windows 2000 Server",   2010,  7, 13)]
    [InlineData("Windows 2000 Professional", 2010, 7, 13)]
    [InlineData("Windows NT",            2004, 12, 31)]
    public void Eol_S13_3_NewEntries_AreMatched(string os, int y, int m, int d)
    {
        Assert.True(EolMatcher.TryGetEolDate(os, out var date));
        Assert.Equal(new DateTime(y, m, d), date);
    }

    [Theory]
    [InlineData("Windows 11 Pro")]
    [InlineData("Windows 11 Enterprise")]
    [InlineData("Windows Server 2016 Standard")]
    [InlineData("Windows Server 2022 Standard")]
    [InlineData("Windows Server 2019 Datacenter")]
    [InlineData("Windows Server 2025 Datacenter")]
    [InlineData("Windows 10 Enterprise LTSC")]   // own, later support end — see EolMatcher
    [InlineData("Windows 10 Enterprise 2016 LTSB")]
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
