using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;
using Xunit;
using static DirHealth.Desktop.Core.AD.Checks.CheckLogic;

namespace DirHealth.Tests;

// Sprint 19: new LDAP checks — evaluation logic, catalog entries, score version, Critical.
public class Sprint19Tests
{
    private static readonly DateTime Now = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] NewCategories =
    [
        "HiddenPrimaryGroup", "AdminNotProtected", "AdminKerberoastable", "AdminNoPreauth", "AdminOldPassword",
        "AdminWithMailbox", "OperatorGroupsNotEmpty", "KrbtgtPasswordOld", "GuestEnabled", "MachineAccountQuota",
        "PreWindows2000Access", "RecycleBinDisabled", "FunctionalLevelOld", "SingleDomainController", "DesOnly",
        "ServiceAccountsWithoutAes", "ReversibleEncryption", "UserPasswordAttribute", "DelegationToDomainControllers",
        "DelegationToMissingAccounts", "TrustWithoutSidFiltering", "TrustTgtDelegation", "TrustInactive",
    ];

    // ── catalog: each new check delivers RuleId, weight, texts, fix + revert ──
    [Fact]
    public void EveryNewCheck_IsCatalogued_AndMappedToAQuery()
    {
        Assert.Equal(23, NewCategories.Length);
        foreach (var c in NewCategories)
        {
            var r = FindingCatalog.Get(c);
            Assert.NotNull(r);
            Assert.InRange(r!.Weight, 1, 10);
            Assert.False(string.IsNullOrWhiteSpace(r.Fix), c);
            Assert.False(string.IsNullOrWhiteSpace(r.Revert), c);
            Assert.True(ScanGuard.QueryOf.ContainsKey(c), c);
        }
    }

    [Fact]
    public void TwoFilterRules_ShowBothFilters()
    {
        Assert.Contains(CheckFilters.HiddenPrimaryGroupComputers, FindingCatalog.Get("HiddenPrimaryGroup")!.Fix);
        Assert.Contains(CheckFilters.DesOnlyComputers, FindingCatalog.Get("DesOnly")!.Fix);
        Assert.Contains(CheckFilters.ReversibleComputers, FindingCatalog.Get("ReversibleEncryption")!.Fix);
        Assert.Contains(CheckFilters.RbcdOnDcOrKrbtgt, FindingCatalog.Get("DelegationToDomainControllers")!.Fix);
    }

    [Fact]
    public void AdminFixes_ComputeTier0BySid_IncludingPrimaryGroup()
    {
        foreach (var c in new[] { "AdminNotProtected", "AdminKerberoastable", "AdminNoPreauth", "AdminOldPassword", "AdminWithMailbox" })
        {
            var fix = FindingCatalog.Get(c)!.Fix;
            Assert.Contains("$sid-512", fix);
            Assert.Contains("(primaryGroupID=512)", fix);
            Assert.Contains("Where-Object Enabled", fix);
        }
    }

    [Fact]
    public void UserPasswordCheck_NeverReadsTheValue()
    {
        // presence filter only; the scanner never requests the attributes
        Assert.Contains("userPassword=*", CheckFilters.UserPasswordAttribute);
        var revert = FindingCatalog.Get("UserPasswordAttribute")!.Revert;
        Assert.Contains("never exported", revert);
    }

    [Fact]
    public void TrustRules_AreKeyedByName_ObjectRulesBySid()
    {
        Assert.False(FindingCatalog.Get("TrustInactive")!.KeyedBySid);
        Assert.True(FindingCatalog.Get("OperatorGroupsNotEmpty")!.KeyedBySid);
        Assert.False(FindingCatalog.Get("GuestEnabled")!.KeyedBySid);   // Presence rule
    }

    [Fact]
    public void ScoreFormulaVersion_WasBumped()
    {
        Assert.True(ScoreModel.FormulaVersion >= 3);   // bumped again by later sprints
    }

    // ── S19-1 hidden primary group ───────────────────────────────────────────
    [Theory]
    [InlineData(513, false)] [InlineData(514, false)] [InlineData(515, false)] [InlineData(516, false)] [InlineData(521, false)]
    [InlineData(512, true)]  [InlineData(1105, true)] [InlineData(0, false)]
    public void HiddenPrimaryGroup(int pgid, bool hidden) => Assert.Equal(hidden, HasHiddenPrimaryGroup(pgid));

    [Fact]
    public void HiddenPrimaryGroup_FilterExcludesTheDefaults()
    {
        Assert.Contains("(!(primaryGroupID=513))", CheckFilters.HiddenPrimaryGroupUsers);
        Assert.Contains("(!(primaryGroupID=514))", CheckFilters.HiddenPrimaryGroupUsers);
        foreach (var id in new[] { 515, 516, 521 })
            Assert.Contains($"(!(primaryGroupID={id}))", CheckFilters.HiddenPrimaryGroupComputers);
    }

    [Fact]
    public void Tier0_IncludesCertPublishers_AndKeyAdmins_NotRdp()
    {
        Assert.True(WellKnownGroups.IsTier0Sid("S-1-5-21-1-2-3-517"));
        var rids = WellKnownGroups.Tier0.Select(d => d.Rid).ToHashSet();
        Assert.Contains(WellKnownGroups.KeyAdminsRid, rids);
        Assert.Contains(WellKnownGroups.EnterpriseKeyAdminsRid, rids);
        Assert.Contains(WellKnownGroups.CertPublishersRid, rids);
        Assert.DoesNotContain(WellKnownGroups.RemoteDesktopUsersRid, rids);
        Assert.Contains(WellKnownGroups.Tier0, d => d.FallbackName == "DnsAdmins" && d.Source == GroupSidSource.NameOnly);
    }

    // ── S19-2 admin hardening ────────────────────────────────────────────────
    private static AdUser Admin(int uac = 0x200, bool spn = false, DateTime? pwd = null, bool never = false) => new()
    {
        SamAccountName = "admin1", UserAccountControl = uac, HasSpn = spn, IsEnabled = true,
        PasswordLastSet = pwd ?? Now.AddDays(-10), PasswordNeverExpires = never,
    };

    [Fact]
    public void Admin_Hardened_HasNoIssue()
    {
        var a = Admin(uac: 0x200 | UacFlags.NotDelegated);
        Assert.Equal(AdminIssue.None, EvaluateAdmin(new(a, InProtectedUsers: false, HasMailbox: false), Now));
        Assert.Equal(AdminIssue.None, EvaluateAdmin(new(Admin(), InProtectedUsers: true, HasMailbox: false), Now));
    }

    [Fact]
    public void Admin_AllIssuesDetected()
    {
        var a = Admin(uac: 0x200 | UacFlags.DontRequirePreauth, spn: true, never: true);
        var i = EvaluateAdmin(new(a, InProtectedUsers: false, HasMailbox: true), Now);
        Assert.True(i.HasFlag(AdminIssue.NotProtected));
        Assert.True(i.HasFlag(AdminIssue.Kerberoastable));
        Assert.True(i.HasFlag(AdminIssue.NoPreauth));
        Assert.True(i.HasFlag(AdminIssue.OldPassword));
        Assert.True(i.HasFlag(AdminIssue.Mailbox));
    }

    [Theory]
    [InlineData(366, true)] [InlineData(364, false)]
    public void Admin_PasswordAge(int days, bool old)
    {
        var i = EvaluateAdmin(new(Admin(uac: 0x200 | UacFlags.NotDelegated, pwd: Now.AddDays(-days)), false, false), Now);
        Assert.Equal(old, i.HasFlag(AdminIssue.OldPassword));
    }

    // ── S19-3 domain basics ──────────────────────────────────────────────────
    [Theory]
    [InlineData(181, true)] [InlineData(179, false)]
    public void Krbtgt_Age(int days, bool old) => Assert.Equal(old, KrbtgtPasswordTooOld(Now.AddDays(-days), Now));

    [Fact]
    public void Krbtgt_Unknown_IsOld() => Assert.True(KrbtgtPasswordTooOld(null, Now));

    [Theory]
    [InlineData("CN=S-1-5-11,CN=ForeignSecurityPrincipals,DC=corp,DC=com", "Authenticated Users")]
    [InlineData("CN=S-1-5-7,CN=ForeignSecurityPrincipals,DC=corp,DC=com",  "Anonymous Logon")]
    [InlineData("CN=S-1-1-0,CN=ForeignSecurityPrincipals,DC=corp,DC=com",  "Everyone")]
    [InlineData("CN=Some Service,OU=Svc,DC=corp,DC=com", null)]
    public void PreWin2000_RiskyMembers(string dn, string? expected) => Assert.Equal(expected, RiskyPreWin2000Member(dn));

    [Theory]
    [InlineData(6, "Windows Server 2012 R2")] [InlineData(7, "Windows Server 2016")] [InlineData(10, "Windows Server 2025")]
    public void FunctionalLevelNames(int level, string name) => Assert.Equal(name, FunctionalLevelName(level));

    [Fact]
    public void FunctionalLevel_Threshold_Is2016() => Assert.Equal(7, MinFunctionalLevel);

    // ── S19-4 Kerberos encryption ────────────────────────────────────────────
    [Theory]
    [InlineData(null, true)] [InlineData(0x4, true)] [InlineData(0x1C, false)] [InlineData(0x8, false)] [InlineData(0x10, false)]
    public void ServiceAccount_Aes(int? enc, bool lacks) => Assert.Equal(lacks, LacksAes(enc));

    // ── S19-5 delegation and trusts ──────────────────────────────────────────
    [Theory]
    [InlineData("cifs/dc01.corp.com", true)]
    [InlineData("ldap/DC01", true)]
    [InlineData("krbtgt/CORP.COM", true)]
    [InlineData("http/web01.corp.com:8080", false)]
    public void Delegation_ToDomainController(string spn, bool toDc) =>
        Assert.Equal(toDc, TargetsDomainController(spn, ["dc01.corp.com", "DC01"]));

    [Fact]
    public void Delegation_MissingSpn_ChecksHostFallback()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HOST/app01.corp.com", "MSSQLSvc/sql01.corp.com:1433" };
        Assert.False(SpnIsMissing("cifs/app01.corp.com", existing.Contains));        // covered by HOST/
        Assert.False(SpnIsMissing("MSSQLSvc/sql01.corp.com:1433", existing.Contains));
        Assert.True(SpnIsMissing("http/gone.corp.com", existing.Contains));
    }

    [Fact]
    public void Trust_ExternalWithoutQuarantine_IsUnfiltered()
    {
        var external = new TrustFacts("partner.com", TrustAttributes: 0, TrustDirection: 3, Now.AddDays(-5));
        Assert.True(EvaluateTrust(external, Now).HasFlag(TrustIssue.NoSidFiltering));
        var quarantined = external with { TrustAttributes = TrustQuarantinedDomain };
        Assert.False(EvaluateTrust(quarantined, Now).HasFlag(TrustIssue.NoSidFiltering));
    }

    [Fact]
    public void Trust_ForestWithSidHistory_IsUnfiltered_DefaultForestIsFine()
    {
        var forest = new TrustFacts("other.com", TrustForestTransitive, 2, Now);
        Assert.Equal(TrustIssue.None, EvaluateTrust(forest, Now));
        Assert.True(EvaluateTrust(forest with { TrustAttributes = TrustForestTransitive | TrustTreatAsExternal }, Now).HasFlag(TrustIssue.NoSidFiltering));
        Assert.True(EvaluateTrust(forest with { TrustAttributes = TrustForestTransitive | TrustEnableTgtDelegation }, Now).HasFlag(TrustIssue.TgtDelegation));
    }

    [Fact]
    public void Trust_IncomingOnly_IsNotAbusableHere_ButCanBeInactive()
    {
        var incoming = new TrustFacts("in.com", 0, TrustDirection: 1, Now.AddDays(-200));
        var i = EvaluateTrust(incoming, Now);
        Assert.False(i.HasFlag(TrustIssue.NoSidFiltering));
        Assert.True(i.HasFlag(TrustIssue.Inactive));
    }

    [Fact]
    public void Trust_WithinForest_IsIgnored()
    {
        var child = new TrustFacts("child.corp.com", TrustWithinForest, 3, Now.AddDays(-400));
        Assert.Equal(TrustIssue.None, EvaluateTrust(child, Now));
    }

    // ── S19-6 Critical ───────────────────────────────────────────────────────
    [Fact]
    public void SeverityFilter_KnowsCritical()
    {
        var vm = new FindingsViewModel();
        Assert.Contains("Critical", vm.SeverityOptions);
        vm.LoadFindings(
        [
            new AdFinding { Category = "AdminNoPreauth", Severity = FindingSeverity.Critical, Title = "c" },
            new AdFinding { Category = "InactiveUsers", Severity = FindingSeverity.Medium, Title = "m" },
        ]);
        vm.SeverityFilter = "Critical";
        Assert.Equal("c", Assert.Single(vm.FilteredFindings).Title);
    }
}
