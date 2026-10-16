using System.IO.Compression;
using System.Text.Json;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Export.Reports;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;

namespace DirHealth.Tests;

// Sprint 18: two-level PDF report, print discipline, exceptions/failed sections, redaction, XLSX.
[Collection(ExportHookCollection.Name)]
public class Sprint18Tests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dh-s18-");
    public void Dispose() => _dir.Delete(recursive: true);
    private string P(string n) => Path.Combine(_dir.FullName, n);

    private static readonly DateTime Now = new(2026, 10, 15, 9, 30, 0, DateTimeKind.Local);

    private static AffectedObject U(string sam) => new()
        { Name = sam, DistinguishedName = $"CN={sam},OU=Staff,DC=corp,DC=com", Sid = $"S-1-5-21-1-2-3-{Math.Abs(sam.GetHashCode()) % 9000 + 1000}", Type = AffectedObjectType.User };

    private static ReportInput Input(int objects = 3, bool failed = false, ScanDiff? diff = null, IReadOnlyList<ObjectException>? exceptions = null)
    {
        var findings = new List<AdFinding>
        {
            new() { Category = "AsRepRoasting", Title = $"{objects} Account(s) Vulnerable to AS-REP Roasting", Severity = FindingSeverity.High,
                    Count = objects, Objects = Enumerable.Range(0, objects).Select(i => U($"jdoe{i}")).ToList() },
            new() { Category = "InactiveComputers", Title = "1 Inactive Computer Account(s)", Severity = FindingSeverity.Low, Count = 1,
                    Objects = [new AffectedObject { Name = "WS-OLD01", Type = AffectedObjectType.Computer }] },
        };
        var checks = failed ? new List<CheckResult> { new("SidHistory", "DH-USR-005", CheckStatus.Failed, "LDAP timeout", 0) } : [];
        var score  = ScoreModel.Compute(FindingCatalog.All, findings, checks, new ScanTotals(100, 50, 40));
        return new ReportInput("corp.com", findings, score, score.Score, checks, exceptions ?? [], diff, 5,
            InactiveUsers: [new AdUser { DisplayName = "Jane Doe", SamAccountName = "jdoe0", Email = "jane.doe@corp.com", DistinguishedName = "CN=Jane Doe,OU=Staff,DC=corp,DC=com" }],
            ExpiringPasswords: [],
            DomainAdmins: ["Administrator", "jdoe1"],
            CreatedLocal: Now, Version: "test");
    }

    // ── S18-1 two levels ─────────────────────────────────────────────────────
    [Fact]
    public void Summary_HasScoreDirectionFixFirstAndCategories_NoTechnicalPart()
    {
        var m = ReportBuilder.Build(Input(), new ReportOptions(ReportKind.Summary, false));
        Assert.Equal("Higher is better (0–100)", m.ScoreDirection);
        Assert.NotEmpty(m.FixFirst.Rows);
        Assert.Equal("1", m.FixFirst.Rows[0][0]);
        Assert.Contains("AS-REP", m.FixFirst.Rows[0][1]);           // largest gain first
        Assert.Equal(2, m.CategoryOverview.Rows.Count);
        Assert.Equal("▲ +5 since the last scan", m.DeltaText);
        Assert.Empty(m.Findings);
        Assert.Empty(m.Appendix);
    }

    [Fact]
    public void Full_HasCardsWithFixAndUndo()
    {
        var m = ReportBuilder.Build(Input(), new ReportOptions(ReportKind.Full, false));
        var f = m.Findings.First(x => x.RuleId == "DH-KRB-002");
        Assert.Contains("Set-ADAccountControl", f.Fix);
        Assert.Contains("DoesNotRequirePreAuth $true", f.Revert);
        Assert.True(f.PointsIfFixed > 0);
        Assert.Equal(3, m.Appendix.Count);
    }

    [Fact]
    public void Delta_AcrossFormulaChange_SaysNotComparable()
    {
        var m = ReportBuilder.Build(Input(diff: new ScanDiff { ScoreComparable = false }), new ReportOptions(ReportKind.Summary, false));
        Assert.Equal("Score model changed since the last scan — no comparison.", m.DeltaText);
    }

    // ── S18-2 print discipline ───────────────────────────────────────────────
    [Fact]
    public void ObjectLists_CappedAt25_WithHint()
    {
        var m = ReportBuilder.Build(Input(objects: 40), new ReportOptions(ReportKind.Full, false));
        var t = m.Findings.First(x => x.RuleId == "DH-KRB-002").Objects;
        Assert.Equal(ReportBuilder.ObjectCap, t.Rows.Count);
        Assert.Equal(15, t.HiddenRows);
        Assert.Equal("15 more — see the CSV export of this finding", t.HiddenHint);
    }

    [Fact]
    public void Capped_UnderLimit_NoHint()
    {
        var t = ReportBuilder.Capped(["A"], [new[] { "x" }]);
        Assert.Equal(0, t.HiddenRows);
        Assert.Equal("", t.HiddenHint);
    }

    // ── S18-3 exceptions and failed checks ───────────────────────────────────
    [Fact]
    public void ActiveExceptionsAndFailedChecks_AreSections()
    {
        var ex = new List<ObjectException>
        {
            new("DH-KRB-002", "S-1", "svc_app", "legacy client", Now.ToUniversalTime().AddDays(30), "CORP\\admin", Now.ToUniversalTime()),
            new("DH-KRB-002", "S-2", "old_one", "expired", Now.ToUniversalTime().AddDays(-1), "CORP\\admin", Now.ToUniversalTime().AddDays(-40)),
        };
        var m = ReportBuilder.Build(Input(failed: true, exceptions: ex), new ReportOptions(ReportKind.Full, false));
        var e = Assert.Single(m.Exceptions);                       // expired ones are not "active"
        Assert.Equal(("svc_app", "legacy client", "CORP\\admin"), (e.Object, e.Reason, e.CreatedBy));
        var f = Assert.Single(m.FailedChecks);
        Assert.Equal("LDAP timeout", f.Reason);
        Assert.Equal("Upper bound — some checks could not run.", m.ScoreNote);
    }

    // ── S18-4 redaction ──────────────────────────────────────────────────────
    [Fact]
    public void Redacted_ModelContainsNoUserName_ButKeepsNumbersAndComputers()
    {
        var m = ReportBuilder.Build(Input(objects: 3), new ReportOptions(ReportKind.Full, RedactUserNames: true));
        var json = JsonSerializer.Serialize(m);
        foreach (var original in new[] { "jdoe0", "jdoe1", "jdoe2", "Jane Doe", "jane.doe@corp.com", "CN=Jane Doe" })
            Assert.DoesNotContain(original, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user-00", json);
        Assert.Contains("WS-OLD01", json);                       // computers are not people
        Assert.Contains("3 Account(s)", json);                   // counts stay
        Assert.True(m.Redacted);
    }

    [Fact]
    public void Redactor_SamePersonSameAlias_Stable()
    {
        var r = new Redactor([["Jane Doe", "jdoe", "CN=Jane Doe,OU=Staff,DC=corp,DC=com"], ["bob"]]);
        Assert.Equal("user-002", r.Apply("jdoe"));               // sorted: "bob" < "CN=…" … → stable numbering
        Assert.Equal("user-002 logged on", r.Apply("Jane Doe logged on"));
        Assert.Equal("user-002", r.Apply("CN=Jane Doe,OU=Staff,DC=corp,DC=com"));
        Assert.Equal("user-001", r.Apply("bob"));
        Assert.Equal("bobby", r.Apply("bobby"));                  // whole names only
        Assert.Equal("score 42", r.Apply("score 42"));
    }

    [Fact]
    public void Redactor_SameInputSameAliases()
    {
        var a = ReportBuilder.Build(Input(), new ReportOptions(ReportKind.Full, true));
        var b = ReportBuilder.Build(Input(), new ReportOptions(ReportKind.Full, true));
        Assert.Equal(JsonSerializer.Serialize(a.Findings), JsonSerializer.Serialize(b.Findings));
    }

    // ── S18-5 XLSX (ADR 0003) ────────────────────────────────────────────────
    private record Row(string Name, int Days, DateTime? When);

    private static string SheetXml(string path, int n)
    {
        using var z = ZipFile.OpenRead(path);
        using var r = new StreamReader(z.GetEntry($"xl/worksheets/sheet{n}.xml")!.Open());
        return r.ReadToEnd();
    }

    private static string SharedStrings(string path)
    {
        using var z = ZipFile.OpenRead(path);
        var e = z.GetEntry("xl/sharedStrings.xml");
        if (e is null) return "";
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    private static readonly IReadOnlyList<ITableColumn> Cols =
    [
        new TableColumn<Row>("Name", r => r.Name), new TableColumn<Row>("Days", r => r.Days), new TableColumn<Row>("When", r => r.When),
    ];

    [Fact]
    public void Xlsx_FormulaPayloadIsText_HeaderFrozen_Autofilter_NumbersTyped()
    {
        var path = P("t.xlsx");
        const string payload = "=cmd|' /C calc'!A0";
        new XlsxExporter().Export([new Row(payload, 91, new DateTime(2026, 1, 2))], Cols, path, [("Rows", "1")]);
        var xml = SheetXml(path, 1) + SharedStrings(path);
        Assert.DoesNotContain("<x:f>", xml);
        Assert.DoesNotContain("<f>", xml);
        Assert.Contains("=cmd|", xml);                        // unchanged value, no apostrophe added
        Assert.DoesNotContain("'=cmd", xml);
        Assert.Contains("state=\"frozen\"", SheetXml(path, 1));
        Assert.Contains("autoFilter", SheetXml(path, 1));
        Assert.Contains("t=\"n\"", SheetXml(path, 1));         // Days stays a number → numeric filters work
    }

    [Fact]
    public void Xlsx_InfoSheet_HasAllFieldsButNoSelfHash()
    {
        var path = P("i.xlsx");
        var fields = ExportMetadata.Fields(new ExportContext("corp.com", "DC1", "CORP\\a"), "Users", "status=Active", 1, "i.xlsx", DateTime.UtcNow, "x");
        new XlsxExporter().Export([new Row("a", 1, null)], Cols, path, fields);
        var info = SheetXml(path, 2) + SharedStrings(path);
        foreach (var (k, _) in fields) Assert.Contains(k, info);
        Assert.DoesNotContain("SHA-256", info);
    }

    [Fact]
    public void Xlsx_ControlCharsRemoved_LongTextTruncated()
    {
        Assert.Equal("ab", XlsxExporter.CleanText("a\u0001b"));
        Assert.Equal("a\tb", XlsxExporter.CleanText("a\tb"));
        var longText = XlsxExporter.CleanText(new string('x', 40_000));
        Assert.Equal(XlsxExporter.MaxCellLength, longText.Length);
        Assert.EndsWith("[truncated]", longText);
        var path = P("c.xlsx");
        new XlsxExporter().Export([new Row("bad\u0002value", 1, null)], Cols, path, []);
        using var z = ZipFile.OpenRead(path);                  // still a valid zip/xml package
        Assert.NotNull(z.GetEntry("xl/workbook.xml"));
    }

    [Fact]
    public void Xlsx_FromTable_WritesMetaWithHashOfWorkbook()
    {
        var path = P("users.xlsx");
        var saved = TableViewModel.PickSavePathFor;
        try
        {
            TableViewModel.PickSavePathFor = (_, fmt) => fmt == ExportFormat.Xlsx ? path : null;
            var t = new TableViewModel<AdUser>("Users", UserColumns.Grid);
            t.SetRows([new AdUser { SamAccountName = "a", DisplayName = "A" }]);
            t.ExportXlsxAllCommand.Execute(null);
            Assert.True(File.Exists(path));
            Assert.Contains(ExportMetadata.Sha256OfFile(path), File.ReadAllText(ExportMetadata.MetaPathFor(path)));
            Assert.Equal("Excel (all 1)", t.ExportXlsxAllLabel);
        }
        finally { TableViewModel.PickSavePathFor = saved; }
    }

    [Fact]
    public void Xlsx_EmptyTable_StillHasHeader()
    {
        var path = P("e.xlsx");
        Assert.Equal(0, new XlsxExporter().Export([], Cols, path, []));
        Assert.Contains("Name", SheetXml(path, 1) + SharedStrings(path));
    }

    // ── renderer (needs Windows: PdfSharp + WPF resources) ───────────────────
    [Fact]
    public void Renderer_FullReport_PagesAndLandscapeForWideTables()
    {
        var path = P("full.pdf");
        var m = ReportBuilder.Build(Input(objects: 40), new ReportOptions(ReportKind.Full, true));
        // add a six-column appendix table to exercise the landscape rule
        m = m with { Appendix = [.. m.Appendix, ("Wide", new ReportTable(["1", "2", "3", "4", "5", "6"], [new[] { "a", "b", "c", "d", "e", "f" }], 0, ""))] };
        ReportPdfRenderer.Render(m, path);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)[..4]));
        var doc = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount >= 2);
        Assert.Contains(doc.Pages.Cast<PdfSharp.Pdf.PdfPage>(), p => p.Width.Point > p.Height.Point);   // landscape page exists
    }

    [Fact]
    public void Renderer_Summary_IsShort()
    {
        var path = P("summary.pdf");
        ReportPdfRenderer.Render(ReportBuilder.Build(Input(objects: 40), new ReportOptions(ReportKind.Summary, false)), path);
        Assert.InRange(ReportPdfRenderer.PageCount(path), 1, 2);
    }
}
