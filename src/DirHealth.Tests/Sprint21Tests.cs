using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Export.Reports;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace DirHealth.Tests;

// Sprint 21: report for management — risk matrix, references with links, "What was checked",
// findings-tab PDF through the report engine. Rule counts come from FindingCatalog.All (S23 adds rules).
[Collection(ExportHookCollection.Name)]
public class Sprint21Tests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dh-s21-");
    public void Dispose() => _dir.Delete(recursive: true);
    private string P(string n) => Path.Combine(_dir.FullName, n);

    private static readonly DateTime Now = new(2026, 10, 18, 9, 30, 0, DateTimeKind.Local);

    private static FindingRule Rule(string ruleId) => FindingCatalog.All.Single(r => r.RuleId == ruleId);
    private static FindingRule FirstOf(string area) => FindingCatalog.All.Where(r => r.RuleId.StartsWith(area + "-")).OrderBy(r => r.RuleId).First();

    private static AdFinding F(FindingRule r, FindingSeverity sev, bool excepted = false, string obj = "jdoe") => new()
    {
        Category = r.Category, Title = $"{r.Category} finding", Severity = sev, Count = 1, IsAcknowledged = excepted,
        Objects = [new AffectedObject { Name = obj, DistinguishedName = $"CN={obj},OU=Staff,DC=corp,DC=com", Sid = "S-1-5-21-1-2-3-1500", Type = AffectedObjectType.User }],
    };

    private static ReportInput Input(IReadOnlyList<AdFinding> findings, IReadOnlyList<CheckResult>? checks = null)
    {
        checks ??= [];
        var score = ScoreModel.Compute(FindingCatalog.All, findings, checks, new ScanTotals(100, 50, 40));
        return new ReportInput("corp.com", findings, score, score.Score, checks, [], null, null,
            InactiveUsers: [new AdUser { DisplayName = "Jane Doe", SamAccountName = "jdoe", DistinguishedName = "CN=jdoe,OU=Staff,DC=corp,DC=com" }],
            ExpiringPasswords: [], DomainAdmins: ["Administrator"], CreatedLocal: Now, Version: "test");
    }

    private static IReadOnlyList<AdFinding> KrbAndPrvHigh() =>
        [F(FirstOf("DH-KRB"), FindingSeverity.High), F(FirstOf("DH-PRV"), FindingSeverity.High)];

    private static int Cell(ReportRiskMatrix m, string area, string severity) =>
        m.Rows.Single(r => r.Area == area).Counts[m.Severities.ToList().IndexOf(severity)];

    // ── S21-1 risk matrix ────────────────────────────────────────────────────
    [Fact]
    public void RiskMatrix_OneOpenHighInKrbAndPrv_ExactlyTwoCells()
    {
        var m = ReportBuilder.Build(Input(KrbAndPrvHigh()), new ReportOptions(ReportKind.Summary, false)).RiskMatrix;
        Assert.Equal(2, m.OccupiedCells);
        Assert.Equal(1, Cell(m, "DH-KRB", "High"));
        Assert.Equal(1, Cell(m, "DH-PRV", "High"));
        Assert.Equal(["Critical", "High", "Medium", "Low"], m.Severities);
    }

    [Fact]
    public void RiskMatrix_EveryCatalogArea_IsARow_EvenWhenEmpty()
    {
        var m = ReportBuilder.Build(Input([]), new ReportOptions(ReportKind.Full, false)).RiskMatrix;
        var areas = FindingCatalog.All.Select(r => ReportBuilder.AreaOf(r.RuleId)).Distinct().OrderBy(a => a, StringComparer.Ordinal);
        Assert.Equal(areas, m.Rows.Select(r => r.Area));
        Assert.Equal(0, m.OccupiedCells);
        Assert.DoesNotContain(m.Rows, r => r.Label.StartsWith("DH-"));   // all known areas have a readable name
    }

    [Fact]
    public void RiskMatrix_CountsOnlyOpenFindings()
    {
        var findings = new List<AdFinding> { F(FirstOf("DH-KRB"), FindingSeverity.High), F(FirstOf("DH-PRV"), FindingSeverity.High, excepted: true) };
        var m = ReportBuilder.RiskMatrix(findings, FindingCatalog.All);
        Assert.Equal(1, m.OccupiedCells);
        Assert.Equal(0, Cell(m, "DH-PRV", "High"));
    }

    [Fact]
    public void RiskMatrix_InSummaryAndFull_SameCells_ScoreUntouched()
    {
        var input = Input(KrbAndPrvHigh());
        var s = ReportBuilder.Build(input, new ReportOptions(ReportKind.Summary, false));
        var f = ReportBuilder.Build(input, new ReportOptions(ReportKind.Full, true));
        Assert.Equal(s.RiskMatrix.Rows.Select(r => r.Total), f.RiskMatrix.Rows.Select(r => r.Total));
        Assert.Equal(input.Score!.Display, s.ScoreText);            // the matrix adds no score of its own
        Assert.Equal(input.Score!.Display, f.ScoreText);
    }

    [Fact]
    public void AreaOf_SplitsRuleId()
    {
        Assert.Equal("DH-KRB", ReportBuilder.AreaOf("DH-KRB-001"));
        Assert.Equal("Other", ReportBuilder.AreaOf(""));
        Assert.Equal("Other", ReportBuilder.AreaOf("X"));
    }

    // ── S21-2 references ─────────────────────────────────────────────────────
    [Fact]
    public void Full_FindingCarriesCatalogReferences()
    {
        var rule = Rule("DH-KRB-001");
        var m = ReportBuilder.Build(Input([F(rule, FindingSeverity.High)]), new ReportOptions(ReportKind.Full, false));
        var refs = m.Findings.Single(f => f.RuleId == "DH-KRB-001").References;
        Assert.NotEmpty(rule.References);
        Assert.Equal(rule.References.Select(r => (r.Label, r.Url)), refs.Select(r => (r.Label, r.Url)));
        Assert.All(refs, r => Assert.StartsWith("https://", r.Url));
    }

    [Fact]
    public void Summary_HasNoFindingDetails_SoNoReferences()
    {
        var m = ReportBuilder.Build(Input([F(Rule("DH-KRB-001"), FindingSeverity.High)]), new ReportOptions(ReportKind.Summary, false));
        Assert.Empty(m.Findings);
        Assert.Empty(m.CheckedRules);
    }

    // ── S21-3 "What was checked" ─────────────────────────────────────────────
    [Fact]
    public void Full_ListsEveryCatalogRule_WithOneOfFourStatuses()
    {
        var krb = Rule("DH-KRB-001");
        var others = FindingCatalog.All.Where(r => r.RuleId != krb.RuleId).OrderBy(r => r.RuleId).Take(3).ToList();
        var checks = new List<CheckResult>
        {
            new(krb.Category, krb.RuleId, CheckStatus.Found, "", 1),
            new(others[0].Category, others[0].RuleId, CheckStatus.Passed, "", 0),
            new(others[1].Category, others[1].RuleId, CheckStatus.Failed, "LDAP timeout", 0),
            new(others[2].Category, others[2].RuleId, CheckStatus.NotApplicable, "No PSOs", 0),
        };
        var m = ReportBuilder.Build(Input([F(krb, FindingSeverity.High)], checks), new ReportOptions(ReportKind.Full, false));

        Assert.Equal(FindingCatalog.All.Count, m.CheckedRules.Count);
        Assert.Equal(FindingCatalog.All.Select(r => r.RuleId).OrderBy(x => x, StringComparer.Ordinal), m.CheckedRules.Select(c => c.RuleId));
        string[] four = ["Found", "Passed", "Failed", "Not applicable"];
        Assert.All(m.CheckedRules, c => Assert.Contains(c.Status, four));
        Assert.Equal(FindingCatalog.All.Count, m.CheckedRules.GroupBy(c => c.Status).Sum(g => g.Count()));
        Assert.Equal("Passed", m.CheckedRules.Single(c => c.RuleId == others[0].RuleId).Status);
        Assert.Equal("LDAP timeout", m.CheckedRules.Single(c => c.RuleId == others[1].RuleId).Reason);
        Assert.Equal("Not applicable", m.CheckedRules.Single(c => c.RuleId == others[2].RuleId).Status);
    }

    [Fact]
    public void CheckedRules_NoResult_IsFailed_NeverPassed_FindingWithoutCheck_IsFound()
    {
        var krb = Rule("DH-KRB-001");
        var rows = ReportBuilder.CheckedRules([], [F(krb, FindingSeverity.High)], FindingCatalog.All);
        Assert.Equal("Found", rows.Single(c => c.RuleId == krb.RuleId).Status);
        Assert.DoesNotContain(rows, c => c.Status == "Passed");
        Assert.All(rows.Where(c => c.RuleId != krb.RuleId), c => Assert.Equal("Failed", c.Status));
    }

    [Fact]
    public void Redaction_CoversNewTextFields()
    {
        var krb = Rule("DH-KRB-001");
        var checks = new List<CheckResult> { new(krb.Category, krb.RuleId, CheckStatus.Failed, "Cannot read CN=jdoe,OU=Staff,DC=corp,DC=com", 0) };
        var m = ReportBuilder.Build(Input([F(krb, FindingSeverity.High)], checks),
            new ReportOptions(ReportKind.Full, RedactUserNames: true, FilterScope: "text contains \"jdoe\""));
        Assert.DoesNotContain("jdoe", m.CheckedRules.Single(c => c.RuleId == krb.RuleId).Reason);
        Assert.DoesNotContain("jdoe", m.FilterScope);
        Assert.Contains("user-", m.FilterScope);
    }

    // ── S21-4 one PDF engine ─────────────────────────────────────────────────
    [Fact]
    public void PdfExporter_ExportReport_IsRemoved()
    {
        Assert.Null(typeof(PdfExporter).GetMethod("ExportReport"));
    }

    private FindingsViewModel Vm(IReadOnlyList<AdFinding> findings, IReadOnlyList<CheckResult>? checks = null)
    {
        var vm = new FindingsViewModel(new ExceptionStore(P("exceptions.json")), new AcknowledgeStore(P("acknowledged.json")));
        vm.LoadFindings(findings.ToList(), 70);
        var input = Input(findings, checks);
        vm.ReportContext = () => new FindingsReportContext("corp.com", input.Score, input.StoredScore, checks ?? []);
        return vm;
    }

    [Fact]
    public void FindingsTab_Report_UsesFilterSelection_AndSaysSo()
    {
        var krb = F(FirstOf("DH-KRB"), FindingSeverity.High);
        var low = F(FirstOf("DH-CMP"), FindingSeverity.Low, obj: "WS01");
        var vm = Vm([krb, low]);
        vm.SeverityFilter = "High";

        var m = vm.BuildReportModel(new ReportOptions(ReportKind.Full, false), Now);

        Assert.Equal([krb.RuleId], m.Findings.Select(f => f.RuleId));
        Assert.Equal(1, m.RiskMatrix.OccupiedCells);
        Assert.NotNull(m.FilterScope);
        Assert.Contains("severity = High", m.FilterScope);
        Assert.Contains("1 of 2 findings", m.FilterScope);
        Assert.Empty(m.Appendix);                       // user lists belong to the whole scan
        Assert.Null(m.DeltaText);
        Assert.Empty(m.Changes.Rows);
        Assert.DoesNotContain(m.FixFirst.Rows, r => r[1] == low.Title);
        Assert.Equal(FindingCatalog.All.Count, m.CheckedRules.Count);
        Assert.Equal("corp.com", m.Domain);
    }

    [Fact]
    public void FindingsTab_NoFilter_SaysAllFindings()
    {
        var vm = Vm([F(FirstOf("DH-KRB"), FindingSeverity.High)]);
        Assert.Equal("all findings (1 of 1 findings)", vm.FilterScopeText);
    }

    [Fact]
    public void DashboardReport_HasNoFilterScope_AndKeepsUserLists()
    {
        var m = ReportBuilder.Build(Input(KrbAndPrvHigh()), new ReportOptions(ReportKind.Full, false));
        Assert.Null(m.FilterScope);
        Assert.Equal(3, m.Appendix.Count);
    }

    // ── renderer (needs Windows fonts: PdfSharp + WPF resources) ─────────────
    private static List<PdfDictionary> Links(string path)
    {
        var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var links = new List<PdfDictionary>();
        foreach (var page in doc.Pages)
            for (int i = 0; i < page.Annotations.Count; i++)
                if (page.Annotations[i].Elements.GetName("/Subtype") == "/Link") links.Add(page.Annotations[i]);
        return links;
    }

    [Fact]
    public void Renderer_Full_HasLinkAnnotations_MatrixAndCheckedAppendix()
    {
        var path = P("full.pdf");
        var rule = Rule("DH-KRB-001");
        var m = ReportBuilder.Build(Input([F(rule, FindingSeverity.High), F(FirstOf("DH-PRV"), FindingSeverity.High)]), new ReportOptions(ReportKind.Full, false));
        var text = new List<string>();
        ReportPdfRenderer.Render(m, path, text.Add);

        var links = Links(path);
        Assert.NotEmpty(links);
        var uris = links.Select(l => l.Elements.GetDictionary("/A")?.Elements.GetString("/URI")).ToList();
        Assert.Contains(rule.References[0].Url, uris);
        Assert.Contains(rule.References[0].Label, text);
        Assert.Contains("Risk matrix — open findings by area and severity", text);
        Assert.Contains($"What was checked ({FindingCatalog.All.Count} rules)", text);
        int pages = ReportPdfRenderer.PageCount(path);
        Assert.Contains($"Page 1 of {pages}", text);
    }

    [Fact]
    public void Renderer_FindingsTab_ShowsScopeAndPageXofY()
    {
        var path = P("findings.pdf");
        var vm = Vm([F(FirstOf("DH-KRB"), FindingSeverity.High), F(FirstOf("DH-CMP"), FindingSeverity.Low, obj: "WS01")]);
        vm.SeverityFilter = "High";
        var text = new List<string>();
        ReportPdfRenderer.Render(vm.BuildReportModel(new ReportOptions(ReportKind.Full, false), Now), path, text.Add);
        Assert.Contains(text, t => t.StartsWith("Filtered selection: severity = High"));
        Assert.Contains($"Page 1 of {ReportPdfRenderer.PageCount(path)}", text);
    }

    [Fact]
    public void FindingsTab_ExportCommand_WritesPdfAndMeta()
    {
        var path = P("export.pdf");
        var vm = Vm([F(FirstOf("DH-KRB"), FindingSeverity.High)]);
        var (ask, pick) = (FindingsViewModel.AskReportOptions, FindingsViewModel.PickReportPath);
        try
        {
            FindingsViewModel.AskReportOptions = () => new ReportOptions(ReportKind.Full, false);
            FindingsViewModel.PickReportPath   = _ => path;
            vm.ExportPdfCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        }
        finally { (FindingsViewModel.AskReportOptions, FindingsViewModel.PickReportPath) = (ask, pick); }

        Assert.True(File.Exists(path), vm.StatusMessage);
        Assert.True(File.Exists(ExportMetadata.MetaPathFor(path)));
        Assert.Contains("all findings", File.ReadAllText(ExportMetadata.MetaPathFor(path)));
    }
}
