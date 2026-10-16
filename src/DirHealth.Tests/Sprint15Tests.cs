using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.ViewModels;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;

namespace DirHealth.Tests;

// Sprint 15: search table/limit/prefixes, user detail, dossier, quick filters.
[Collection(ExportHookCollection.Name)]
public class Sprint15Tests
{
    // ── S15-3 prefixes ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("user:jdoe",        SearchScopeType.User,               "jdoe")]
    [InlineData("USER:jdoe",        SearchScopeType.User,               "jdoe")]
    [InlineData("  Group: Admins ", SearchScopeType.Group,              "Admins")]
    [InlineData("computer:WS01",    SearchScopeType.Computer,           "WS01")]
    [InlineData("ou:Finance",       SearchScopeType.OrganizationalUnit, "Finance")]
    [InlineData("user:",            SearchScopeType.User,               "")]
    [InlineData("jdoe",             SearchScopeType.Any,                "jdoe")]
    [InlineData("users:x",          SearchScopeType.Any,                "users:x")]   // not a prefix
    public void Parse_RecognisesPrefixes(string query, SearchScopeType type, string term)
    {
        var (t, rest) = SearchQuery.Parse(query);
        Assert.Equal(type, t);
        Assert.Equal(term, rest);
    }

    [Fact]
    public void NameFilter_RestrictsTypeAndKeepsEscapedTerm()
    {
        var escaped = AdConnector.EscapeFilterValue("a*)(b");
        var filter  = SearchQuery.NameFilter(escaped, SearchScopeType.Group);
        Assert.StartsWith("(&(objectClass=group)(|(cn=*", filter);
        Assert.Contains("a\\2a\\29\\28b", filter);
        Assert.DoesNotContain("a*)(b", filter);
        Assert.DoesNotContain("objectClass", SearchQuery.NameFilter("x", SearchScopeType.Any));
    }

    [Theory]
    [InlineData(SearchMode.Sid,   "user:jdoe", SearchMode.Name)]
    [InlineData(SearchMode.Email, "group:x",   SearchMode.Name)]
    [InlineData(SearchMode.Ldap,  "user:jdoe", SearchMode.Ldap)]   // raw LDAP is never reinterpreted
    [InlineData(SearchMode.Sid,   "S-1-5-21",  SearchMode.Sid)]
    public void EffectiveMode_PrefixSwitchesToNameSearch(SearchMode selected, string query, SearchMode expected)
    {
        Assert.Equal(expected, AdSearchViewModel.EffectiveMode(selected, query));
    }

    // ── S15-1 limit ──────────────────────────────────────────────────────────
    [Fact]
    public void ApplyLimit_501Rows_Shows500WithHint()
    {
        var rows = Enumerable.Range(0, AdSearcher.MaxResults + 1).Select(_ => new AdSearchResult()).ToList();
        var (shown, hint) = AdSearchViewModel.ApplyLimit(rows);
        Assert.Equal(500, shown.Count);
        Assert.Equal("Showing 500 of more than 500 — narrow your search", hint);
    }

    [Fact]
    public void ApplyLimit_Exactly500_NoHint()
    {
        var rows = Enumerable.Range(0, 500).Select(_ => new AdSearchResult()).ToList();
        Assert.Equal("", AdSearchViewModel.ApplyLimit(rows).Hint);
    }

    [Fact]
    public void SearchResults_HaveIconAndTooltipColumn()
    {
        var type = ViewColumns.SearchResults[0];
        Assert.Equal(nameof(AdSearchResult.TypeIcon), type.BindingPath);
        Assert.Equal(nameof(AdSearchResult.TypeLabel), type.ToolTipPath);
        Assert.Equal("Group", type.ExportValue(new AdSearchResult { ObjectType = AdObjectType.Group }));
    }

    // ── S15-4 / S15-5 group paths ────────────────────────────────────────────
    private static AdUserGroup G(string name, GroupMembershipKind kind, params string[] parents) =>
        new() { Name = name, DistinguishedName = $"CN={name},DC=x", Kind = kind,
                ParentDns = parents.Select(p => $"CN={p},DC=x").ToList() };

    [Fact]
    public void Paths_MultiLevelNesting()
    {
        var groups = new List<AdUserGroup>
        {
            G("IT-Admins",     GroupMembershipKind.Direct, "Server-Admins"),
            G("Server-Admins", GroupMembershipKind.Nested, "Domain Admins"),
            G("Domain Admins", GroupMembershipKind.Nested),
        };
        GroupPaths.Assign("jdoe", groups);
        Assert.Equal("jdoe → IT-Admins", groups[0].Path);
        Assert.Equal("jdoe → IT-Admins → Server-Admins → Domain Admins", groups[2].Path);
    }

    [Fact]
    public void Paths_CycleTerminates()
    {
        var groups = new List<AdUserGroup>
        {
            G("A", GroupMembershipKind.Direct, "B"),
            G("B", GroupMembershipKind.Nested, "A"),   // A ↔ B
        };
        GroupPaths.Assign("u", groups);                 // must not loop
        Assert.Equal("u → A → B", groups[1].Path);
    }

