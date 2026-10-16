using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;

namespace DirHealth.Tests;

// Sprint 16: finding model, finding card, fix block, check status.
[Collection(ExportHookCollection.Name)]
public class Sprint16Tests
{
    // ── S16-1 model ──────────────────────────────────────────────────────────
    [Fact]
    public void Catalog_EveryScannedCategoryHasRuleIdAndWeight()
    {
        Assert.Equal(ScanGuard.QueryOf.Keys.OrderBy(k => k), FindingCatalog.All.Select(r => r.Category).OrderBy(c => c));
        foreach (var rule in FindingCatalog.All)
        {
            Assert.Matches(@"^DH-(USR|GRP|CMP|KRB|PRV|POL|DOM|TRU)-\d{3}$", rule.RuleId);
            Assert.InRange(rule.Weight, 1, 10);
            Assert.False(string.IsNullOrWhiteSpace(rule.What));
            Assert.False(string.IsNullOrWhiteSpace(rule.Why));
            Assert.False(string.IsNullOrWhiteSpace(rule.HowToFix));
            Assert.NotEmpty(rule.References);
        }
        Assert.Equal(FindingCatalog.All.Count, FindingCatalog.All.Select(r => r.RuleId).Distinct().Count());
    }

    [Fact]
    public void Catalog_ExploitableOutweighsHygiene()
    {
        Assert.True(FindingCatalog.Get("AsRepRoasting")!.Weight > FindingCatalog.Get("InactiveUsers")!.Weight);
        Assert.True(FindingCatalog.Get("UnconstrainedDelegationComputers")!.Weight > FindingCatalog.Get("EmptyGroups")!.Weight);
    }

    [Fact]
    public void Catalog_ReferencesAreHttps()
    {
        foreach (var r in FindingCatalog.All.SelectMany(r => r.References))
            Assert.StartsWith("https://", r.Url);
    }

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void OldCache_WithStringObjects_StillLoads()
    {
        // Shape of last-scan.json written by v2.9.x
        const string old = """
        { "Findings": [ { "Category": "InactiveUsers", "Title": "2 Inactive", "Severity": "Medium", "Count": 2,
                          "AffectedObjects": ["jdoe", "asmith"], "IsAcknowledged": false, "AcknowledgeNote": "" } ],
          "ComplianceScore": 80, "LastScanTime": "x", "FindingsCount": 1 }
        """;
        var cache = JsonSerializer.Deserialize<ScanCache>(old, Json)!;
        var f = Assert.Single(cache.Findings);
        Assert.Equal(["jdoe", "asmith"], f.Objects.Select(o => o.Name));
        Assert.All(f.Objects, o => Assert.Equal("", o.DistinguishedName));   // a SAM is not a DN
        Assert.Empty(cache.Checks);                                           // field added in S16
        Assert.Equal("DH-USR-001", f.RuleId);
    }

    [Fact]
    public void NewCache_RoundTrip_KeepsStructuredObjects()
    {
        var finding = new AdFinding
        {
            Category = "AsRepRoasting", Count = 1,
            Objects  = [new AffectedObject { Name = "svc", DistinguishedName = "CN=svc,DC=x", Sid = "S-1-5-21-1-2-3-1000", Type = AffectedObjectType.User }],
        };
        var json = JsonSerializer.Serialize(new ScanCache { Findings = [finding] }, Json);
        Assert.DoesNotContain("\"Rule\"", json);       // catalog data is not persisted
        var back = JsonSerializer.Deserialize<ScanCache>(json, Json)!.Findings.Single();
        var o = Assert.Single(back.Objects);           // not duplicated by the legacy AffectedObjects setter
        Assert.Equal("CN=svc,DC=x", o.DistinguishedName);
        Assert.Equal(AffectedObjectType.User, o.Type);
    }

    [Fact]
    public void AffectedObjects_LegacyView_IncludesDetail()
    {
        var f = new AdFinding { Objects = [new AffectedObject { Name = "WS01", Detail = "Windows 7" }] };
        Assert.Equal(["WS01 — Windows 7"], f.AffectedObjects);
    }

