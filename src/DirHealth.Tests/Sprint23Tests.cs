using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Storage;
using Xunit;
using static DirHealth.Desktop.Core.AD.Checks.CheckLogic;

namespace DirHealth.Tests;

// Sprint 23: checks with high value — privileged SID history, admin backdoors, recoverability.
public class Sprint23Tests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private const string Dom     = "S-1-5-21-100-200-300";
    private const string Foreign = "S-1-5-21-900-800-700";

    private static readonly string[] NewCategories =
        ["PrivilegedSidHistory", "AdminShadowCredentials", "AdminAltSecurityIdentities", "AdminProtocolTransition", "BackupOutdated", "SysvolNtfrs"];

    // ── catalog, mapping, score version ──────────────────────────────────────
    [Fact]
    public void NewChecks_AreCatalogued_Scored_AndMappedToTheirOwnQuery()
    {
        var expected = new Dictionary<string, (string RuleId, string Query)>
        {
            ["PrivilegedSidHistory"]       = ("DH-PRV-010", "Privileged.SidHistory"),
            ["AdminShadowCredentials"]     = ("DH-PRV-011", "Privileged.Backdoors"),
            ["AdminAltSecurityIdentities"] = ("DH-PRV-012", "Privileged.Backdoors"),
            ["AdminProtocolTransition"]    = ("DH-PRV-013", "Privileged.Backdoors"),
            ["BackupOutdated"]             = ("DH-DOM-010", "Recovery.Backup"),
            ["SysvolNtfrs"]                = ("DH-DOM-011", "Recovery.Sysvol"),
        };
        foreach (var (category, (ruleId, query)) in expected)
        {
            var r = FindingCatalog.Get(category);
            Assert.NotNull(r);
            Assert.Equal(ruleId, r!.RuleId);
            Assert.Equal(query, ScanGuard.QueryOf[category]);
            Assert.False(string.IsNullOrWhiteSpace(r.Fix), category);
            Assert.False(string.IsNullOrWhiteSpace(r.FixRisk), category);
            Assert.False(string.IsNullOrWhiteSpace(r.Revert), category);
            Assert.NotEmpty(r.References);
            Assert.NotEqual(ScoreMeasure.Informational, r.Measure);
        }
        var run = new AdScanner(new AdConnector()).SplitChecks().Select(c => c.Query).ToList();
        Assert.Subset(run.ToHashSet(), expected.Values.Select(v => v.Query).ToHashSet());
        Assert.Equal(5, ScoreModel.FormulaVersion);
    }

    [Fact]
    public void PrivilegedSidHistory_IsCritical_AndOutweighsPlainSidHistory()
    {
        var priv  = FindingCatalog.Get("PrivilegedSidHistory")!;
        var plain = FindingCatalog.Get("SidHistory")!;
        Assert.Equal(10, priv.Weight);
        Assert.True(priv.Weight > plain.Weight);
        Assert.NotEqual(plain.RuleId, priv.RuleId);   // separate rule, DH-USR-005 stays
    }

    [Fact]
    public void Fixes_ArePowerShellText_WithWhatIfOrReadOnly()
    {
        foreach (var c in NewCategories)
        {
            var fix = FindingCatalog.Get(c)!.Fix;
            Assert.StartsWith("Import-Module ActiveDirectory", fix);
            Assert.True(fix.Contains("-WhatIf") || fix.Contains("read-only"), c);
        }
        Assert.Contains(CheckFilters.SidHistoryAnyObject, FindingCatalog.Get("PrivilegedSidHistory")!.Fix);
        Assert.Contains(CheckFilters.ProtocolTransitionUsers, FindingCatalog.Get("AdminProtocolTransition")!.Fix);
    }

    // ── S23-1 privileged SID in sIDHistory ───────────────────────────────────
    [Theory]
    [InlineData(Dom + "-512", true)]          // Domain Admins, own domain
    [InlineData(Foreign + "-519", true)]      // Enterprise Admins of a foreign domain
    [InlineData(Foreign + "-500", true)]      // built-in Administrator
    [InlineData(Dom + "-502", true)]          // krbtgt
    [InlineData(Dom + "-516", true)]          // Domain Controllers (DCSync)
    [InlineData(Dom + "-517", true)]          // Cert Publishers (Tier 0)
    [InlineData("S-1-5-32-544", true)]        // BUILTIN\Administrators
    [InlineData("S-1-5-32-551", true)]        // Backup Operators
    [InlineData("S-1-5-9", true)]             // Enterprise Domain Controllers
    [InlineData(Dom + "-1105", false)]        // ordinary migrated user
    [InlineData(Dom + "-513", false)]         // Domain Users
    [InlineData("S-1-5-32-545", false)]       // BUILTIN\Users
    [InlineData("S-1-5-21-1-2-512", false)]   // a domain SID without RID is no account SID
    [InlineData("", false)]
    public void PrivilegedHistorySid(string sid, bool privileged) => Assert.Equal(privileged, IsPrivilegedHistorySid(sid));

    [Fact]
    public void UnprivilegedHistory_IsNoFinding_InTheNewRule()
    {
        Assert.Empty(PrivilegedHistorySids([Dom + "-1105", Foreign + "-2001"]));
        Assert.Equal([Dom + "-512"], PrivilegedHistorySids([Dom + "-1105", Dom + "-512"]));
    }

    [Fact]
    public void PrivilegedHistory_CoversEveryTier0GroupWithAFixedSid()
    {
        foreach (var def in WellKnownGroups.Tier0.Where(d => d.Source != GroupSidSource.NameOnly))
        {
            var sid = WellKnownGroups.SidFor(def, Foreign, Foreign)!;
            Assert.True(IsPrivilegedHistorySid(sid), def.FallbackName);
        }
    }

    [Fact]
    public void PrivilegedSidHistoryFix_UsesTheSameRidLists()
    {
        var fix = FindingCatalog.Get("PrivilegedSidHistory")!.Fix;
        Assert.Contains("-in " + string.Join(",", PrivilegedDomainRids), fix);
        Assert.Contains("-in " + string.Join(",", PrivilegedBuiltinRids), fix);
        Assert.Contains($"'{EnterpriseDomainControllersSid}'", fix);
    }

    [Fact]
    public void SidHistoryFilter_KeepsGroups_WhichHaveNoUserAccountControl()
    {
        // (!(userAccountControl:…:=2)) is undefined on groups; the OR with objectClass=group keeps them
        Assert.Contains("(|(objectClass=group)" + CheckFilters.NotDisabled + ")", CheckFilters.SidHistoryAnyObject);
        Assert.StartsWith("(&(sIDHistory=*)", CheckFilters.SidHistoryAnyObject);
    }

    // ── S23-2 admin backdoors ─────────────────────────────────────────────────
    [Fact]
    public void ProtocolTransitionUac_IsAFinding()
    {
        var issues = EvaluateBackdoors(new BackdoorFacts(0x200 | UacFlags.TrustedToAuthForDelegation, false, false));
        Assert.Equal(BackdoorIssue.ProtocolTransition, issues);
        Assert.Equal(0x1000000, UacFlags.TrustedToAuthForDelegation);
    }

    [Fact]
    public void KeyCredentialAndAltSecurityIdentities_AreFindings_CleanAdminIsNot()
    {
        Assert.Equal(BackdoorIssue.ShadowCredentials, EvaluateBackdoors(new BackdoorFacts(0x200, true, false)));
        Assert.Equal(BackdoorIssue.AltSecurityIdentities, EvaluateBackdoors(new BackdoorFacts(0x200, false, true)));
        Assert.Equal(BackdoorIssue.None, EvaluateBackdoors(new BackdoorFacts(0x200 | UacFlags.TrustedForDelegation, false, false)));
        Assert.Equal(BackdoorIssue.ShadowCredentials | BackdoorIssue.AltSecurityIdentities | BackdoorIssue.ProtocolTransition,
            EvaluateBackdoors(new BackdoorFacts(UacFlags.TrustedToAuthForDelegation, true, true)));
    }

    [Fact]
    public void BackdoorFilters_ArePresenceFilters_OnEnabledUsers()
    {
        Assert.Contains("(msDS-KeyCredentialLink=*)", CheckFilters.ShadowCredentialUsers);
        Assert.Contains("(altSecurityIdentities=*)", CheckFilters.AltSecurityIdentityUsers);
        Assert.Contains("(userAccountControl:1.2.840.113556.1.4.803:=16777216)", CheckFilters.ProtocolTransitionUsers);
        foreach (var f in new[] { CheckFilters.ShadowCredentialUsers, CheckFilters.AltSecurityIdentityUsers, CheckFilters.ProtocolTransitionUsers })
            Assert.Contains(CheckFilters.EnabledUser, f);
    }

    /// <summary>
    /// Key material (msDS-KeyCredentialLink) and certificate mappings (altSecurityIdentities) may only appear
    /// as a presence filter "attr=*" — never as a bare attribute name (PropertiesToLoad, Properties[…]).
    /// Literals with a space are prose or fix-block text for the admin, not something DirHealth requests.
    /// Same pattern as the userPassword test (FollowUpS13to20Tests.G7).
    /// </summary>
    [Fact]
    public void KeyMaterial_OnlyAsPresenceFilter_NeverLoaded()
    {
        var root = ReadOnlyGuardTests.FindDesktopSourceDir();
        var attr = new Regex(@"(?i)\b(msDS-KeyCredentialLink|altSecurityIdentities)\b(?!=\*)");
        int checkedLiterals = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (var line in File.ReadLines(file))
            {
                var code = line.Split("//")[0];
                foreach (Match lit in Regex.Matches(code, "\"[^\"]*\""))
                {
                    checkedLiterals++;
                    if (attr.IsMatch(lit.Value) && !lit.Value.Contains(' '))
                        Assert.Fail($"{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }
        Assert.True(checkedLiterals > 1000, $"only {checkedLiterals} literals scanned — wrong directory?");
    }

    [Fact]
    public void Scanner_NeverNamesKeyMaterialAttributes_OnlyUsesCheckFilters()
    {
        var dir = Path.Combine(ReadOnlyGuardTests.FindDesktopSourceDir(), "Core", "AD");
        var scannerFiles = Directory.GetFiles(dir, "AdScanner*.cs");
        Assert.Contains(scannerFiles, f => f.EndsWith("AdScanner.Checks23.cs"));
        foreach (var file in scannerFiles)
        {
            foreach (var line in File.ReadLines(file))
                foreach (Match lit in Regex.Matches(line.Split("//")[0], "\"[^\"]*\""))
                    Assert.DoesNotMatch(@"(?i)\b(msDS-KeyCredentialLink|altSecurityIdentities)\b", lit.Value);
        }
    }

    // ── S23-3 recoverability: backup age ──────────────────────────────────────
    private static string Meta(string attribute, string time) =>
        "<DS_REPL_ATTR_META_DATA>\n\t<pszAttributeName>" + attribute + "</pszAttributeName>\n\t<dwVersion>4</dwVersion>\n" +
        "\t<ftimeLastOriginatingChange>" + time + "</ftimeLastOriginatingChange>\n" +
        "\t<uuidLastOriginatingDsaInvocationID>0e3a7f3a-1111-2222-3333-444455556666</uuidLastOriginatingDsaInvocationID>\n" +
        "\t<usnOriginatingChange>12345</usnOriginatingChange>\n\t<usnLocalChange>12345</usnLocalChange>\n" +
        "\t<pszLastOriginatingDsaDN>CN=NTDS Settings,CN=DC01,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=corp,DC=test</pszLastOriginatingDsaDN>\n" +
        "</DS_REPL_ATTR_META_DATA>\n\0";

    [Fact]
    public void Backup30DaysAgo_IsAFinding()
    {
        var last = LastBackupUtc([Meta("whenChanged", "2026-10-18T10:00:00Z"), Meta("dSASignature", "2026-09-18T02:00:00Z")]);
        Assert.Equal(new DateTime(2026, 9, 18, 2, 0, 0, DateTimeKind.Utc), last);
        Assert.True(BackupTooOld(last, Now));
    }

    [Fact]
    public void RecentBackup_IsNoFinding()
    {
        var last = LastBackupUtc([Meta("dSASignature", "2026-10-17T02:00:00Z")]);
        Assert.False(BackupTooOld(last, Now));
        Assert.Equal(7, CheckFilters.BackupMaxDays);   // Annahme, mit Stefan klären
    }

    [Fact]
    public void NeverBackedUp_IsAFinding()
    {
        var last = LastBackupUtc([Meta("dSASignature", "1601-01-01T00:00:00Z")]);
        Assert.Equal(DateTime.MinValue, last);
        Assert.True(BackupTooOld(last, Now));
    }

    [Fact]
    public void MissingBackupMetadata_Throws_SoTheCheckIsFailed_NotPassed()
    {
        Assert.Throws<InvalidOperationException>(() => LastBackupUtc([]));
        Assert.Throws<InvalidOperationException>(() => LastBackupUtc([Meta("whenChanged", "2026-10-18T10:00:00Z")]));
        Assert.Throws<InvalidOperationException>(() => LastBackupUtc([Meta("dSASignature", "garbage")]));
    }

    [Fact]
    public async Task FailedBackupRead_MarksOnlyBackupOutdatedFailed_AndCannotRaiseTheScore()
    {
        var log = new ConcurrentBag<ScanLogEntry>();
        var failures = new ConcurrentDictionary<string, string>();
        var na = new ConcurrentDictionary<string, string>();
        // The scanner's Recovery.Backup sub-query calls LastBackupUtc on the NC metadata; here it is empty
        await ScanGuard.Run("Recovery.Backup", () => Task.Run(() =>
        {
            var last = LastBackupUtc([]);
            return BackupTooOld(last, Now) ? new List<AdFinding> { new() { Category = "BackupOutdated", Count = 1 } } : new List<AdFinding>();
        }), log, failures, na);

        var checks = ScanGuard.Evaluate(FindingCatalog.All, [], failures, na);
        var byCat  = checks.ToDictionary(c => c.Category);
        Assert.Equal(CheckStatus.Failed, byCat["BackupOutdated"].Status);
        Assert.Equal(CheckStatus.Passed, byCat["SysvolNtfrs"].Status);
        Assert.Single(checks, c => c.Status == CheckStatus.Failed);

        var score = ScoreModel.Compute(FindingCatalog.All, [], checks, new ScanTotals(100, 50, 40));
        Assert.True(score.IsUpperBound);
        Assert.Equal("≤ 100", score.Display);
    }

    // ── S23-3 recoverability: SYSVOL on NTFRS ─────────────────────────────────
    [Theory]
    [InlineData(48, false)]   // Eliminated
    [InlineData(0, true)]     // Start
    [InlineData(16, true)]    // Prepared
    [InlineData(32, true)]    // Redirected
    public void SysvolFlags_NotEliminated_IsAFinding(int flags, bool ntfrs) =>
        Assert.Equal(ntfrs, SysvolOnNtfrs(true, flags, frsSysvolReplicaSetFound: false));

    [Fact]
    public void NoDfsrObject_ButFrsReplicaSet_IsAFinding() => Assert.True(SysvolOnNtfrs(false, null, true));

    [Fact]
    public void SysvolStateUnknown_Throws_SoTheCheckIsFailed()
    {
        Assert.Throws<InvalidOperationException>(() => SysvolOnNtfrs(true, null, false));    // attribute missing
        Assert.Throws<InvalidOperationException>(() => SysvolOnNtfrs(false, null, false));   // nothing readable
    }

    // ── score: the new rules count ───────────────────────────────────────────
    [Fact]
    public void Score_PrivilegedSidHistoryObject_Costs25Points_BackupPresence_Costs18()
    {
        var checks = ScanGuard.Evaluate(FindingCatalog.All, [], new Dictionary<string, string>(), new Dictionary<string, string>());
        var totals = new ScanTotals(100, 50, 40);
        var sidHist = new AdFinding
        {
            Category = "PrivilegedSidHistory", Severity = FindingSeverity.Critical, Count = 1,
            Objects  = [new AffectedObject { Name = "mig", Sid = Dom + "-1200", Type = AffectedObjectType.User }],
        };
        Assert.Equal(75, ScoreModel.Compute(FindingCatalog.All, [sidHist], checks, totals).Score);

        var backup = new AdFinding
        {
            Category = "BackupOutdated", Severity = FindingSeverity.High, Count = 1,
            Objects  = [new AffectedObject { Name = "DC=corp,DC=test", Type = AffectedObjectType.DomainSetting }],
        };
        Assert.Equal(83, ScoreModel.Compute(FindingCatalog.All, [backup], checks, totals).Score);   // 100 − 2.5 × 7
    }
}
