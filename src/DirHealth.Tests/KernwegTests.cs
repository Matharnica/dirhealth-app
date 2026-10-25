using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Export.Reports;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;

namespace DirHealth.Tests;

/// <summary>
/// Kernweg tests (PROCESS.md P7c, docs/ziellinie.md): one chain per core path K01–K10, from
/// in-memory scan data up to exactly before the AD boundary, asserting what the user finally
/// sees or receives (view-model state, exported file, PDF text). Run alone with
/// <c>dotnet test --filter Category=Kernweg</c>.
///
/// What they do NOT check: a real Active Directory (no LDAP/WMI answer is ever read), a real
/// click in the running app, the installer, opening a CSV in Excel, how a PDF looks. Those
/// steps are listed as skipped tests below ("manuell (AD)") and in the verification checklist —
/// skipped is not green; the test output counts both separately.
/// </summary>
[Collection(ExportHookCollection.Name)]
public class KernwegTests : IDisposable
{
    private const string K = "Kernweg";

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dh-kernweg-");
    private readonly Func<string, ExportFormat, string?> _savedPicker = TableViewModel.PickSavePathFor;
    private readonly ColumnLayoutStore _savedLayout = TableViewModel.LayoutStore;
    private readonly CultureInfo _savedCulture = CultureInfo.CurrentCulture;

    public KernwegTests() => TableViewModel.LayoutStore = new ColumnLayoutStore(P("columns.json"));

    public void Dispose()
    {
        TableViewModel.PickSavePathFor = _savedPicker;
        TableViewModel.LayoutStore     = _savedLayout;
        CultureInfo.CurrentCulture     = _savedCulture;
        _dir.Delete(recursive: true);
    }

    private string P(string name) => Path.Combine(_dir.FullName, name);

    private static AffectedObject User(string sam, int rid) => new()
    {
        Name = sam, Sid = $"S-1-5-21-1-2-3-{rid}", DistinguishedName = $"CN={sam},OU=Staff,DC=corp,DC=com",
        Type = AffectedObjectType.User,
    };

    private static AdFinding Finding(string category, FindingSeverity sev, params AffectedObject[] objects) =>
        new() { Category = category, Title = $"{objects.Length} {category}", Severity = sev, Count = objects.Length, Objects = [.. objects] };