    [Fact]
    public void Paths_ShortestWins_AndPrimaryIsMarked()
    {
        var groups = new List<AdUserGroup>
        {
            G("Domain Users", GroupMembershipKind.Primary, "Target"),
            G("Long1",        GroupMembershipKind.Direct,  "Long2"),
            G("Long2",        GroupMembershipKind.Nested,  "Target"),
            G("Target",       GroupMembershipKind.Nested),
        };
        GroupPaths.Assign("u", groups);
        Assert.Equal("u → Domain Users (primary) → Target", groups[3].Path);
    }

    [Fact]
    public void Paths_UnreachableGroup_GetsEllipsis()
    {
        var groups = new List<AdUserGroup> { G("Orphan", GroupMembershipKind.Nested) };
        GroupPaths.Assign("u", groups);
        Assert.Equal("u → … → Orphan", groups[0].Path);
    }

    // ── Tier 0 by SID ────────────────────────────────────────────────────────
    [Theory]
    [InlineData("S-1-5-21-1-2-3-512", true)]
    [InlineData("S-1-5-21-9-9-9-519", true)]
    [InlineData("S-1-5-21-1-2-3-526", true)]
    [InlineData("S-1-5-32-544",       true)]
    [InlineData("S-1-5-32-551",       true)]
    [InlineData("S-1-5-21-1-2-3-513", false)]   // Domain Users
    [InlineData("S-1-5-32-555",       false)]   // Remote Desktop Users
    [InlineData("S-1-5-21-1-2-3-1512",false)]   // RID ends in 512 but is not 512
    [InlineData("",                   false)]
    public void IsTier0Sid(string sid, bool expected)
    {
        Assert.Equal(expected, WellKnownGroups.IsTier0Sid(sid));
    }

    // ── S15-4 detail rows ────────────────────────────────────────────────────
    [Fact]
    public void DetailRows_LabelBothLogonFieldsHonestly()
    {
        var rows = UserDetailRows.Build(new AdUser { SamAccountName = "x" }, null);
        Assert.Contains(rows, r => r.Label == "Last Logon (replicated, ±14 d)" && r.Value == "No logon recorded");
        Assert.Contains(rows, r => r.Label == "Last Logon (this DC)"           && r.Value == "No logon on this DC");
    }

    [Fact]
    public void DetailRows_FlagRiskyValues()
    {
        var u = new AdUser
        {
            SamAccountName = "svc", IsEnabled = true, HasSpn = true, Spns = ["HTTP/web"],
            UserAccountControl = UacFlags.DontRequirePreauth | 0x200, Delegation = "Unconstrained",
        };
        var rows = UserDetailRows.Build(u, null);
        Assert.True(rows.Single(r => r.Label == "Kerberoastable").Warn);
        Assert.True(rows.Single(r => r.Label == "Pre-Auth Not Required (AS-REP)").Warn);
        Assert.True(rows.Single(r => r.Label == "Delegation").Warn);
        Assert.Equal("DONT_REQ_PREAUTH, NORMAL_ACCOUNT".Split(", ").OrderBy(x => x),
                     rows.Single(r => r.Label == "UAC Flags").Value.Split(", ").OrderBy(x => x));
    }

    [Fact]
    public void FindingsFor_MatchesSamOrDn_CaseInsensitive()
    {
        var u = new AdUser { SamAccountName = "jdoe", DistinguishedName = "CN=J,DC=x" };
        var findings = new List<AdFinding>
        {
            new() { Title = "a", AffectedObjects = ["JDOE"], Severity = FindingSeverity.Low },
            new() { Title = "b", AffectedObjects = ["cn=j,dc=x"], Severity = FindingSeverity.High },
            new() { Title = "c", AffectedObjects = ["someone"] },
        };
        Assert.Equal(["b", "a"], UserDetailRows.FindingsFor(u, findings).Select(f => f.Title));
    }

    // ── S15-5 dossier CSVs ───────────────────────────────────────────────────
    private static UserDossier Dossier(int groups, int findings) => new(
        new AdUser { SamAccountName = "jdoe", DisplayName = "J Doe" },
        UserDetailRows.Build(new AdUser { SamAccountName = "jdoe" }, null),
        Enumerable.Range(0, groups).Select(i => new AdUserGroup
            { Name = $"G{i}", DistinguishedName = $"CN=G{i},DC=x", Path = $"jdoe → G{i}", Sid = "S-1-5-21-1-2-3-512" }).ToList(),
        Enumerable.Range(0, findings).Select(i => new AdFinding { Title = $"F{i}", Category = "InactiveUsers" }).ToList(),
        new ExportContext("corp.com", "DC1", "CORP\\a"), "2026-10-13", DateTime.UtcNow, "test");