    // ── S16-3 fix block ──────────────────────────────────────────────────────
    [Fact]
    public void FixBlock_EveryRuleHasFixRiskAndRevert()
    {
        foreach (var r in FindingCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Fix),     r.Category);
            Assert.False(string.IsNullOrWhiteSpace(r.FixRisk), r.Category);
            Assert.False(string.IsNullOrWhiteSpace(r.Revert),  r.Category);
        }
    }

    [Fact]
    public void FixBlock_UsesExactlyTheCheckFilter()
    {
        foreach (var r in FindingCatalog.All.Where(r => r.CheckFilter is not null))
            Assert.Contains($"-LDAPFilter \"{r.CheckFilter}\"", r.Fix);
        // spot checks against the scanner's source of truth
        Assert.Contains(CheckFilters.AsRepRoastable, FindingCatalog.Get("AsRepRoasting")!.Fix);
        Assert.Contains(CheckFilters.InactiveUsers("$cutoff"), FindingCatalog.Get("InactiveUsers")!.Fix);
        Assert.Contains($"AddDays(-{CheckFilters.InactiveUserDays})", FindingCatalog.Get("InactiveUsers")!.Fix);
        Assert.Contains(CheckFilters.NotDisabled, FindingCatalog.Get("KerberoastableAccounts")!.Fix);   // enabled only, like the check
    }

    [Fact]
    public void FixBlock_WritesStartWithWhatIfAndBackup()
    {
        foreach (var r in FindingCatalog.All.Where(r => !r.Fix.Contains("read-only")))
        {
            Assert.Contains("-WhatIf", r.Fix);
            Assert.Contains("backup", r.Fix, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FixBlock_StaleDomainAdmins_FindsGroupBySid()
    {
        var fix = FindingCatalog.Get("StaleDomainAdmins")!.Fix;
        Assert.Contains("-512", fix);
        Assert.DoesNotContain("'Domain Admins'", fix);
    }

    [Theory]
    [InlineData("Windows 10 Enterprise")]
    [InlineData("Windows 10 Enterprise LTSC")]
    [InlineData("Windows 11 Pro")]
    [InlineData("Windows Server 2012 R2 Standard")]
    [InlineData("Windows Server 2022 Standard")]
    [InlineData("Windows 8.1 Pro")]
    [InlineData("Windows NT")]
    public void FixBlock_EolPowerShellFilter_MatchesEolMatcher(string os)
    {
        var fix   = FindingCatalog.Get("EolOperatingSystems")!.Fix;
        var match = Regex.Match(fix, @"-match '([^']+)' -and \$_\.operatingSystem -notmatch '([^']+)'");
        Assert.True(match.Success);
        bool ps = Regex.IsMatch(os, match.Groups[1].Value, RegexOptions.IgnoreCase)
               && !Regex.IsMatch(os, match.Groups[2].Value, RegexOptions.IgnoreCase);
        Assert.Equal(EolMatcher.TryGetEolDate(os, out _), ps);
    }

    // ── S16-4 check status ───────────────────────────────────────────────────
    [Fact]
    public async Task Guard_FailingQuery_DoesNotStopOthers()
    {
        var log = new ConcurrentBag<ScanLogEntry>();
        var failures = new ConcurrentDictionary<string, string>();
        var na = new ConcurrentDictionary<string, string>();

        var bad  = ScanGuard.Run<List<AdUser>>("AsRepRoasting", () => throw new TimeoutException("LDAP timeout"), log, failures, na);
        var good = ScanGuard.Run("InactiveUsers", () => Task.FromResult(new List<AdUser> { new() }), log, failures, na);
        await Task.WhenAll(bad, good);

        Assert.Empty(bad.Result);
        Assert.Single(good.Result);
        Assert.Equal("LDAP timeout", failures["AsRepRoasting"]);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public async Task Guard_NotApplicable_IsRecordedSeparately()
    {
        var failures = new ConcurrentDictionary<string, string>();
        var na = new ConcurrentDictionary<string, string>();
        await ScanGuard.Run<List<AdFinding>>("FineGrainedPwdPolicies",
            () => throw new CheckNotApplicableException("No PSOs."), new(), failures, na);
        Assert.Empty(failures);
        Assert.Equal("No PSOs.", na["FineGrainedPwdPolicies"]);
    }

    [Fact]
    public void Evaluate_FailedIsNeverPassed_AndOrderIsFailedFirst()
    {
        var findings = new List<AdFinding> { new() { Category = "InactiveUsers", Count = 4 } };
        var failures = new Dictionary<string, string> { ["PasswordPolicy"] = "access denied" };
        var na       = new Dictionary<string, string> { ["FineGrainedPwdPolicies"] = "No PSOs." };

        var checks = ScanGuard.Evaluate(FindingCatalog.All, findings, failures, na);

        Assert.Equal(FindingCatalog.All.Count, checks.Count);
        foreach (var c in new[] { "WeakPasswordLength", "WeakPasswordHistory", "NoAccountLockout" })
            Assert.Equal(CheckStatus.Failed, checks.Single(x => x.Category == c).Status);   // one failing query → all its checks
        Assert.Equal("access denied", checks.Single(x => x.Category == "NoAccountLockout").Reason);
        Assert.Equal(CheckStatus.Found, checks.Single(x => x.Category == "InactiveUsers").Status);
        Assert.Equal(4, checks.Single(x => x.Category == "InactiveUsers").Count);
        Assert.Equal(CheckStatus.NotApplicable, checks.Single(x => x.Category == "FineGrainedPasswordPolicy").Status);
        Assert.Equal(CheckStatus.Passed, checks.Single(x => x.Category == "SidHistory").Status);
        Assert.Equal(CheckStatus.Failed, checks[0].Status);
    }

    [Fact]
    public void Dashboard_Summary()
    {
        var checks = new List<CheckResult>
        {
            new("A", "1", CheckStatus.Passed, "", 0), new("B", "2", CheckStatus.Passed, "", 0),
            new("C", "3", CheckStatus.Found, "", 3),  new("D", "4", CheckStatus.Failed, "x", 0),
        };
        Assert.Equal("2 passed · 1 found · 1 failed · 0 not applicable", DashboardViewModel.Summarize(checks));
    }

    // ── S16-2 finding card ───────────────────────────────────────────────────
    [Fact]
    public void Card_SelectingFinding_FillsObjectTableAndRule()
    {
        var vm = new FindingsViewModel();
        vm.SelectedFinding = new AdFinding
        {
            Category = "KerberoastableAccounts",
            Objects  = [new AffectedObject { Name = "svc_sql", DistinguishedName = "CN=svc_sql,DC=x", Type = AffectedObjectType.User }],
        };
        Assert.Equal("DH-KRB-001", vm.SelectedRule!.RuleId);
        Assert.Equal(1, vm.ObjectsTable.TotalCount);
    }

    [Fact]
    public void Card_CopyFix_GoesToClipboardOnly()
    {
        string? copied = null;
        var saved = TableViewModel.SetClipboardText;
        try
        {
            TableViewModel.SetClipboardText = s => copied = s;
            var vm = new FindingsViewModel { SelectedFinding = new AdFinding { Category = "AsRepRoasting" } };
            vm.CopyFixCommand.Execute(null);
            Assert.Equal(FindingCatalog.Get("AsRepRoasting")!.Fix, copied);
            vm.CopyRevertCommand.Execute(null);
            Assert.Equal(FindingCatalog.Get("AsRepRoasting")!.Revert, copied);
        }
        finally { TableViewModel.SetClipboardText = saved; }
    }

    [Fact]
    public void Card_OpenObject_NavigatesOnlyWithDn()
    {
        AffectedObject? opened = null;
        var vm = new FindingsViewModel { NavigateToObject = o => opened = o };
        vm.OpenObject(new AffectedObject { Name = "jdoe", Type = AffectedObjectType.User });
        Assert.Null(opened);
        Assert.Contains("run a new scan", vm.StatusMessage);

        var withDn = new AffectedObject { Name = "jdoe", DistinguishedName = "CN=jdoe,DC=x", Type = AffectedObjectType.User };
        vm.OpenObject(withDn);
        Assert.Same(withDn, opened);
    }
}
