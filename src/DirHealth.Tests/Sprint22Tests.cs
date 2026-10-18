using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Storage;
using Xunit;

namespace DirHealth.Tests;

// Sprint 22: exports for work and audit — work list (S22-5), workbook (S22-6), scan JSON (S22-7, ADR 0005).
// Rule ids, weights and the formula version are read from FindingCatalog/ScoreModel, never hard-coded
// (new rules and FormulaVersion bumps must not break these tests).
[Collection(ExportHookCollection.Name)]
public class Sprint22Tests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dh-s22-");
    public void Dispose() => _dir.Delete(recursive: true);
    private string P(string n) => Path.Combine(_dir.FullName, n);

    private static readonly ExportContext Ctx = new("corp.com", "DC1.corp.com", "CORP\\auditor");
    private const string Payload = "=cmd|' /C calc'!A0";

    private static AffectedObject U(string sam, int rid) => new()
        { Name = sam, DistinguishedName = $"CN={sam},OU=Staff,DC=corp,DC=com", Sid = $"S-1-5-21-1-2-3-{rid}", Type = AffectedObjectType.User };

    /// <summary>2 findings × 3 objects (the S22 fixture), optionally plus one finding without objects.</summary>
    private static List<AdFinding> Fixture(string firstName = "jdoe0", bool withEmpty = false)
    {
        var list = new List<AdFinding>
        {
            new() { Category = "AsRepRoasting", Title = "3 Account(s) Vulnerable to AS-REP Roasting", Severity = FindingSeverity.High, Count = 3,
                    Objects = [U(firstName, 1101), U("jdoe1", 1102), U("jdoe2", 1103)] },
            new() { Category = "PasswordNeverExpires", Title = "3 Account(s) with Password Never Expires", Severity = FindingSeverity.Medium, Count = 3,
                    Objects = [U("svc1", 1201), U("svc2", 1202), U("svc3", 1203)] },
        };
        if (withEmpty)
            list.Add(new AdFinding { Category = "InactiveUsers", Title = "Inactive users (old cache)", Severity = FindingSeverity.Low, Count = 2 });
        return list;
    }

    private static ScoreResult Score(List<AdFinding> f) =>
        ScoreModel.Compute(FindingCatalog.All, f, [], new ScanTotals(100, 50, 40));

    private static string Entry(string xlsx, string name)
    {
        using var z = ZipFile.OpenRead(xlsx);
        var e = z.GetEntry(name);
        if (e is null) return "";
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    private static List<string> SheetNamesIn(string xlsx) =>
        Regex.Matches(Entry(xlsx, "xl/workbook.xml"), "<(?:x:)?sheet [^>]*name=\"([^\"]+)\"")
             .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    private static int RowCount(string sheetXml) => Regex.Matches(sheetXml, "<(?:x:)?row[ >]").Count;

    // ── S22-5 work list ──────────────────────────────────────────────────────
    [Fact]
    public void Worklist_TwoFindingsThreeObjects_SixRowsPlusHeader_RuleDataFromCatalog()
    {
        var path = P("w.csv");
        Assert.Equal(6, WorklistExporter.ExportCsv(Fixture(), path, Ctx, ";"));
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        Assert.Equal(7, lines.Count);
        Assert.Equal("RuleId;Finding;Severity;Weight;Object;DistinguishedName;SID;Exception;HowToFix", lines[0].TrimStart('﻿'));

        var rows = WorklistExporter.Build(Fixture());
        var asRep = FindingCatalog.Get("AsRepRoasting")!;
        var first = rows[0];                                   // High before Medium
        Assert.Equal(asRep.RuleId, first.RuleId);
        Assert.Equal(asRep.Weight, first.Weight);
        Assert.Equal(asRep.HowToFix, first.HowToFix);
        Assert.Equal("High", first.Severity);
        Assert.Equal("CN=jdoe0,OU=Staff,DC=corp,DC=com", first.DistinguishedName);
        Assert.Equal("S-1-5-21-1-2-3-1101", first.Sid);
        Assert.Equal(3, rows.Count(r => r.RuleId == FindingCatalog.Get("PasswordNeverExpires")!.RuleId));
    }

    [Fact]
    public void Worklist_Csv_FormulaInObjectName_IsDefusedBySafeField()
    {
        var path = P("inj.csv");
        WorklistExporter.ExportCsv(Fixture(Payload), path, Ctx, ";");
        var text = File.ReadAllText(path);
        Assert.Contains("'=cmd|", text);
        Assert.DoesNotMatch(@"(^|;|"")=cmd", text.Replace("'=cmd", ""));   // no unescaped occurrence left
    }

    [Fact]
    public void Worklist_Csv_WritesMetaWithSha256()
    {
        var path = P("m.csv");
        WorklistExporter.ExportCsv(Fixture(), path, Ctx, ";");
        var meta = File.ReadAllText(ExportMetadata.MetaPathFor(path));
        Assert.Contains(ExportMetadata.Sha256OfFile(path), meta);
        Assert.Contains("Work list", meta);
        Assert.Matches(@"Rows\s*: 6", meta);
    }

    [Fact]
    public void Worklist_Xlsx_SixRows_ValueUnchanged_NoFormula_Meta()
    {
        var path = P("w.xlsx");
        Assert.Equal(6, WorklistExporter.ExportXlsx(Fixture(Payload), path, Ctx));
        var sheet = Entry(path, "xl/worksheets/sheet1.xml");
        Assert.Equal(7, RowCount(sheet));
        var xml = sheet + Entry(path, "xl/sharedStrings.xml");
        Assert.Contains("=cmd|", xml);
        Assert.DoesNotContain("'=cmd", xml);                    // XLSX: no SafeField (ADR 0003)
        Assert.DoesNotContain("<f>", xml);
        Assert.Contains(ExportMetadata.Sha256OfFile(path), File.ReadAllText(ExportMetadata.MetaPathFor(path)));
    }

    [Fact]
    public void Worklist_CarriesExceptionStatePerObject()
    {
        var findings = Fixture();
        var store = new ExceptionStore(P("exceptions.json"));
        var rule = FindingCatalog.Get("AsRepRoasting")!;
        var now = DateTime.UtcNow;
        store.Add(rule.RuleId, ExceptionKey.For(rule, findings[0].Objects[1])!, "jdoe1", "Legacy app, ticket 42", now.AddDays(30), "auditor", now);
        store.Apply(findings, now);

        var rows = WorklistExporter.Build(findings);
        var excepted = rows.Single(r => r.Object == "jdoe1");
        Assert.StartsWith("Active — until", excepted.Exception);
        Assert.Contains("Legacy app, ticket 42", excepted.Exception);
        Assert.Equal("", rows.Single(r => r.Object == "jdoe0").Exception);
    }

    [Fact]
    public void Worklist_FindingWithoutObjects_StillGetsOneRow()
    {
        var rows = WorklistExporter.Build(Fixture(withEmpty: true));
        Assert.Equal(7, rows.Count);
        Assert.Single(rows, r => r.RuleId == FindingCatalog.Get("InactiveUsers")!.RuleId && r.Object == "");
    }

    // ── S22-6 workbook ───────────────────────────────────────────────────────
    [Fact]
    public void SheetNames_ExcelSafe_Unique_MaxLength()
    {
        var names = XlsxExporter.SheetNames(
        [
            "Summary", "Info", "a[b]c:d*e?f/g\\h", "summary", new string('x', 40), new string('x', 40), new string('x', 40),
            "'quoted'", "", "History", "tab\there",
        ]);
        Assert.Equal(11, names.Count);
        Assert.Equal("Summary", names[0]);
        Assert.Equal("Info", names[1]);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n =>
        {
            Assert.InRange(n.Length, 1, XlsxExporter.MaxSheetName);
            Assert.True(n.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) < 0, n);
            Assert.False(n.StartsWith('\'') || n.EndsWith('\''), n);
            Assert.DoesNotContain(n, c => c < 0x20);
        });
        Assert.NotEqual("History", names[9]);
        Assert.Equal(new string('x', 31), names[4]);
        Assert.EndsWith(" (2)", names[5]);
    }

    [Fact]
    public void Workbook_SummaryPlusSheetPerFindingWithObjectsPlusInfo_AutofilterAndFrozenHeader()
    {
        var findings = Fixture(Payload, withEmpty: true);
        var path = P("wb.xlsx");
        int sheets = WorklistExporter.ExportWorkbook(findings, Score(findings), path, Ctx);

        int withObjects = findings.Count(f => f.Objects.Count > 0);
        Assert.Equal(2 + withObjects, sheets);
        var names = SheetNamesIn(path);
        Assert.Equal(2 + withObjects, names.Count);
        Assert.Equal("Summary", names[0]);
        Assert.Equal("Info", names[^1]);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.True(n.Length <= 31 && n.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) < 0, n));
        Assert.Contains(names, n => n.StartsWith(FindingCatalog.Get("AsRepRoasting")!.RuleId));

        for (int i = 1; i < names.Count; i++)                   // every data sheet (all but Info)
        {
            var xml = Entry(path, $"xl/worksheets/sheet{i}.xml");
            Assert.Contains("autoFilter", xml);
            Assert.Contains("state=\"frozen\"", xml);
        }
        Assert.Equal(1 + findings.Count, RowCount(Entry(path, "xl/worksheets/sheet1.xml")));   // summary: header + all findings
        Assert.Equal(4, RowCount(Entry(path, "xl/worksheets/sheet2.xml")));                    // header + 3 objects

        var info = Entry(path, $"xl/worksheets/sheet{names.Count}.xml") + Entry(path, "xl/sharedStrings.xml");
        Assert.Contains("Score formula version", info);
        Assert.DoesNotContain("SHA-256", info);
        Assert.Contains(ExportMetadata.Sha256OfFile(path), File.ReadAllText(ExportMetadata.MetaPathFor(path)));
    }

    [Fact]
    public void Workbook_SameCategoryTwice_GetsUniqueSheetNames()
    {
        var findings = Fixture();
        findings.Add(new AdFinding { Category = "AsRepRoasting", Title = "dup", Severity = FindingSeverity.High, Count = 1, Objects = [U("x", 9)] });
        var sheets = WorklistExporter.BuildWorkbook(findings, null, []);
        Assert.Equal(5, sheets.Count);
        Assert.Equal(sheets.Count, sheets.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── S22-7 scan JSON (ADR 0005) ───────────────────────────────────────────
    private static ScanJsonInput JsonInput(List<AdFinding> f) => new(
        f, [new CheckResult("SidHistory", FindingCatalog.Get("SidHistory")?.RuleId ?? "", CheckStatus.Failed, "LDAP timeout", 0)],
        Score(f), new ScanTotals(100, 50, 40), "corp.com", "DC1.corp.com", "18.10.2026 09:30:00",
        new DateTime(2026, 10, 18, 7, 30, 0, DateTimeKind.Utc), "test");

    [Fact]
    public void Json_RoundTrip_SameFindingsObjectsAndScore_VersionsSet_Meta()
    {
        var findings = Fixture();
        var input = JsonInput(findings);
        var path = P("scan.json");
        Assert.Equal(2, ScanJsonExporter.Export(input, path, Ctx));

        var back = ScanJsonExporter.Deserialize(File.ReadAllText(path))!;
        Assert.Equal(ScanJsonExporter.SchemaVersion, back.SchemaVersion);
        Assert.Equal(ScoreModel.FormulaVersion, back.FormulaVersion);
        Assert.Equal(findings.Count, back.Findings.Count);
        Assert.Equal(findings.Sum(f => f.Objects.Count), back.Findings.Sum(f => f.Objects.Count));
        Assert.Equal(input.Score!.Score, back.Score!.Value);
        Assert.Equal(input.Score.IsUpperBound, back.Score.IsUpperBound);
        Assert.Equal(new ScanJsonTotals(100, 50, 40), back.Totals);
        Assert.Equal(CheckStatus.Failed, back.Checks.Single().Status);
        var asRep = back.Findings.Single(f => f.Category == "AsRepRoasting");
        Assert.Equal(FindingCatalog.Get("AsRepRoasting")!.RuleId, asRep.RuleId);
        Assert.Equal(input.Score.PointsFor("AsRepRoasting"), asRep.PointsIfFixed);
        Assert.Equal("S-1-5-21-1-2-3-1101", asRep.Objects.Single(o => o.Name == "jdoe0").Sid);

        var meta = File.ReadAllText(ExportMetadata.MetaPathFor(path));
        Assert.Contains(ExportMetadata.Sha256OfFile(path), meta);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));   // no BOM
    }

    [Fact]
    public void Json_TopLevelContract_CamelCase_EnumsAsText()
    {
        var json = ScanJsonExporter.Serialize(ScanJsonExporter.Build(JsonInput(Fixture())));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(ScanJsonExporter.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(ScoreModel.FormulaVersion, root.GetProperty("formulaVersion").GetInt32());
        Assert.Equal("High", root.GetProperty("findings")[0].GetProperty("severity").GetString());
        Assert.Equal("User", root.GetProperty("findings")[0].GetProperty("objects")[0].GetProperty("type").GetString());
        Assert.Equal("Failed", root.GetProperty("checks")[0].GetProperty("status").GetString());
    }

    [Fact]
    public void Json_CarriesExceptionStatePerObject()
    {
        var findings = Fixture();
        findings[0].Objects[2].Exception = ExceptionState.Expired;
        findings[0].Objects[2].ExceptionLabel = "expired 2026-01-01 — test";
        var back = ScanJsonExporter.Deserialize(ScanJsonExporter.Serialize(ScanJsonExporter.Build(JsonInput(findings))))!;
        var o = back.Findings.Single(f => f.Category == "AsRepRoasting").Objects.Single(x => x.Name == "jdoe2");
        Assert.Equal(ExceptionState.Expired, o.Exception.State);
        Assert.Equal("expired 2026-01-01 — test", o.Exception.Label);
    }

    // Secret-bearing AD attributes (CLAUDE.md: only via presence filters). None may ever be a field of the export.
    private static readonly string[] SecretAttributes =
    [
        "userPassword", "unicodePwd", "unixUserPassword", "dBCSPwd", "ntPwdHistory", "lmPwdHistory", "supplementalCredentials",
        "ms-Mcs-AdmPwd", "msLAPS-Password", "msLAPS-EncryptedPassword", "msLAPS-EncryptedPasswordHistory",
        "msLAPS-EncryptedDSRMPassword", "msFVE-RecoveryPassword", "msFVE-KeyPackage", "msDS-ManagedPassword",
    ];

    /// <summary>All property names in a JSON text, compared with '-' and '_' removed, case-insensitive.</summary>
    private static List<string> SecretFieldsIn(string json)
    {
        static string Norm(string s) => s.Replace("-", "").Replace("_", "").ToLowerInvariant();
        var secrets = SecretAttributes.Select(Norm).ToHashSet();
        var hits = new List<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject()) { if (secrets.Contains(Norm(p.Name))) hits.Add(p.Name); Walk(p.Value); }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Walk(x);
        }
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement);
        return hits;
    }

    [Fact]
    public void Json_SecretGuard_SelfTest_DetectsPlantedSecretField()
    {
        Assert.Equal(["msLAPS-Password"], SecretFieldsIn("""{"a":[{"msLAPS-Password":"x"}]}"""));
        Assert.Equal(["UnicodePwd"], SecretFieldsIn("""{"UnicodePwd":1}"""));
        Assert.Empty(SecretFieldsIn("""{"ms-Mcs-AdmPwdExpirationTime":1}"""));   // the LAPS expiry check is not a secret
    }

    [Fact]
    public void Json_NeverContainsSecretAttributeFields()
    {
        // Every finding of the catalog with an object, so all DTO paths are filled.
        var findings = FindingCatalog.All.Select(r => new AdFinding
            { Category = r.Category, Title = r.Category, Severity = FindingSeverity.High, Count = 1, Objects = [U("o-" + r.RuleId, 5000)] }).ToList();
        var json = ScanJsonExporter.Serialize(ScanJsonExporter.Build(JsonInput(findings)));
        Assert.Empty(SecretFieldsIn(json));

        // Also guard the contract types themselves (a new DTO property would appear here before it reaches a file).
        // Walks every type reachable from the document, so a new nested DTO is covered automatically.
        var norm = SecretAttributes.Select(s => s.Replace("-", "").ToLowerInvariant()).ToHashSet();
        var seen = new HashSet<Type>();
        var todo = new Stack<Type>([typeof(ScanJsonDocument)]);
        while (todo.Count > 0)
        {
            var t = todo.Pop();
            if (!seen.Add(t) || t.Assembly != typeof(ScanJsonDocument).Assembly && !t.IsGenericType) continue;
            foreach (var arg in t.GetGenericArguments()) todo.Push(arg);
            if (t.Assembly != typeof(ScanJsonDocument).Assembly) continue;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(p.Name.ToLowerInvariant(), norm);
                todo.Push(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType);
            }
        }
        Assert.Contains(typeof(ScanJsonObject), seen);           // the walk really reached the objects
    }

    // ── Dashboard wiring ─────────────────────────────────────────────────────
    [Fact]
    public void Dashboard_HasThreeExportCommands_GuardedByLiveScan()
    {
        // The toolkit's [RelayCommand] attribute is not kept in the assembly, so check the source.
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DirHealth.Desktop", "ViewModels", "DashboardViewModel.cs"));
        foreach (var name in new[] { "ExportWorklistAsync", "ExportWorkbookAsync", "ExportScanJsonAsync" })
            Assert.Matches(@"\[RelayCommand\(CanExecute = nameof\(HasScanData\)\)\]\s*public async Task " + name + @"\(", src);
        Assert.Matches(@"HasScanData\(\) => _liveScanCompleted && !IsBusy", src);
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DirHealth.Desktop", "Views", "Dashboard", "DashboardView.xaml"));
        foreach (var cmd in new[] { "ExportWorklistCommand", "ExportWorkbookCommand", "ExportScanJsonCommand" })
            Assert.Contains($"{{Binding {cmd}}}", xaml);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "DirHealth.Desktop"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