    [Fact]
    public void Dossier_WritesGroupAndFindingCsvsWithMeta()
    {
        var dir = Directory.CreateTempSubdirectory("dh-dossier-");
        try
        {
            var pdf   = Path.Combine(dir.FullName, "jdoe.pdf");
            var files = UserDossierExporter.WriteCsvs(Dossier(groups: 3, findings: 2), pdf);
            Assert.Equal(2, files.Count);
            var groupsCsv = File.ReadAllLines(UserDossierExporter.GroupsCsvPath(pdf));
            Assert.Equal(4, groupsCsv.Length);                   // header + 3 groups
            Assert.Contains("jdoe → G1", groupsCsv[2]);
            Assert.Contains("TRUE", groupsCsv[1]);                // Tier 0 column
            foreach (var f in files)
                Assert.Contains(ExportMetadata.Sha256OfFile(f), File.ReadAllText(ExportMetadata.MetaPathFor(f)));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Dossier_EmptyLists_WriteNoCsv()
    {
        var dir = Directory.CreateTempSubdirectory("dh-dossier-");
        try
        {
            Assert.Empty(UserDossierExporter.WriteCsvs(Dossier(0, 0), Path.Combine(dir.FullName, "x.pdf")));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Dossier_IsTier0_FromGroups()
    {
        Assert.True(Dossier(1, 0).IsTier0);
        Assert.False(Dossier(0, 0).IsTier0);
    }

    [Fact]
    public void Dossier_Pdf_IsWritten()
    {
        var dir = Directory.CreateTempSubdirectory("dh-dossier-");
        try
        {
            var pdf   = Path.Combine(dir.FullName, "jdoe.pdf");
            var files = UserDossierExporter.Export(Dossier(40, 12), pdf);   // enough rows to force page breaks
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf)[..4]));
            Assert.Equal(3, files.Count);
            Assert.True(File.Exists(ExportMetadata.MetaPathFor(pdf)));
        }
        finally { dir.Delete(recursive: true); }
    }

    // Follow-up 2026-10-16: the dossier PDF's content, not just its existence (S15-5 AC)
    [Fact]
    public void Dossier_Pdf_ContainsPathsFixesMetadata_AndContinuesAcrossPages()
    {
        var dir = Directory.CreateTempSubdirectory("dh-dossier-");
        try
        {
            var d = Dossier(40, 0) with
            {
                Findings = [new AdFinding { Title = "Kerberoastable account", Category = "KerberoastableAccounts",
                                            Severity = FindingSeverity.High }],
            };
            var lines = new List<string>();
            var pdf = Path.Combine(dir.FullName, "jdoe.pdf");
            new PdfExporter().ExportUserDossier(d, pdf, lines.Add);

            Assert.Contains("jdoe → G39", lines);                                   // membership path of the last group
            Assert.Contains(lines, l => l.StartsWith("GROUP MEMBERSHIPS (40)"));
            Assert.Contains(lines, l => l.EndsWith("(continued)"));                 // section title repeated after a page break
            Assert.Contains("HIGH  Kerberoastable account", lines);
            var fix = FindingCatalog.Get("KerberoastableAccounts")!.HowToFix;     // remediation comes from the catalog
            Assert.Contains(lines, l => l.Length > "→ Fix: ".Length && ("→ Fix: " + fix).StartsWith(l.TrimEnd()));   // first wrapped line
            foreach (var meta in new[] { "corp.com", "DC1", "CORP\\a", "2026-10-13", "DirHealth test" })
                Assert.Contains(meta, lines);
            Assert.Contains("Last Logon (this DC)", lines);                         // attributes section
        }
        finally { dir.Delete(recursive: true); }
    }

    // ── S15-6 quick filters ──────────────────────────────────────────────────
    [Fact]
    public void QuickFilters_CombineWithAnd()
    {
        var now = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        var admin = new AdUser { IsEnabled = true, AdminCount = true, LastLogon = now.AddDays(-200) };
        Assert.True(QuickFilters.Matches(admin, new(Admin: true, Inactive: true), now));
        Assert.False(QuickFilters.Matches(admin, new(Admin: true, Disabled: true), now));
        Assert.True(QuickFilters.Matches(admin, new(), now));        // no chip = no restriction
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(91.0, true)]
    [InlineData(89.0, false)]
    public void QuickFilters_Inactive90(double? daysAgo, bool expected)
    {
        var now = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        DateTime? last = daysAgo is { } d ? now.AddDays(-d) : null;
        Assert.Equal(expected, QuickFilters.IsInactive(last, now));
    }

    [Fact]
    public void UserBrowser_ChipNarrowsTableAndCounter()
    {
        var vm = new UserBrowserViewModel();
        vm.Table.SetRows(
        [
            new AdUser { SamAccountName = "a", IsEnabled = true },
            new AdUser { SamAccountName = "b", IsEnabled = false },
        ]);
        vm.ChipDisabled = true;
        Assert.Equal("1 of 2", vm.Table.CountLabel);
        vm.ChipDisabled = false;
        Assert.Equal("2 items", vm.Table.CountLabel);
    }

    [Fact]
    public void ComputerBrowser_ChipInactive()
    {
        var vm = new ComputerBrowserViewModel();
        vm.Table.SetRows(
        [
            new AdComputer { Name = "old", LastLogon = DateTime.UtcNow.AddDays(-400) },
            new AdComputer { Name = "new", LastLogon = DateTime.UtcNow },
        ]);
        vm.ChipInactive = true;
        Assert.Equal("old", Assert.Single(vm.Table.FilteredRows).Name);
    }
}
