using System.Text.Json;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Export.Reports;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

// Paket 3 (2026-11-16, review docs/…/review-export.md): PDF report polish — PowerShell wrap (F-01), wrapping
// table cells (F-02), redaction too wide/too narrow (F-03/F-04), page 1 for a CISO (V-11, F-05..F-07),
// .meta.txt everywhere (F-08/F-09), CSV numbers (F-28), JSON scan time (F-29).
[Collection(ExportHookCollection.Name)]
public class ReportPolishTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dh-p3-");
    public void Dispose() => _dir.Delete(recursive: true);
    private string P(string n) => Path.Combine(_dir.FullName, n);

    private static readonly DateTime Now = new(2026, 11, 16, 14, 3, 0, DateTimeKind.Local);

    private static AffectedObject U(string sam, int rid, AffectedObjectType type = AffectedObjectType.User) => new()
        { Name = sam, DistinguishedName = $"CN={sam},OU=Staff,DC=corp,DC=com", Sid = $"S-1-5-21-1-2-3-{rid}", Type = type };

    private static AdFinding F(string category, FindingSeverity sev, params AffectedObject[] objects) => new()
        { Category = category, Title = FindingCatalog.Get(category)!.What, Severity = sev, Count = objects.Length, Objects = [.. objects],
          Description = FindingCatalog.Get(category)!.What };

    private static ReportInput Input(IReadOnlyList<AdFinding> findings, IReadOnlyList<CheckResult>? checks = null,
        IReadOnlyList<ObjectException>? exceptions = null, IReadOnlyList<string>? admins = null,
        IReadOnlyList<AdUser>? inactive = null)
    {
        checks ??= [];
        var score = ScoreModel.Compute(FindingCatalog.All, findings, checks, new ScanTotals(1200, 300, 450));
        return new ReportInput("corp.com", findings, score, score.Score, checks, exceptions ?? [], null, null,
            inactive ?? [], [], admins ?? [], Now, "test",
            Totals: new ScanTotals(1200, 300, 450), DomainController: "DC01.corp.com", ScanTime: "16.11.2026 13:55:00");
    }

    // ── F-01 PowerShell wrap ─────────────────────────────────────────────────
    private const string Tier0Comment =
        "$sid = (Get-ADDomain).DomainSID.Value   # Enterprise/Schema/Enterprise Key Admins: use the forest root SID in child domains";
    private const string Tier0Array =
        "$t0 = \"$sid-512\",\"$sid-518\",\"$sid-519\",\"$sid-526\",\"$sid-527\",\"$sid-517\",'S-1-5-32-544','S-1-5-32-548','S-1-5-32-549','S-1-5-32-550','S-1-5-32-551' |";

    [Fact]
    public void Code_ShortLines_AndIndentation_AreKept()
    {
        var lines = TextWrap.Code("Import-Module ActiveDirectory\n    ForEach-Object { $_ }\n\tGet-X", 90);
        Assert.Equal(["Import-Module ActiveDirectory", "    ForEach-Object { $_ }", "    Get-X"], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.False(l.IsContinuation));
    }

    [Fact]
    public void Code_TrailingComment_TooLong_GoesOnOwnLines_ThatStayComments()
    {
        var lines = TextWrap.Code(Tier0Comment, 60);
        Assert.Equal("$sid = (Get-ADDomain).DomainSID.Value", lines[0].Text);       // the command is untouched
        Assert.All(lines.Skip(1), l => Assert.StartsWith("#", l.Text.TrimStart()));  // nothing after it can run
        Assert.Contains(lines, l => l.Text.StartsWith(TextWrap.CommentContinuation)); // the wrapped comment says so
        Assert.All(lines, l => Assert.True(l.Text.Length <= 60, l.Text));
        Assert.Contains("child domains", string.Join(" ", lines.Select(l => l.Text)));
    }

    [Fact]
    public void Code_LongArray_BreaksAfterCommas_AndJoinsBackToTheSource()
    {
        var lines = TextWrap.Code(Tier0Array, 60);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(l.Text.Length <= 60, l.Text));
        Assert.DoesNotContain(lines, l => l.Text.Contains(TextWrap.HardBreakMarker.Trim()));
        Assert.All(lines.Take(lines.Count - 1), l => Assert.EndsWith(",", l.Text));    // PowerShell continues after ","
        // continuation lines only add indentation: joined, they are the source line
        Assert.Equal(Tier0Array, string.Concat(lines.Select((l, i) => i == 0 ? l.Text : l.Text.TrimStart())));
    }

    [Fact]
    public void Code_BlankBreak_UsesBacktick_AndNeverSplitsAString()
    {
        const string src = "Get-ADUser -Filter \"Name -like 'svc_*' -and Enabled -eq $true -and Description -notlike '*keep*'\" -Properties Description, PasswordLastSet";
        var lines = TextWrap.Code(src, 91);
        Assert.All(lines, l => Assert.True(l.Text.Length <= 91, l.Text));
        // the quoted filter is printed in one piece
        Assert.Contains(lines, l => l.Text.Contains("\"Name -like 'svc_*' -and Enabled -eq $true -and Description -notlike '*keep*'\""));
        Assert.All(lines.Take(lines.Count - 1), l => Assert.True(l.Text.EndsWith(" `") || l.Text.EndsWith(",") || l.Text.EndsWith("|"), l.Text));
    }

    [Fact]
    public void Code_NoSafePoint_IsBrokenHard_AndMarkedAsNotPowerShell()
    {
        var token = "$" + new string('a', 150);          // a bare token: no blank, no quote, no operator
        var lines = TextWrap.Code(token, 50);
        Assert.True(lines.Count >= 3);
        Assert.All(lines, l => Assert.True(l.Text.Length <= 50, l.Text));
        Assert.All(lines.Skip(1), l => Assert.StartsWith(TextWrap.HardBreakMarker, l.Text.TrimStart()));
        Assert.Equal(token, string.Concat(lines.Select((l, i) => i == 0 ? l.Text : l.Text.TrimStart()[TextWrap.HardBreakMarker.Length..])));
    }

    [Fact]
    public void Code_LongString_IsSplitIntoAConcatenation_WithTheSameValue()
    {
        const string src = "$t = Get-ADUser -LDAPFilter \"(&(objectClass=user)(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(pwdLastSet<=$cutoff))\" -Properties pwdLastSet";
        var lines = TextWrap.Code(src, 60);
        Assert.All(lines, l => Assert.True(l.Text.Length <= 60, l.Text));
        Assert.DoesNotContain(lines, l => l.Text.Contains(TextWrap.HardBreakMarker.Trim()));
        Assert.Contains(lines, l => l.Text.Contains("$cutoff"));                       // never split inside a variable
        // undo the print wrap: string parts joined, backtick continuations joined → the source plus "( … )"
        var flat = System.Text.RegularExpressions.Regex.Replace(string.Join("\n", lines.Select(l => l.Text)), "\" \\+\\n\\s*\"", "");
        flat = System.Text.RegularExpressions.Regex.Replace(flat, " `\\n\\s*", " ");
        Assert.Equal(src.Replace("\"(&", "(\"(&").Replace("))\" -Prop", "))\") -Prop"), flat);
    }

    [Fact]
    public void Code_SplitPoints_NeverInsideVariablesSubexpressionsOrEscapes()
    {
        var content = "a $sid-5 $(x y) `\"q\"\"z";
        var ok = TextWrap.SplitPoints(content, '"');
        Assert.False(ok[content.IndexOf("sid")]);                 // between $ and the name
        Assert.False(ok[content.IndexOf("id")]);                  // inside the name
        Assert.True(ok[content.IndexOf("-5")]);                   // after the name
        Assert.False(ok[content.IndexOf(" y")]);                  // inside $( … )
        Assert.False(ok[content.IndexOf('`') + 1]);               // between ` and the escaped char
        Assert.False(ok[content.LastIndexOf('"')]);               // inside a "" pair
        Assert.False(TextWrap.SplitPoints("it''s", '\'')[3]);    // inside a '' pair
    }

    [Theory]
    [InlineData(91)]   // A4 portrait, Courier New 9 pt
    [InlineData(40)]
    public void Code_EveryCatalogScript_FitsTheLine_AndCommentsStayComments(int cols)
    {
        foreach (var r in FindingCatalog.All)
            foreach (var script in new[] { r.Fix, r.Revert })
            {
                var lines = TextWrap.Code(script, cols);
                Assert.All(lines, l => Assert.True(l.Text.Length <= cols, $"{r.RuleId}: {l.Text}"));
                if (cols >= 91)   // at print width every catalog line has a PowerShell-safe break point
                    Assert.DoesNotContain(lines, l => l.Text.TrimStart().StartsWith(TextWrap.HardBreakMarker));
                // a wrapped comment line never turns into code
                foreach (var src in script.Split('\n').Where(x => x.TrimStart().StartsWith('#')))
                    Assert.All(TextWrap.Code(src, cols), l => Assert.StartsWith("#", l.Text.TrimStart()));
            }
    }

    [Fact]
    public void Words_LongTokenIsBroken_EveryLineFits()
    {
        var url = "https://learn.microsoft.com/" + new string('x', 120);
        var lines = TextWrap.Words("See " + url + " for details", 40, s => s.Length);
        Assert.All(lines, l => Assert.True(l.Length <= 40, l));
        Assert.Equal("See" + url + "fordetails", string.Concat(lines).Replace(" ", ""));
    }

    [Fact]
    public void ColumnWidths_SumToTotal_NarrowColumnsKeepTheirWidth_WeightsWin()
    {
        var w = TextWrap.ColumnWidths([20, 600, 30], 500);
        Assert.Equal(500, w.Sum(), 6);
        Assert.Equal(36, w[0], 6);                      // minimum width
        Assert.Equal(36, w[2], 6);
        Assert.Equal(428, w[1], 6);                     // the long text gets the rest
        var weighted = TextWrap.ColumnWidths([1, 1], 300, [0.25, 0.75]);
        Assert.Equal([75.0, 225.0], weighted);
    }

    // ── F-03 / F-04 redaction ────────────────────────────────────────────────
    [Fact]
    public void Redaction_KeepsBuiltInAccountsAndGroups_BySid()
    {
        var findings = new List<AdFinding>
        {
            F("KrbtgtPasswordOld", FindingSeverity.High, U("krbtgt", 502)),
            F("GuestEnabled", FindingSeverity.High, U("Gast", 501)),                         // localized name, caught by RID
            F("AsRepRoasting", FindingSeverity.High, U("jdoe", 1105)),
        };
        findings[0].Title = "krbtgt Password Older Than 180 Days";
        findings[1].Title = "Built-in Guest Account Is Enabled";
        var m = ReportBuilder.Build(Input(findings, admins: ["Administrator", "Domain Admins", "Domänen-Admins", "mmuster"]),
            new ReportOptions(ReportKind.Full, RedactUserNames: true));
        var cells = m.Findings.SelectMany(f => f.Objects.Rows.Select(r => r[0])).ToList();
        Assert.Contains("krbtgt", cells);
        Assert.Contains("Gast", cells);
        Assert.DoesNotContain("jdoe", cells);
        var admins = m.Appendix.Single(a => a.Title == "Domain Admins").Table.Rows.Select(r => r[0]).ToList();
        Assert.Equal(["Administrator", "Domain Admins", "Domänen-Admins", "account-001"], admins);
        // titles are scanner text — never touched (were "user-004 Password …" / "Built-in user-007 Account …")
        Assert.Contains(m.Findings, f => f.Title == "krbtgt Password Older Than 180 Days");
        Assert.Contains(m.Findings, f => f.Title == "Built-in Guest Account Is Enabled");
    }

    [Fact]
    public void Redaction_ShortAccountName_DoesNotGarbleEnglishText()
    {
        var findings = new List<AdFinding> { F("AsRepRoasting", FindingSeverity.High, U("it", 1201), U("admin", 1202)) };
        findings[0].Description = "Each admin account listed here can be attacked; it is reachable offline.";
        var ex = new List<ObjectException>
            { new(FindingCatalog.Get("AsRepRoasting")!.RuleId, "S-1-5-21-1-2-3-1201", "it", "kept because it runs the legacy admin tool", Now.ToUniversalTime().AddDays(30), "CORP\\boss", Now.ToUniversalTime()) };
        var m = ReportBuilder.Build(Input(findings, exceptions: ex), new ReportOptions(ReportKind.Full, true));
        var f = m.Findings.Single();
        Assert.Equal("Each admin account listed here can be attacked; it is reachable offline.", f.Description);
        Assert.All(f.Objects.Rows, r => Assert.StartsWith("user-", r[0]));         // whole fields: any length
        var e = Assert.Single(m.Exceptions);
        Assert.StartsWith("user-", e.Object);
        // free text: "admin" (≥ 3 chars, a known account) is replaced, the 2-letter "it" is not
        Assert.Equal("kept because it runs the legacy user-001 tool", e.Reason);
    }

    [Fact]
    public void Redaction_ExceptionCreator_IsRedacted_EvenWhenNoListNamesHim()
    {
        // findings-tab PDF: no user lists — the creator used to stay in clear text (F-04)
        var findings = new List<AdFinding> { F("InactiveComputers", FindingSeverity.Low, U("WS01", 3001, AffectedObjectType.Computer)) };
        var ex = new List<ObjectException>
            { new(FindingCatalog.Get("InactiveComputers")!.RuleId, "S-1-5-21-1-2-3-3001", "WS01", "lab machine of jsmith", Now.ToUniversalTime().AddDays(30), "CORP\\jsmith", Now.ToUniversalTime()) };
        var m = ReportBuilder.Build(Input(findings, exceptions: ex),
            new ReportOptions(ReportKind.Full, true, FilterScope: "all findings (1 of 1 findings)"));
        var e = Assert.Single(m.Exceptions);
        Assert.Equal("WS01", e.Object);                                  // a computer is not a person, never "user-…"
        Assert.Matches("^user-\\d{3}$", e.CreatedBy);
        Assert.Equal($"lab machine of {e.CreatedBy}", e.Reason);         // known name inside free text replaced too

        var text = new List<string>();
        ReportPdfRenderer.Render(m, P("redacted.pdf"), text.Add);
        Assert.DoesNotContain(text, t => t.Contains("jsmith", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(text, t => t.Contains("names redacted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Redactor_RedactAlways_UnknownValue_BecomesPlaceholder()
    {
        var r = new Redactor([["jdoe"]]);
        Assert.Equal(Redactor.RedactedPlaceholder, r.RedactAlways("OTHER\\someone"));
        Assert.Equal("user-001", r.RedactAlways("jdoe"));
        Assert.Equal("", r.RedactAlways(""));
    }

    [Fact]
    public void Redactor_TypedAliases_AndMergedIdentities()
    {
        var r = new Redactor([(RedactKind.Account, (IReadOnlyCollection<string>)["jdoe"]),
                              (RedactKind.User, ["Jane Doe", "jdoe"]),
                              (RedactKind.Account, ["svc-old"])]);
        Assert.Equal("user-001", r.Apply("jdoe"));          // shares "jdoe" with a user → one person, typed "user"
        Assert.Equal("user-001", r.Apply("Jane Doe"));
        Assert.Equal("account-001", r.Apply("svc-old"));
        Assert.Equal(2, r.Count);
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-500", "renamed-admin", true)]
    [InlineData("S-1-5-21-1-2-3-502", "krbtgt", true)]
    [InlineData("S-1-5-32-544", "Administrators", true)]
    [InlineData("S-1-5-21-1-2-3-1105", "jdoe", false)]
    [InlineData("", "Guest", true)]
    [InlineData("", "Enterprise Admins", true)]
    [InlineData("", "jdoe", false)]
    public void Redactor_IsWellKnown_BySidThenName(string sid, string name, bool expected)
    {
        Assert.Equal(expected, Redactor.IsWellKnown(sid, name));
    }

    // ── V-11 / F-05..F-07 page 1 ─────────────────────────────────────────────
    [Fact]
    public void Page1_HasSentences_Legend_Scope_AndFailedChecks()
    {
        var findings = new List<AdFinding> { F("AsRepRoasting", FindingSeverity.High, U("jdoe", 1105)) };
        var failing = FindingCatalog.All.Where(r => r.Category != "AsRepRoasting").Take(7).Select(r => r.Category).ToHashSet();
        var checks = FindingCatalog.All.Where(r => failing.Contains(r.Category)).Select(r => new CheckResult(r.Category, r.RuleId, CheckStatus.Failed, "LDAP timeout", 0))
            .Concat(FindingCatalog.All.Where(r => !failing.Contains(r.Category)).Select(r => new CheckResult(r.Category, r.RuleId,
                r.Category == "AsRepRoasting" ? CheckStatus.Found : CheckStatus.Passed, "", 0))).ToList();
        var m = ReportBuilder.Build(Input(findings, checks), new ReportOptions(ReportKind.Summary, false));

        Assert.InRange(m.SummarySentences.Count, 2, 3);
        Assert.Contains("/100", m.SummarySentences[0]);
        Assert.Contains("1 open finding, 1 high", m.SummarySentences[0]);
        Assert.Contains("7 failed checks", m.SummarySentences[^1]);
        Assert.Equal(ReportBuilder.ScoreLegendText, m.ScoreLegend);

        var scope = m.Scope.ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("corp.com", scope["Domain"]);
        Assert.Equal("DC01.corp.com", scope["Domain controller"]);
        Assert.Equal("2026-11-16 13:55", scope["Scan time"]);            // report date format (ISO, local)
        Assert.Equal("1,200 users · 300 groups · 450 computers", scope["Objects read"]);
        Assert.Contains("7 failed", scope["Checks"]);
        Assert.StartsWith($"{FindingCatalog.All.Count} rules", scope["Checks"]);

        Assert.Equal(ReportBuilder.FailedChecksOnPageOne + 1, m.FailedChecksShort.Count);    // 5 + "2 more"
        Assert.StartsWith("2 more", m.FailedChecksShort[^1]);
        Assert.Contains("LDAP timeout", m.FailedChecksShort[0]);
    }

    [Fact]
    public void Page1_NoCheckResults_AreCountedAsSuch_NotListedAsFailed()
    {
        // findings tab without scan context / older scan: no check ran — not the same as "failed"
        var m = ReportBuilder.Build(Input([F("AsRepRoasting", FindingSeverity.High, U("jdoe", 1105))]), new ReportOptions(ReportKind.Summary, false));
        Assert.Empty(m.FailedChecksShort);
        Assert.Contains($"{FindingCatalog.All.Count - 1} without a result in this scan", m.Scope.Single(l => l.Label == "Checks").Value);
        Assert.DoesNotContain(m.SummarySentences, s => s.Contains("failed"));
    }

    [Fact]
    public void Page1_ScoreUnavailable_IsNeutral_NotRedCritical()
    {
        var findings = new List<AdFinding> { F("AsRepRoasting", FindingSeverity.High, U("jdoe", 1105)) };
        var checks = FindingCatalog.All.Select(r => new CheckResult(r.Category, r.RuleId, CheckStatus.Failed, "no permission", 0)).ToList();
        var m = ReportBuilder.Build(Input(findings, checks), new ReportOptions(ReportKind.Summary, false));
        Assert.False(m.ScoreAvailable);
        Assert.Equal($"Score not available — {FindingCatalog.All.Count} of {FindingCatalog.All.Count} checks failed", m.ScoreText);
        Assert.Equal("", m.ScoreLabel);
        Assert.Null(m.ScoreNote);
        Assert.StartsWith("No score could be computed", m.SummarySentences[0]);

        var text = new List<string>();
        ReportPdfRenderer.Render(m, P("na.pdf"), text.Add);
        Assert.DoesNotContain(text, t => t.Contains("n/a") || t.Contains("/ 100"));
        Assert.Contains(text, t => t.StartsWith("Score not available"));
    }

    [Fact]
    public void Page1_SeverityCounts_MatchTheRiskMatrix_ExceptedNamedSeparately()
    {
        var excepted = F("PasswordNeverExpires", FindingSeverity.Medium, U("old", 1300));
        excepted.IsAcknowledged = true;
        var findings = new List<AdFinding>
        {
            F("AdminKerberoastable", FindingSeverity.Critical, U("svc-sql", 1101)),
            F("AsRepRoasting", FindingSeverity.High, U("jdoe", 1105)),
            excepted,
        };
        var m = ReportBuilder.Build(Input(findings), new ReportOptions(ReportKind.Summary, false));
        var matrixBySeverity = m.RiskMatrix.Severities.Select((s, c) => (s, m.RiskMatrix.Rows.Sum(r => r.Counts[c]))).ToList();
        Assert.Equal(matrixBySeverity.Select(x => $"{x.Item1}:{x.Item2}"), m.SeverityCounts.Select(x => $"{x.Severity}:{x.Count}"));
        Assert.Equal("Medium:0", $"{m.SeverityCounts[2].Severity}:{m.SeverityCounts[2].Count}");
        Assert.Equal("+1 fully excepted finding, not counted above", m.ExceptedNote);
    }

    // ── F-02 + F-01 in a real render: long and many lines, no crash, nothing cut ─
    [Fact]
    public void Render_FullReport_LongCellsAndCode_WrapsInsteadOfCutting()
    {
        var longReason = string.Join(" ", Enumerable.Repeat("approved by the change board for the migration window", 12)) + " ENDMARK";
        var objects = Enumerable.Range(0, 60).Select(i => U($"user{i:D2}-with-a-rather-long-account-name-{new string('x', 30)}", 2000 + i)).ToArray();
        var findings = new List<AdFinding>
        {
            F("AdminNotProtected", FindingSeverity.High, objects),     // Fix/Undo start with the Tier 0 preamble (long lines)
            F("AsRepRoasting", FindingSeverity.High, objects[..3]),
        };
        var ex = new List<ObjectException>
            { new(FindingCatalog.Get("AsRepRoasting")!.RuleId, objects[0].Sid, objects[0].Name, longReason, Now.ToUniversalTime().AddDays(30), "CORP\\boss", Now.ToUniversalTime()) };
        var checks = new List<CheckResult> { new("SidHistory", FindingCatalog.Get("SidHistory")!.RuleId, CheckStatus.Failed, longReason, 0) };
        var inactive = Enumerable.Range(0, 200).Select(i => new AdUser
            { DisplayName = $"Person {i}", SamAccountName = $"p{i}", DistinguishedName = $"CN=Person {i},OU=Very Long Organisational Unit Name {i},OU=Staff,DC=corp,DC=com" }).ToList();
        var m = ReportBuilder.Build(Input(findings, checks, ex, ["Administrator"], inactive), new ReportOptions(ReportKind.Full, false));

        var text = new List<string>();
        var path = P("full.pdf");
        ReportPdfRenderer.Render(m, path, text.Add);

        Assert.True(ReportPdfRenderer.PageCount(path) >= 3);
        Assert.Contains(text, t => t.EndsWith("ENDMARK"));                         // the reason is wrapped, not cut
        Assert.Contains("$sid = (Get-ADDomain).DomainSID.Value", text);            // code line intact
        Assert.Contains(text, t => t.TrimStart().StartsWith("# ") && t.Contains("child domains"));
        Assert.True(text.Count(t => t == "OBJECT") >= 2);                          // header repeated after the break
        Assert.Contains(text, t => t.StartsWith("Long lines are wrapped for print"));
        Assert.Contains(text, t => t.StartsWith("DirHealth test · corp.com · created"));   // footer reaches the sink (V-25)
        Assert.Contains(text, t => t.EndsWith("(continued)") && t.Contains(m.Findings[0].RuleId));   // V-13
    }

    // ── F-08 / F-09 / backlog: .meta.txt ─────────────────────────────────────
    [Fact]
    public void Meta_SafeContext_And_Redacted()
    {
        var ctx = ExportMetadata.SafeContext(() => throw new InvalidOperationException("no DC"), "corp.com");
        Assert.Equal(("corp.com", "unknown"), (ctx.Domain, ctx.DomainController));
        Assert.Equal("DC01", ExportMetadata.SafeContext(() => new ExportContext("corp.com", "DC01", "CORP\\a")).DomainController);
        Assert.Equal(Redactor.RedactedPlaceholder, ExportMetadata.Redacted(new ExportContext("corp.com", "DC01", "CORP\\a")).User);
    }

    private FindingsViewModel FindingsVm(IReadOnlyList<AdFinding> findings)
    {
        var vm = new FindingsViewModel(new ExceptionStore(P("exceptions.json")), new AcknowledgeStore(P("acknowledged.json")))
        {
            ExportContextProvider = () => new ExportContext("corp.com", "DC01.corp.com", "CORP\\operator"),
        };
        vm.LoadFindings(findings.ToList(), 70);
        var input = Input(findings);
        vm.ReportContext = () => new FindingsReportContext("corp.com", input.Score, input.StoredScore, []);
        return vm;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindingsPdf_Meta_NamesTheDc_AndHidesTheUserWhenRedacted(bool redact)
    {
        var path = P($"findings-{redact}.pdf");
        var vm = FindingsVm([F("AsRepRoasting", FindingSeverity.High, U("jdoe", 1105))]);
        var (ask, pick) = (FindingsViewModel.AskReportOptions, FindingsViewModel.PickReportPath);
        try
        {
            FindingsViewModel.AskReportOptions = () => new ReportOptions(ReportKind.Full, redact);
            FindingsViewModel.PickReportPath   = _ => path;
            vm.ExportPdfCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        }
        finally { (FindingsViewModel.AskReportOptions, FindingsViewModel.PickReportPath) = (ask, pick); }

        var meta = File.ReadAllText(ExportMetadata.MetaPathFor(path));
        Assert.Contains("Domain controller : DC01.corp.com", meta);
        Assert.Equal(!redact, meta.Contains("CORP\\operator"));
    }

    // ── F-28 CSV numbers ─────────────────────────────────────────────────────
    [Theory]
    [InlineData("-5")]
    [InlineData("-0.25")]
    [InlineData("42")]
    [InlineData("-120")]
    public void SafeField_PlainNumbers_StayNumbers(string value)
    {
        Assert.Equal(value, CsvExporter.SafeField(value));
    }

    [Theory]
    [InlineData("-1+cmd|' /C calc'!A0")]
    [InlineData("-5 ")]
    [InlineData("-5\n")]
    [InlineData("－5")]
    [InlineData("-5e3")]
    [InlineData("-1,5")]
    [InlineData("-٥")]          // Arabic-Indic digit: not an ASCII number
    [InlineData(" -5")]
    public void SafeField_AnythingButAPlainNumber_IsStillDefused(string value)
    {
        Assert.Equal("'" + value, CsvExporter.SafeField(value));
    }

    [Fact]
    public void ExportTable_NegativeDays_WithoutApostrophe()
    {
        var path = P("pw.csv");
        var cols = new List<ITableColumn> { new TableColumn<AdUser>("Days", u => u.DaysUntilPasswordExpiry) };
        new CsvExporter(",").ExportTable([new AdUser { DaysUntilPasswordExpiry = -5 }], cols, path);
        Assert.Equal(["Days", "-5"], File.ReadAllLines(path));
    }

    // ── F-29 JSON scan time ──────────────────────────────────────────────────
    [Fact]
    public void Json_ScanTimeUtc_IsIso8601Utc_DisplayFieldKept_SchemaUnchanged()
    {
        var input = new ScanJsonInput([], [], null, null, "corp.com", "DC01", "16.11.2026 13:55:00", new DateTime(2026, 11, 16, 13, 0, 0, DateTimeKind.Utc), "test");
        var doc = ScanJsonExporter.Build(input);
        var expected = new DateTime(2026, 11, 16, 13, 55, 0, DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(expected, doc.ScanTimeUtc);
        Assert.Equal(DateTimeKind.Utc, doc.ScanTimeUtc!.Value.Kind);
        Assert.Equal(1, doc.SchemaVersion);                     // additive field: no breaking change (ADR 0005)

        using var json = JsonDocument.Parse(ScanJsonExporter.Serialize(doc));
        Assert.Equal("16.11.2026 13:55:00", json.RootElement.GetProperty("scanTime").GetString());
        Assert.Equal(expected.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), json.RootElement.GetProperty("scanTimeUtc").GetString());
    }

    [Theory]
    [InlineData("Never")]
    [InlineData("")]
    [InlineData("2026-11-16 13:55")]
    public void Json_ScanTimeUtc_UnknownOrForeignFormat_IsNull(string scanTime)
    {
        Assert.Null(ScanJsonExporter.ScanTimeUtcOf(scanTime));
    }
}
