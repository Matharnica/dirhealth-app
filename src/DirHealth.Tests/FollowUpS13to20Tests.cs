using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

// Follow-up to the S13–S20 coverage review (2026-10-16): closes the test gaps and pins the two
// "failed read looks like passed/no value" fixes.
public class FollowUpS13to20Tests
{
    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine([ReadOnlyGuardTests.FindDesktopSourceDir(), .. parts]));

    // ── G1: a failed lastLogon read is shown, never "No logon on this DC" ────
    [Fact]
    public void DetailRows_LastLogonError_IsShownAndWarned()
    {
        var row = UserDetailRows.Build(new AdUser { SamAccountName = "x" }, null, "server down")
            .Single(r => r.Label == "Last Logon (this DC)");
        Assert.Equal("Could not be read: server down", row.Value);
        Assert.True(row.Warn);

        var ok = UserDetailRows.Build(new AdUser { SamAccountName = "x" }, null).Single(r => r.Label == "Last Logon (this DC)");
        Assert.Equal("No logon on this DC", ok.Value);
        Assert.False(ok.Warn);
    }

    // ── G1 + G3 (S13-2): with an unreachable directory the detail shows errors, not empty values
    [Fact]
    public async Task UserDetail_UnreachableDirectory_ShowsErrors_NotEmptyState()
    {
        var vm = new UserDetailViewModel(new AdScanner(new AdConnector()));
        await vm.LoadAsync(new AdUser { SamAccountName = "jdoe", DistinguishedName = "CN=jdoe,DC=invalid,DC=test" });

        Assert.StartsWith("Could not be read:", vm.Attributes.Single(r => r.Label == "Last Logon (this DC)").Value);
        Assert.StartsWith("Group memberships could not be loaded:", vm.GroupsError);
        Assert.Empty(vm.Groups);
        Assert.False(vm.IsLoading);
    }

    // ── G2: S19-3 / S20-2 run as one sub-query per rule source ─────────────────
    private static readonly string[] SplitCategories =
    [
        "KrbtgtPasswordOld", "GuestEnabled", "MachineAccountQuota", "PreWindows2000Access", "RecycleBinDisabled",
        "FunctionalLevelOld", "SingleDomainController",
        "LapsNotDeployed", "ComputersWithoutLaps", "MachinePasswordNotRotated", "DuplicateObjects",
        "AnonymousLdapAllowed", "AdminSdExMask", "SmartcardOldPassword",
    ];

    [Fact]
    public void SplitChecks_MatchQueryOf_BothWays()
    {
        var run    = new AdScanner(new AdConnector()).SplitChecks().Select(c => c.Query).ToList();
        var mapped = SplitCategories.Select(c => ScanGuard.QueryOf[c]).Distinct().ToList();
        Assert.Equal(run.Count, run.Distinct().Count());
        Assert.Equal(mapped.OrderBy(x => x), run.OrderBy(x => x));          // every target runs, every run is a target
    }

    [Fact]
    public async Task SplitCheck_FailedRead_FailsOnlyItsOwnRules_AndKeepsOtherFindings()
    {
        var log = new ConcurrentBag<ScanLogEntry>();
        var failures = new ConcurrentDictionary<string, string>();
        var na = new ConcurrentDictionary<string, string>();

        await ScanGuard.Run<List<AdFinding>>("DomainBasics.RecycleBin",
            () => throw new InvalidOperationException("Recycle Bin feature object could not be read."), log, failures, na);
        var maq = await ScanGuard.Run("DomainBasics.MachineAccountQuota",
            () => Task.FromResult(new List<AdFinding> { new() { Category = "MachineAccountQuota", Count = 1 } }), log, failures, na);

        var checks = ScanGuard.Evaluate(FindingCatalog.All, maq, failures, na).ToDictionary(c => c.Category);
        Assert.Equal(CheckStatus.Failed, checks["RecycleBinDisabled"].Status);
        Assert.Equal(CheckStatus.Found,  checks["MachineAccountQuota"].Status);   // finding survives the sibling's failure
        Assert.Equal(CheckStatus.Passed, checks["GuestEnabled"].Status);
        Assert.Single(checks.Values, c => c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void SplitScannerMethods_NeverSwallowAFailedRead()
    {
        var s19 = Src("Core", "AD", "AdScanner.Checks19.cs");
        var s20 = Src("Core", "AD", "AdScanner.Checks20.cs");
        Assert.DoesNotContain("if (!g.Found) continue;", s19);                 // operator groups
        Assert.DoesNotContain("if (preWin.Found)", s19);
        Assert.DoesNotContain("if (krbtgt is not null", s19);
        Assert.DoesNotContain("if (!string.IsNullOrEmpty(config))", s19);
        Assert.DoesNotContain("catch", s19);
        Assert.DoesNotContain("catch", s20);
    }

    // ── G5: decision logic of S19-3 checks that used to be inline ───────────────
    [Theory]
    [InlineData(0, false)] [InlineData(1, true)] [InlineData(10, true)]
    public void MachineAccountQuota(int quota, bool risky) => Assert.Equal(risky, CheckLogic.MachineAccountQuotaRisky(quota));

    [Fact]
    public void RecycleBin_DisabledWithoutBacklinks()
    {
        Assert.True(CheckLogic.RecycleBinDisabled(0));
        Assert.False(CheckLogic.RecycleBinDisabled(2));
    }

    [Fact]
    public void DomainControllers_OneIsFinding_ZeroIsFailedRead()
    {
        Assert.True(CheckLogic.TooFewDomainControllers(1));
        Assert.False(CheckLogic.TooFewDomainControllers(2));
        Assert.Throws<InvalidOperationException>(() => CheckLogic.TooFewDomainControllers(0));
    }

    [Fact]
    public void Sprint20Filters_ExcludeDisabled_AndUseTheirThresholds()
    {
        Assert.Contains(CheckFilters.NotDisabled, CheckFilters.StaleMachinePasswords("1", "2"));
        Assert.Contains("(!(userAccountControl:1.2.840.113556.1.4.803:=8192))", CheckFilters.StaleMachinePasswords("1", "2"));
        Assert.Contains("(pwdLastSet<=1)", CheckFilters.StaleMachinePasswords("1", "2"));
        Assert.Contains("(lastLogonTimestamp>=2)", CheckFilters.StaleMachinePasswords("1", "2"));
        Assert.Contains(":=8192)", CheckFilters.StaleDcPasswords("1"));
        Assert.Contains(CheckFilters.EnabledUser, CheckFilters.SmartcardOldPassword("1"));
        Assert.Contains(":=262144)", CheckFilters.SmartcardOldPassword("1"));
        Assert.Equal((45, 90, 365), (CheckFilters.MachinePasswordMaxDays, CheckFilters.DcPasswordMaxDays, CheckFilters.SmartcardPasswordMaxDays));
    }

    // ── G7: userPassword / unixUserPassword are only ever used in a presence filter ─
    [Fact]
    public void UserPassword_OnlyAsPresenceFilter_NeverLoaded()
    {
        var root = ReadOnlyGuardTests.FindDesktopSourceDir();
        var attr = new Regex(@"(?i)\b(unix)?userPassword\b(?!=\*)");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (var line in File.ReadLines(file))
            {
                var code = line.Split("//")[0];
                // String literals that name the attribute: only "userPassword=*"-style presence filters are allowed
                foreach (Match lit in Regex.Matches(code, "\"[^\"]*\""))
                    if (attr.IsMatch(lit.Value) && !lit.Value.Contains(' '))
                        Assert.Fail($"{Path.GetFileName(file)}: {line.Trim()}");
            }
        }
    }

    // ── G4: the LDAP-mode warning banner stays wired (CLAUDE.md invariant) ──────
    [Fact]
    public void AdSearchView_KeepsLdapWarningBanner()
    {
        var xaml = Src("Views", "AdSearch", "AdSearchView.xaml");
        Assert.Matches(@"DataTrigger Binding=""\{Binding IsLdapMode\}"" Value=""True"">\s*<Setter Property=""Visibility"" Value=""Visible""", xaml);
        Assert.Contains("Unrestricted Access", xaml);
    }

    // ── G9 (S13-3): the EOL fix text mentions ESU ──────────────────────────────
    [Fact]
    public void EolFix_MentionsEsu()
    {
        var rule = FindingCatalog.Get("EolOperatingSystems")!;
        Assert.Contains("ESU", rule.HowToFix);
    }
}