    private static List<AdFinding> ScanFindings() =>
    [
        Finding("AdminKerberoastable", FindingSeverity.Critical, User("svc-sql", 1101)),
        Finding("AsRepRoasting",       FindingSeverity.High,     User("jdoe", 1102), User("amueller", 1103)),
        Finding("InactiveComputers",   FindingSeverity.Low,      new AffectedObject { Name = "WS-OLD01", Type = AffectedObjectType.Computer }),
    ];

    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    private static string[] CsvLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "CSV without UTF-8 BOM — Excel would read it as ANSI");
        return File.ReadAllLines(path, Encoding.UTF8);
    }

    private static void AssertMetaMatches(string path) =>
        Assert.Contains(ExportMetadata.Sha256OfFile(path), File.ReadAllText(ExportMetadata.MetaPathFor(path)));

    // ── K01 Installieren, anmelden, aktualisieren ──────────────────────────────
    private sealed class RecordingHandler(Dictionary<string, string> bodies) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            var body = bodies.FirstOrDefault(b => request.RequestUri!.ToString().Contains(b.Key)).Value ?? "";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static string Release(string checksumHost) => $$"""
        [{"tag_name":"v99.1.0","draft":false,"prerelease":false,"html_url":"https://github.com/Matharnica/dirhealth-app/releases/tag/v99.1.0",
          "assets":[{"browser_download_url":"https://github.com/Matharnica/dirhealth-app/releases/download/v99.1.0/DirHealth-Setup-99.1.0.exe","size":4242},
                    {"browser_download_url":"https://{{checksumHost}}/DirHealth-Setup-99.1.0.sha256","size":64}]}]
        """;

    [Fact, Trait("Category", K)]
    public async Task K01_UpdateCheck_FindsNewVersion_FetchesChecksumOnlyFromGitHub()
    {
        // pinned host: the checksum is fetched and handed to the installer download
        var good = new RecordingHandler(new() { ["api.github.com"] = Release("github.com"), [".sha256"] = "ABCDEF0123\n" });
        var (update, diag) = await new UpdateChecker(new HttpClient(good)).CheckAsync();
        Assert.NotNull(update);
        Assert.Equal("99.1.0", update.Version);
        Assert.True(update.HasDirectDownload);
        Assert.Equal(4242, update.FileSize);
        Assert.Equal("abcdef0123", update.ExpectedSha256);
        Assert.Equal("Update found: 99.1.0", diag);

        // foreign host: never contacted, no checksum taken from it
        var evil = new RecordingHandler(new() { ["api.github.com"] = Release("evil.example.com"), [".sha256"] = "deadbeef" });
        var (update2, _) = await new UpdateChecker(new HttpClient(evil)).CheckAsync();
        Assert.NotNull(update2);
        Assert.Null(update2.ExpectedSha256);
        Assert.DoesNotContain(evil.Requests, u => u.Host == "evil.example.com");
    }

    private static readonly (string Text, string Back)[] ContrastPairs =
    [
        ("TextPrimaryColor", "BackgroundColor"), ("TextPrimaryColor", "SurfaceColor"),
        ("TextSecondaryColor", "BackgroundColor"), ("TextSecondaryColor", "SurfaceColor"),
    ];

    private static double Luminance(Color c)
    {
        static double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    [Fact, Trait("Category", K)]
    public void K01_BothThemes_Load_SameBrushKeys_TextReadable()
    {
        OnSta(() =>
        {
            var dir = Path.Combine(ReadOnlyGuardTests.FindDesktopSourceDir(), "Resources", "Themes");
            var dark  = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(dir, "Dark.xaml")));
            var light = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(dir, "Light.xaml")));

            // every brush a view can ask for exists in both themes
            static HashSet<string> Brushes(ResourceDictionary d) =>
                d.Keys.Cast<object>().Select(k => k.ToString()!).Where(k => k.EndsWith("Brush", StringComparison.Ordinal)).ToHashSet();
            Assert.Equal(Brushes(dark).OrderBy(k => k), Brushes(light).OrderBy(k => k));

            // WCAG AA (4.5:1) for body and secondary text on both backgrounds
            foreach (var (name, theme) in new[] { ("Dark", dark), ("Light", light) })
                foreach (var (text, back) in ContrastPairs)
                {
                    double a = Luminance((Color)theme[text]), b = Luminance((Color)theme[back]);
                    var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
                    Assert.True(ratio >= 4.5, $"{name}: {text} on {back} = {ratio:0.00}:1");
                }
        });
    }

    [Fact(Skip = "Befund: Light-Theme nicht wählbar — ThemeManager.Apply lädt immer Dark.xaml (Core/Theme/ThemeManager.cs:13), Settings hat keinen Umschalter. Geparkt (Entscheid 2026-10-25), kein Bau vor v3.0.0."), Trait("Category", K)]
    public void K01_LightTheme_IsSelectable() { }

    [Fact(Skip = "manuell: Installer, Login gegen echte Domäne, DPAPI-Zugangsdaten nach Neustart, Download des Installers (Host-Pinning in MainViewModel) — docs/verification-checklist.md K01"), Trait("Category", K)]
    public void K01_Manual_InstallLoginRestartDownload() { }

    // ── K02 Scan und Score ────────────────────────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K02_ScanFindings_GiveScore_BreakdownAndCountsPerSeverity_InclCritical()
    {
        var findings = ScanFindings();
        var totals   = new ScanTotals(100, 50, 40);

        var score = ScoreModel.Compute(FindingCatalog.All, findings, [], totals);
        Assert.InRange(score.Score, 0, 99);
        Assert.Equal(score.Score.ToString(), score.Display);

        // dashboard: "+X if fixed" sums to the missing points, largest first, Critical rule included
        var breakdown = DashboardViewModel.BreakdownOf(score);
        Assert.Equal(100 - score.Score, breakdown.Sum(r => r.PointsIfFixed));
        Assert.Equal(breakdown.Max(r => r.PointsIfFixed), breakdown[0].PointsIfFixed);
        Assert.Contains(breakdown, r => r.Category == "AdminKerberoastable");
        Assert.Equal("", DashboardViewModel.NoteFor(score, "2026-10-18 09:00"));

        // findings view: Critical first and filterable
        var vm = new FindingsViewModel(new ExceptionStore(P("exceptions.json")), new AcknowledgeStore(P("ack.json")));
        vm.LoadFindings(findings, score.Score);
        Assert.Equal(FindingSeverity.Critical, vm.FilteredFindings[0].Severity);
        foreach (var sev in new[] { "Critical", "High", "Low" })
        {
            vm.SeverityFilter = sev;
            Assert.Single(vm.FilteredFindings);
        }
        vm.SeverityFilter = "Medium";
        Assert.Empty(vm.FilteredFindings);

        // report: counts per severity, Critical included
        var input = new ReportInput("corp.com", findings, score, score.Score, [], [], null, null, [], [], [], DateTime.Now, "test");
        var m = ReportBuilder.Build(input, new ReportOptions(ReportKind.Summary, false));
        Assert.Equal([("Critical", 1), ("High", 1), ("Medium", 0), ("Low", 1)], m.SeverityCounts);
    }

    [Fact, Trait("Category", K)]
    public void K02_FailedCheck_ScoreIsUpperBound_AndSaysSo()
    {
        var findings = ScanFindings();
        var checks   = new List<CheckResult> { new("AsRepRoasting", FindingCatalog.Get("AsRepRoasting")!.RuleId, CheckStatus.Failed, "LDAP timeout", 0) };
        var full     = ScoreModel.Compute(FindingCatalog.All, findings, [], new ScanTotals(100, 50, 40));
        var partial  = ScoreModel.Compute(FindingCatalog.All, findings, checks, new ScanTotals(100, 50, 40));

        Assert.True(partial.Score >= full.Score);                       // a failed check never lowers the shown risk honestly …
        Assert.StartsWith("≤ ", partial.Display);                       // … so the number is marked as an upper bound
        Assert.Contains("Upper bound", DashboardViewModel.NoteFor(partial, "2026-10-18 09:00"));
        Assert.Contains("1 failed", DashboardViewModel.Summarize(checks));
    }

    [Fact(Skip = "manuell (AD): Scan nach Login und geplanter Auto-Scan gegen echte Domäne — docs/verification-checklist.md K02"), Trait("Category", K)]
    public void K02_Manual_ScanAgainstRealDomain() { }

    // ── K03 Findings abarbeiten ───────────────────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K03_ExceptionPerObject_SurvivesRestart_ExpiredCountsAgain()
    {
        // session 1: the admin excepts one of two objects
        var vm1 = new FindingsViewModel(new ExceptionStore(P("exceptions.json")), new AcknowledgeStore(P("ack.json")));
        vm1.LoadFindings(ScanFindings());
        vm1.SelectedFinding = vm1.Findings.Single(f => f.Category == "AsRepRoasting");
        vm1.ObjectsTable.SelectedItem = vm1.SelectedFinding.Objects.Single(o => o.Name == "jdoe");
        vm1.ExceptionReason = "legacy Java client, ticket 4711";
        vm1.AddExceptionCommand.Execute(null);
        Assert.Equal(3, vm1.OpenFindingsCount);                         // amueller is still open

        // session 2 (restart, fresh scan objects): the exception is still there and greys the object
        var store2 = new ExceptionStore(P("exceptions.json"));
        var vm2 = new FindingsViewModel(store2, new AcknowledgeStore(P("ack.json"))) { HideExceptions = true };
        vm2.LoadFindings(ScanFindings());
        vm2.SelectedFinding = vm2.Findings.Single(f => f.Category == "AsRepRoasting");
        var jdoe = vm2.SelectedFinding.Objects.Single(o => o.Name == "jdoe");
        Assert.True(jdoe.IsExcepted);
        Assert.Contains("legacy Java client", jdoe.ExceptionLabel);
        Assert.Equal(["amueller"], vm2.ObjectsTable.FilteredRows.Select(o => o.Name));

        // the other object's exception has expired → it counts again
        var rule = FindingCatalog.Get("AsRepRoasting")!;
        var amueller = vm2.SelectedFinding.Objects.Single(o => o.Name == "amueller");
        store2.Add(rule.RuleId, ExceptionKey.For(rule, amueller)!, "amueller", "migration window",
                   DateTime.UtcNow.AddDays(-1), "CORP\\admin", DateTime.UtcNow.AddDays(-30));
        var vm3 = new FindingsViewModel(new ExceptionStore(P("exceptions.json")), new AcknowledgeStore(P("ack.json")));
        vm3.LoadFindings(ScanFindings());
        var asrep = vm3.Findings.Single(f => f.Category == "AsRepRoasting");
        Assert.Equal(ExceptionState.Expired, asrep.Objects.Single(o => o.Name == "amueller").Exception);
        Assert.False(asrep.IsAcknowledged);
        Assert.Equal(3, vm3.OpenFindingsCount);

        // card shows the fix from the catalog
        vm3.SelectedFinding = asrep;
        Assert.False(string.IsNullOrWhiteSpace(vm3.SelectedRule!.Fix));
    }

    [Fact(Skip = "manuell (AD): Findings-Liste mit echten Scan-Daten durchklicken — docs/verification-checklist.md K03"), Trait("Category", K)]
    public void K03_Manual_ClickThroughRealFindings() { }

    // ── K04 User-Export (Liste) ───────────────────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K04_UserList_CsvForGermanExcel_FilteredAndFull_WithFormulaGuardAndMeta()
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");          // Excel splits on the region's list separator
        var users = new List<AdUser>
        {
            new() { SamAccountName = "jmueller", DisplayName = "Jürgen Müller", IsEnabled = true,  DistinguishedName = "CN=Jürgen Müller,OU=Büro,DC=corp,DC=com", Sid = "S-1-5-21-1-2-3-2001" },
            new() { SamAccountName = "evil",     DisplayName = "=1+2",          IsEnabled = true,  DistinguishedName = "CN=evil,DC=corp,DC=com",                Sid = "S-1-5-21-1-2-3-2002" },
            new() { SamAccountName = "old",      DisplayName = "Old Account",   IsEnabled = false, DistinguishedName = "CN=old,DC=corp,DC=com",                 Sid = "S-1-5-21-1-2-3-2003" },
        };
        var table = new TableViewModel<AdUser>("Users", UserColumns.Grid)
        {
            ContextProvider   = () => new ExportContext("corp.com", "DC01.corp.com", "CORP\\admin"),
            FilterDescription = () => "status=Enabled",
        };
        table.SetRows(users);
        table.SetFilter(u => u.IsEnabled);

        var filtered = P("users-filtered.csv");
        TableViewModel.PickSavePathFor = (_, _) => filtered;
        table.ExportFilteredCommand.Execute(null);
        var lines = CsvLines(filtered);
        Assert.Equal(1 + 2, lines.Length);
        Assert.Equal(table.ExportColumns.Count, lines[0].Split(';').Length);   // one Excel column per grid column
        Assert.Contains(lines, l => l.Contains("Jürgen Müller"));
        Assert.Contains(lines, l => l.Contains("'=1+2"));               // formula guard
        Assert.DoesNotContain(lines, l => l.Split(';').Any(c => c.TrimStart('"').StartsWith('=')));
        AssertMetaMatches(filtered);
        Assert.Contains("status=Enabled", File.ReadAllText(ExportMetadata.MetaPathFor(filtered)));

        var full = P("users-all.csv");
        TableViewModel.PickSavePathFor = (_, _) => full;
        table.ExportAllCommand.Execute(null);
        Assert.Equal(1 + 3, CsvLines(full).Length);
        AssertMetaMatches(full);

        // the ADRecon-style full export (all columns incl. opt-ins) uses the same writer
        var wide = P("users-wide.csv");
        var cols = UserColumns.Export(true, true, true);
        Assert.Equal(3, new CsvExporter().ExportTable(users, cols, wide));
        Assert.Equal(cols.Count, CsvLines(wide)[0].Split(';').Length);
    }

    [Fact(Skip = "manuell: CSV in deutschem Excel per Doppelklick öffnen (Spalten, Umlaute) — docs/verification-checklist.md K04"), Trait("Category", K)]
    public void K04_Manual_OpenInExcel() { }

    // ── K05 Einzel-User-Export (Dossier) ──────────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K05_Dossier_NestedLocalizedGroups_Tier0BySid_PdfAndCsvs()
    {
        const string it = "CN=IT-Admins,OU=Groups,DC=corp,DC=com", da = "CN=Domänen-Admins,CN=Users,DC=corp,DC=com",
                     du = "CN=Domänen-Benutzer,CN=Users,DC=corp,DC=com";
        var groups = AdUserGroup.MergeRefs(
            [new(it, "IT-Admins", "S-1-5-21-1-2-3-1105", [da])],
            new(du, "Domänen-Benutzer", "S-1-5-21-1-2-3-513"),
            [new(it, "IT-Admins", "S-1-5-21-1-2-3-1105", [da]), new(da, "Domänen-Admins", "S-1-5-21-1-2-3-512")]);
        GroupPaths.Assign("jdoe", groups);

        Assert.Equal(3, groups.Count);                                   // IT-Admins once, as direct
        var daGroup = groups.Single(g => g.DistinguishedName == da);
        Assert.Equal("jdoe → IT-Admins → Domänen-Admins", daGroup.Path);
        Assert.True(daGroup.IsTier0);                                   // German name, found by RID 512
        Assert.False(groups.Single(g => g.DistinguishedName == it).IsTier0);
        Assert.Equal("jdoe → Domänen-Benutzer (primary)", groups.Single(g => g.DistinguishedName == du).Path);

        var user = new AdUser { SamAccountName = "jdoe", DisplayName = "Jürgen Doe" };
        var d = new UserDossier(user, UserDetailRows.Build(user, null), groups,
            [new AdFinding { Title = "Kerberoastable account", Category = "KerberoastableAccounts", Severity = FindingSeverity.High }],
            new ExportContext("corp.com", "DC1", "CORP\\a"), "2026-10-18", DateTime.UtcNow, "test");
        Assert.True(d.IsTier0);

        var pdf = P("jdoe.pdf");
        var files = UserDossierExporter.Export(d, pdf);
        Assert.Equal(3, files.Count);                                   // PDF + groups CSV + findings CSV
        foreach (var f in files) AssertMetaMatches(f);
        Assert.Contains(CsvLines(UserDossierExporter.GroupsCsvPath(pdf)), l => l.Contains("jdoe → IT-Admins → Domänen-Admins") && l.Contains("TRUE"));
        Assert.Contains(CsvLines(UserDossierExporter.FindingsCsvPath(pdf)), l => l.Contains("Kerberoastable account"));

        var lines = new List<string>();
        new PdfExporter().ExportUserDossier(d, P("jdoe-text.pdf"), lines.Add);
        Assert.Contains("jdoe → IT-Admins → Domänen-Admins", lines);
        Assert.Contains(lines, l => l.Contains("TIER 0"));
        Assert.Contains("HIGH  Kerberoastable account", lines);
    }

    [Fact(Skip = "manuell (AD): Dossier für echte User mit SPN/AS-REP/Delegation erzeugen und lesen — docs/verification-checklist.md K05"), Trait("Category", K)]
    public void K05_Manual_DossierForRealUser() { }

    // ── K06 PDF-Report ────────────────────────────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K06_FullReport_HasEveryFindingWithFix_PagesFooter_AndRedactsNames()
    {
        var findings = ScanFindings();
        findings.Add(Finding("PasswordNeverExpires", FindingSeverity.Medium,
            [.. Enumerable.Range(0, 40).Select(i => User($"user{i:D2}", 3000 + i))]));
        var score = ScoreModel.Compute(FindingCatalog.All, findings, [], new ScanTotals(100, 50, 40));
        var input = new ReportInput("corp.com", findings, score, score.Score, [], [], null, null,
            [], [], ["Administrator"], new DateTime(2026, 10, 18, 9, 0, 0), "test");

        var m = ReportBuilder.Build(input, new ReportOptions(ReportKind.Full, false));
        Assert.Equal(findings.Count, m.Findings.Count);
        Assert.All(m.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.HowToFix)));
        Assert.Equal("Critical", m.Findings[0].Severity);
        var capped = m.Findings.Single(f => f.Objects.HiddenRows > 0);  // 40 objects → capped, rest in the CSV
        Assert.Equal(ReportBuilder.ObjectCap, capped.Objects.Rows.Count);

        var pdf = P("report.pdf");
        ReportPdfRenderer.Render(m, pdf);
        var pages = ReportPdfRenderer.PageCount(pdf);
        Assert.True(pages >= 2);
        Assert.Contains("corp.com", ReportPdfRenderer.FooterText(m));
        Assert.Equal($"Page {pages} of {pages}", ReportPdfRenderer.PageLabel(pages, pages));

        var redacted = ReportBuilder.RedactorFor(input).Apply(ReportBuilder.Build(input, new ReportOptions(ReportKind.Full, true)));
        var all = string.Join("\n", redacted.Findings.SelectMany(f => f.Objects.Rows.SelectMany(r => r)));
        Assert.DoesNotContain("jdoe", all);
        Assert.Contains("WS-OLD01", all);                               // computers stay
    }

    [Fact(Skip = "manuell: Sichtprüfung des PDF — Tabellenköpfe nach jedem Seitenumbruch (Renderer hat keine Textsenke) — docs/verification-checklist.md K06"), Trait("Category", K)]
    public void K06_Manual_VisualCheck() { }

    // ── K07 Suchen und Detail ─────────────────────────────────────────────────
    [Fact, Trait("Category", K)]
    public async Task K07_TypedSearch_BecomesSafeFilter_BadInputNeverReachesLdap()
    {
        // what the user types → the filter that would go to LDAP
        var (type, term) = SearchQuery.Parse("  user: o'hara*)(cn=*");
        Assert.Equal(SearchScopeType.User, type);
        var filter = SearchQuery.NameFilter(AdConnector.EscapeFilterValue(term), type);
        Assert.StartsWith("(&(objectCategory=person)(objectClass=user)", filter);
        Assert.Equal(filter.Count(c => c == '('), filter.Count(c => c == ')'));
        Assert.DoesNotContain("*)(cn=*", filter);                        // injected clause is escaped

        // malformed SID / OU and a foreign event log are refused before any network call
        var searcher = new AdSearcher(new AdConnector());
        Assert.Empty(await searcher.SearchAsync("S-1-5)(objectClass=*)", SearchMode.Sid));
        Assert.Empty(await searcher.SearchAsync("ldap://hostile", SearchMode.Ou));
        Assert.Empty(await new AdWmiClient().GetEventLogAsync("localhost", "System' OR '1'='1", null, 10));
    }

    [Fact(Skip = "manuell (AD): Suche mit Treffern, Doppelklick öffnet Detail, WMI-Daten oder klare Meldung — docs/verification-checklist.md K07"), Trait("Category", K)]
    public void K07_Manual_SearchAndDetail() { }

    // ── K08 Inaktive und privilegierte Konten ─────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K08_PrivilegedGroupsBySid_NestedAndPrimaryMembers_InactiveHonestlyLabelled()
    {
        const string domainSid = "S-1-5-21-11-22-33", forestSid = "S-1-5-21-44-55-66";
        Assert.Equal(12, WellKnownGroups.Privileged.Count);
        foreach (var def in WellKnownGroups.Privileged)
        {
            var sid = WellKnownGroups.SidFor(def, domainSid, forestSid);
            if (def.Source == GroupSidSource.NameOnly) { Assert.Null(sid); continue; }   // DnsAdmins: by name
            Assert.NotNull(sid);
            if (def.Rid != WellKnownGroups.RemoteDesktopUsersRid)
                Assert.True(WellKnownGroups.IsTier0Sid(sid), $"{def.FallbackName} ({sid}) not Tier 0");
        }

        // members: nested chain plus accounts holding Domain Admins only as primary group
        var clause = WellKnownGroups.MembershipClause("CN=Domänen-Admins,CN=Users,DC=corp,DC=com", WellKnownGroups.DomainAdminsRid);
        Assert.Contains("memberOf:1.2.840.113556.1.4.1941:=", clause);
        Assert.Contains("(primaryGroupID=512)", clause);

        // user browser: the inactive chip and its honest column label
        var now = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        var chips = new QuickFilters.UserChips(Admin: true, Inactive: true);
        Assert.True(QuickFilters.Matches(new AdUser { AdminCount = true, LastLogon = now.AddDays(-120) }, chips, now));
        Assert.True(QuickFilters.Matches(new AdUser { AdminCount = true, LastLogon = null }, chips, now));
        Assert.False(QuickFilters.Matches(new AdUser { AdminCount = true, LastLogon = now.AddDays(-10) }, chips, now));
        Assert.Contains("±14 d", UserColumns.LastLogonHeader);
    }

    [Fact(Skip = "manuell (AD): Domain Admins und privilegierte Gruppen inkl. verschachtelter Mitglieder gegen echte Domäne — docs/verification-checklist.md K08"), Trait("Category", K)]
    public void K08_Manual_RealMembers() { }

    // ── K09 Infrastruktur verstehen ───────────────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K09_DcInventoryWithFsmoAndEol_GpoClassification_Export()
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        Assert.True(EolMatcher.TryGetEolDate("Windows Server 2012 R2 Standard", out var eol));
        var dcs = new List<AdDomainController>
        {
            new() { Name = "DC01", OperatingSystem = "Windows Server 2012 R2 Standard", IsGlobalCatalog = true,
                    FsmoRoles = ["PDC", "RID", "Infrastructure"], IsEol = true, EolDate = eol, DistinguishedName = "CN=DC01,OU=Domain Controllers,DC=corp,DC=com" },
            new() { Name = "DC02", OperatingSystem = "Windows Server 2022 Standard", FsmoRoles = ["Schema", "Domain Naming"],
                    DistinguishedName = "CN=DC02,OU=Domain Controllers,DC=corp,DC=com" },
        };
        var table = new TableViewModel<AdDomainController>("Domain Controllers", ViewColumns.DomainControllers);
        table.SetRows(dcs);
        var csv = P("dcs.csv");
        TableViewModel.PickSavePathFor = (_, _) => csv;
        table.ExportAllCommand.Execute(null);
        var lines = CsvLines(csv);
        Assert.Contains(lines, l => l.StartsWith("DC01;") && l.Contains("PDC, RID, Infrastructure") && l.Contains("TRUE"));
        Assert.Equal(5, dcs.SelectMany(d => d.FsmoRoles).Distinct().Count());   // all five roles visible

        // GPO browser: links → GUIDs, status per GPO
        const string gpLink = "[LDAP://cn={31B2F340-016D-11D2-945F-00C04FB984F9},cn=policies,cn=system,DC=corp,DC=com;0]" +
                              "[LDAP://cn={6AC1786C-016F-11D2-945F-00C04FB984F9},cn=policies,cn=system,DC=corp,DC=com;2]";
        Assert.Equal(2, GpoLogic.ExtractGuids(gpLink).Count());
        Assert.Equal("Active",   GpoLogic.Classify(0, 2));
        Assert.Equal("Orphaned", GpoLogic.Classify(0, 0));
        Assert.Equal("Disabled", GpoLogic.Classify(3, 2));
    }

    [Fact(Skip = "manuell (AD): DC-Inventar, Trusts (Richtung wird im Scanner gemappt, AdScanner.Trust.cs), GPO- und OU-Browser gegen echte Domäne — docs/verification-checklist.md K09"), Trait("Category", K)]
    public void K09_Manual_RealInfrastructure() { }

    // ── K10 Änderungen und Datenqualität ──────────────────────────────────────
    [Fact, Trait("Category", K)]
    public void K10_TimelineAndDataQuality_ExportReadable_AndSafe()
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        var changes = new List<AdRecentChange>
        {
            new() { Name = "jdoe",  ObjectType = "User",     Action = "Modified", ChangedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc), DistinguishedName = "CN=jdoe,DC=corp,DC=com" },
            new() { Name = "WS-NEU", ObjectType = "Computer", Action = "Created",  ChangedAt = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc), DistinguishedName = "CN=WS-NEU,DC=corp,DC=com" },
        };
        var table = new TableViewModel<AdRecentChange>("Recent Changes", ViewColumns.RecentChanges);
        table.SetRows(changes);
        var csv = P("timeline.csv");
        TableViewModel.PickSavePathFor = (_, _) => csv;
        table.ExportAllCommand.Execute(null);
        var lines = CsvLines(csv);
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines, l => l.StartsWith("Computer;WS-NEU;Created;"));
        AssertMetaMatches(csv);

        var dq = P("dataquality.csv");
        new CsvExporter().ExportDataQuality(
        [
            new AdAttributeCompleteness { AttributeName = "Email", LdapName = "mail", FilledCount = 50, TotalCount = 100 },
            new AdAttributeCompleteness { AttributeName = "=cmd|' /C calc'!A0", LdapName = "x", FilledCount = 0, TotalCount = 100 },
        ], dq);
        var dqLines = CsvLines(dq);
        Assert.Contains(dqLines, l => l.StartsWith("Email;mail;50;100;50%"));
        Assert.DoesNotContain(dqLines, l => l.Split(';').Any(c => c.TrimStart('"').StartsWith('=')));
    }

    [Fact(Skip = "manuell (AD): Timeline für 7/30/90 Tage (LDAP-Filter wird im Scanner gebaut, AdScanner.Trust.cs) und Data-Quality-Report gegen echte Domäne — docs/verification-checklist.md K10"), Trait("Category", K)]
    public void K10_Manual_RealTimelineAndDataQuality() { }
}
