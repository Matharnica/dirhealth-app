using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Export.Reports;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;
using Xunit;

namespace DirHealth.Tests;

/// <summary>
/// Renders every PDF the app can produce from realistic synthetic data (fictional domain corp.example.com),
/// through the real production paths (ReportBuilder → Redactor → ReportPdfRenderer, PdfExporter). CI uploads
/// the files as the artifact "report-previews" (env REPORT_PREVIEW_DIR), so the layout can be looked at
/// page by page without a Windows machine. The assertions here only guard that each file is a real PDF;
/// content rules live in the report/redaction tests.
/// </summary>
public class ReportPreviewTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 14, 5, 0, DateTimeKind.Local);

    private static string OutDir()
    {
        var env = Environment.GetEnvironmentVariable("REPORT_PREVIEW_DIR");
        var dir = string.IsNullOrWhiteSpace(env) ? Path.Combine(Path.GetTempPath(), "dirhealth-report-previews") : env;
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static readonly string[] First = ["anna", "ben", "clara", "david", "eva", "felix", "greta", "henrik", "ida", "jonas",
                                              "karla", "lukas", "mia", "noah", "olga", "paul", "rosa", "simon", "tara", "uwe"];
    private static readonly string[] Last  = ["schmidt", "meyer", "wagner", "becker", "hoffmann", "koch", "richter", "klein",
                                              "wolf", "neumann", "schwarz", "zimmermann", "braun", "hartmann", "lange"];

    private static string Sam(int i) => $"{First[i % First.Length]}.{Last[(i * 7) % Last.Length]}";
    private static string Display(int i) => $"{char.ToUpperInvariant(First[i % First.Length][0])}{First[i % First.Length][1..]} " +
                                            $"{char.ToUpperInvariant(Last[(i * 7) % Last.Length][0])}{Last[(i * 7) % Last.Length][1..]}";

    private static AffectedObject User(int i, string detail = "") => new()
    {
        Name = Sam(i), Type = AffectedObjectType.User, Detail = detail,
        DistinguishedName = $"CN={Display(i)},OU=Staff,OU=Berlin,DC=corp,DC=example,DC=com",
        Sid = $"S-1-5-21-3623811015-3361044348-30300820-{1100 + i}",
    };

    private static AffectedObject Computer(string name, string detail = "", bool dc = false) => new()
    {
        Name = name, Type = AffectedObjectType.Computer, Detail = detail, IsDomainController = dc,
        DistinguishedName = $"CN={name},OU=Workstations,DC=corp,DC=example,DC=com",
        Sid = $"S-1-5-21-3623811015-3361044348-30300820-{5000 + Math.Abs(name.GetHashCode()) % 900}",
    };

    /// <summary>~20 findings across all severities, taken from the live catalog (so texts, fix blocks and
    /// references are the real ones), with object lists from 1 to 60 entries.</summary>
    private static List<AdFinding> Findings()
    {
        // a mid-sized domain in typical shape: a few severe, small findings and broad hygiene findings
        var plan = new (string Category, FindingSeverity Severity, int Objects)[]
        {
            ("PasswordNotRequired",              FindingSeverity.Critical, 1),
            ("KerberoastableAccounts",           FindingSeverity.High,     2),
            ("AsRepRoasting",                    FindingSeverity.High,     1),
            ("ComputersWithoutLaps",             FindingSeverity.High,    48),
            ("KrbtgtPasswordOld",                FindingSeverity.Medium,   1),
            ("PasswordNeverExpires",             FindingSeverity.Medium,  60),
            ("StaleDomainAdmins",                FindingSeverity.Medium,   1),
            ("EolOperatingSystems",              FindingSeverity.Medium,   1),
            ("InactiveUsers",                    FindingSeverity.Low,     34),
            ("InactiveComputers",                FindingSeverity.Low,     27),
            ("EmptyGroups",                      FindingSeverity.Low,     18),
            ("SingleMemberGroups",               FindingSeverity.Low,     12),
            ("AdminSdHolderAccounts",            FindingSeverity.Low,      5),
        };
        var list = new List<AdFinding>();
        for (int k = 0; k < plan.Length; k++)
        {
            if (FindingCatalog.Get(plan[k].Category) is not { } r) continue;
            int n = plan[k].Objects;
            var sevK = plan[k].Severity;
            bool computers = r.Category.Contains("Computer") || r.Category == "EolOperatingSystems";
            var objs = Enumerable.Range(0, n).Select(i => computers
                ? Computer($"WS-BER-{100 + i:D3}", i % 3 == 0 ? "Windows Server 2012 R2 Standard (end of support 2023-10-10)" : "Windows 10 Enterprise 21H2")
                : User(k * 3 + i, i % 4 == 0 ? $"last logon {2024 - i % 3}-0{1 + i % 9}-1{i % 9}, member of {ReportText.Count(1 + i % 5, "privileged group")}" : ""))
                .ToList();
            if (k == 1 && objs.Count > 0) { objs[0].Exception = ExceptionState.Active; objs[0].ExceptionLabel = "Excepted until 2027-01-31 — legacy ERP service"; }
            list.Add(new AdFinding
            {
                Category = r.Category, Severity = sevK, Count = n, Objects = objs,
                Title = k == 5 ? $"{n} accounts have passwords that never expire, including service accounts used by line-of-business applications"
                               : r.What.Length > 0 ? r.What : r.Category,
                Description = r.Why,
            });
        }
        return list;
    }

    private static ReportInput Input(List<AdFinding> findings, bool failed = true)
    {
        var checks = FindingCatalog.All.Select(r =>
        {
            var f = findings.FirstOrDefault(x => x.Category == r.Category);
            return f is not null ? new CheckResult(r.Category, r.RuleId, CheckStatus.Found, "", f.Count)
                                 : new CheckResult(r.Category, r.RuleId, CheckStatus.Passed, "", 0);
        }).ToList();
        if (failed)
        {
            int idx = checks.FindIndex(c => c.Status == CheckStatus.Passed);
            checks[idx] = checks[idx] with { Status = CheckStatus.Failed, Reason = "LDAP query timed out after 120 s (DC01.corp.example.com, size limit 1000)" };
            int na = checks.FindLastIndex(c => c.Status == CheckStatus.Passed);
            checks[na] = checks[na] with { Status = CheckStatus.NotApplicable, Reason = "No forest trusts configured" };
        }
        var totals = new ScanTotals(1842, 312, 655);
        var score  = ScoreModel.Compute(FindingCatalog.All, findings, checks, totals);
        var exceptions = new List<ObjectException>
        {
            new(findings[1].RuleId, findings[1].Objects[0].Sid, findings[1].Objects[0].Name,
                "Legacy ERP service account; vendor migration scheduled for Q1 2027, approved by CAB-2026-114",
                Now.ToUniversalTime().AddDays(76), "CORP\\it.admin", Now.ToUniversalTime().AddDays(-20)),
        };
        var diff = new ScanDiff
        {
            NewFindings = [findings[3], findings[7]],
            ResolvedFindings = [new AdFinding { Title = "Built-in Guest account is enabled", Category = "GuestEnabled" }],
            ChangedFindings = [new ScanChange(findings[2], +4), new ScanChange(findings[6], -3)],
        };
        var inactive = Enumerable.Range(0, 30).Select(i => new AdUser
        {
            DisplayName = Display(i + 40), SamAccountName = Sam(i + 40), Email = $"{Sam(i + 40)}@corp.example.com",
            DistinguishedName = $"CN={Display(i + 40)},OU=Staff,OU=Hamburg,DC=corp,DC=example,DC=com",
            LastLogon = Now.AddDays(-120 - i * 9), Sid = $"S-1-5-21-3623811015-3361044348-30300820-{2100 + i}",
        }).ToList();
        var expiring = PasswordUsers().Take(12).ToList();
        return new ReportInput("corp.example.com", findings, score, score.Score, checks, exceptions, diff, -6,
            inactive, expiring, ["Administrator", Sam(1), Sam(2), "svc.backup"],
            Now, ExportMetadata.AppVersion, totals, "DC01.corp.example.com", "16.11.2026 13:52:10",
            History: [.. new[] { 48, 51, 50, 55, 58, 57, 61, 66, 64 }.Select((s, n) => new ScoreEntry(Now.ToUniversalTime().AddDays(-7 * (9 - n)), s, ScoreModel.FormulaVersion)),
                      new ScoreEntry(Now.ToUniversalTime(), score.Score, ScoreModel.FormulaVersion)]);
    }

    private static List<AdUser> PasswordUsers() => Enumerable.Range(0, 40).Select(i =>
    {
        int days = -6 + i * 2;
        return new AdUser
        {
            DisplayName = i == 5 ? "Maximiliane Alexandra von Hohenzollern-Sigmaringen (External Consultant)" : Display(i),
            SamAccountName = Sam(i), Email = i == 5 ? "maximiliane.von-hohenzollern-sigmaringen@consulting.corp.example.com" : $"{Sam(i)}@corp.example.com",
            DistinguishedName = $"CN={Display(i)},OU=Staff,DC=corp,DC=example,DC=com",
            PasswordExpires = Now.Date.AddDays(days), DaysUntilPasswordExpiry = days,
        };
    }).ToList();

    private static UserDossier Dossier()
    {
        var u = new AdUser
        {
            SamAccountName = "henrik.wolf", DisplayName = "Henrik Wolf", Email = "henrik.wolf@corp.example.com",
            DistinguishedName = "CN=Henrik Wolf,OU=Admins,OU=IT,DC=corp,DC=example,DC=com", Department = "IT Operations",
            Title = "Senior Systems Engineer", IsEnabled = true, AdminCount = true, HasSpn = true, Spns = ["MSSQLSvc/sql01.corp.example.com:1433"],
            LastLogon = Now.AddDays(-2), PasswordLastSet = Now.AddDays(-412), WhenCreated = new DateTime(2019, 3, 4),
            Sid = "S-1-5-21-3623811015-3361044348-30300820-1188",
        };
        var groups = new List<AdUserGroup>
        {
            new() { Name = "Domain Admins", Kind = GroupMembershipKind.Nested, Sid = "S-1-5-21-3623811015-3361044348-30300820-512",
                    DistinguishedName = "CN=Domain Admins,CN=Users,DC=corp,DC=example,DC=com", Path = "henrik.wolf → IT-Admins → Domain Admins" },
            new() { Name = "IT-Admins", Kind = GroupMembershipKind.Direct, Sid = "S-1-5-21-3623811015-3361044348-30300820-3001",
                    DistinguishedName = "CN=IT-Admins,OU=Groups,DC=corp,DC=example,DC=com", Path = "henrik.wolf → IT-Admins" },
            new() { Name = "Domain Users", Kind = GroupMembershipKind.Primary, Sid = "S-1-5-21-3623811015-3361044348-30300820-513",
                    DistinguishedName = "CN=Domain Users,CN=Users,DC=corp,DC=example,DC=com", Path = "henrik.wolf → Domain Users" },
        };
        groups.AddRange(Enumerable.Range(0, 14).Select(i => new AdUserGroup
        {
            Name = $"APP-{(i % 2 == 0 ? "SharePoint" : "FileShare")}-{i:D2}-ReadWrite", Kind = i % 3 == 0 ? GroupMembershipKind.Nested : GroupMembershipKind.Direct,
            Sid = $"S-1-5-21-3623811015-3361044348-30300820-{3100 + i}", DistinguishedName = $"CN=APP-{i:D2},OU=Groups,DC=corp,DC=example,DC=com",
            Path = i % 3 == 0 ? $"henrik.wolf → IT-Admins → APP-Owners-{i:D2} → APP-{(i % 2 == 0 ? "SharePoint" : "FileShare")}-{i:D2}-ReadWrite"
                              : $"henrik.wolf → APP-{(i % 2 == 0 ? "SharePoint" : "FileShare")}-{i:D2}-ReadWrite",
        }));
        var findings = FindingCatalog.All.Where(r => r.RuleId.StartsWith("DH-USR", StringComparison.Ordinal) || r.RuleId.StartsWith("DH-KRB", StringComparison.Ordinal))
            .Take(4).Select((r, n) => new AdFinding { Category = r.Category, Title = r.What, Severity = n == 0 ? FindingSeverity.High : n == 1 ? FindingSeverity.Critical : FindingSeverity.Medium })
            .ToList();
        return new UserDossier(u, UserDetailRows.Build(u, Now.AddDays(-2)), groups, findings,
            new ExportContext("corp.example.com", "DC01.corp.example.com", "CORP\\it.admin"), "16.11.2026 13:52:10",
            Now.ToUniversalTime(), ExportMetadata.AppVersion);
    }

    // ── wording: real plurals and one date format in every PDF ──────────────
    [Theory]
    [InlineData(1, "failed check", null, "1 failed check")]
    [InlineData(13, "open finding", null, "13 open findings")]
    [InlineData(0, "account", null, "0 accounts")]
    [InlineData(1842, "user", null, "1,842 users")]
    [InlineData(2, "is", "are", "2 are")]
    public void Count_Pluralises(int n, string noun, string? plural, string expected) =>
        Assert.Equal(expected, ReportText.Count(n, noun, plural));

    [Fact]
    public void Dates_OneInvariantFormat()
    {
        Assert.Equal("2026-11-16 13:52", ReportText.ScanTime("16.11.2026 13:52:10"));
        Assert.Equal("Never", ReportText.ScanTime("Never"));                               // not a time: unchanged
        Assert.Equal("2026-11-16 14:05", ReportText.Time(Now));
        Assert.Matches(@"^UTC[+-]\d\d:\d\d$", ReportText.Zone(Now));
        var m = ReportBuilder.Build(Input(Findings()), new ReportOptions(ReportKind.Full, false));
        var scope = m.Scope.ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("2026-11-16 13:52", scope["Scan time"]);
        Assert.Equal("2026-11-16 14:05", scope["Report created"]);                        // zone not repeated here …
        Assert.StartsWith("UTC", scope["Time zone"]);                                     // … but named once
        Assert.DoesNotContain(m.SummarySentences.Concat(m.KeyFacts!).Append(m.ExceptedNote ?? ""), s => s.Contains("(s)"));
    }

    [Fact]
    public void Render_AllPdfs_NoParenthesisedPlurals_NoDottedDates()
    {
        var input = Input(Findings());
        var texts = new List<string>();
        var dir = Directory.CreateTempSubdirectory("dh-preview-");
        try
        {
            ReportPdfRenderer.Render(ReportBuilder.Build(input, new ReportOptions(ReportKind.Full, false)), Path.Combine(dir.FullName, "r.pdf"), texts.Add);
            new PdfExporter().ExportUserDossier(Dossier(), Path.Combine(dir.FullName, "d.pdf"), texts.Add);
        }
        finally { dir.Delete(recursive: true); }
        Assert.DoesNotContain(texts, t => t.Contains("(s)"));
        Assert.DoesNotContain(texts, t => System.Text.RegularExpressions.Regex.IsMatch(t, @"\b\d\d\.\d\d\.\d{4}\b"));
        Assert.Contains(texts, t => t.StartsWith($"DirHealth {ReportPdfRenderer.ShortVersion(ExportMetadata.AppVersion)} · "));
    }

    private static void AssertPdf(string path)
    {
        Assert.True(File.Exists(path), path);
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 4000, $"{path}: {bytes.Length} bytes");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
        Assert.True(ReportPdfRenderer.PageCount(path) >= 1);
    }

    // ── model content of the redesign (cover, executive summary, appendix) ───
    [Fact]
    public void Trend_KeepsOnlyTheLatestComparableRun_Capped()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var history = Enumerable.Range(0, 5).Select(n => new ScoreEntry(t0.AddDays(n), 40 + n, 4))
            .Concat(Enumerable.Range(0, 15).Select(n => new ScoreEntry(t0.AddDays(10 + n), 60 + n, 5))).ToList();
        var trend = ReportBuilder.Trend(history);
        Assert.Equal(ReportBuilder.TrendPoints, trend.Count);
        Assert.All(trend, p => Assert.InRange(p.Score, 60, 74));        // nothing from the old formula
        Assert.Equal(74, trend[^1].Score);
        Assert.Empty(ReportBuilder.Trend([new ScoreEntry(t0, 50, 5)]));   // one point is no trend
        Assert.Empty(ReportBuilder.Trend([new ScoreEntry(t0, 50, 4), new ScoreEntry(t0.AddDays(1), 70, 5)]));
        Assert.Empty(ReportBuilder.Trend(null));
    }

    [Fact]
    public void Model_HasCoverFactsTrendAndMethodology()
    {
        var input = Input(Findings());
        var full = ReportBuilder.Build(input, new ReportOptions(ReportKind.Full, false));
        Assert.Equal("Security Health Report", full.CoverTitle);
        Assert.Equal("CONFIDENTIAL", full.Classification);
        Assert.Contains("changed nothing", full.Disclaimer);
        Assert.InRange(full.KeyFacts!.Count, 1, 4);
        Assert.Contains(full.KeyFacts, f => f.Contains("1 more is covered by an active exception"));
        Assert.Contains(full.KeyFacts, f => f.StartsWith("Since the last scan: 2 new, 1 resolved"));
        Assert.Equal(10, full.ScoreTrend!.Count);
        Assert.Contains(full.Methodology!, l => l.Contains($"{ScoreModel.PointsPerWeight.ToString(System.Globalization.CultureInfo.InvariantCulture)} ×"));
        Assert.Contains(full.Methodology!, l => l.Contains($"version {ScoreModel.FormulaVersion}"));
        Assert.Equal(["#", "Finding", "Points if fixed", "Severity"], full.FixFirst.Headers);
        Assert.All(full.FixFirst.Rows, r => Assert.True(Enum.TryParse<FindingSeverity>(r[3], out _), r[3]));

        var summary = ReportBuilder.Build(input, new ReportOptions(ReportKind.Summary, false));
        Assert.Equal("Management Summary", summary.CoverTitle);
        Assert.Empty(summary.Methodology!);

        var scoped = ReportBuilder.Build(input, new ReportOptions(ReportKind.Full, false, "severity = High"));
        Assert.Equal("Findings Report", scoped.CoverTitle);
        Assert.Empty(scoped.ScoreTrend!);                                  // a filter selection has no history
        Assert.DoesNotContain(scoped.KeyFacts!, f => f.StartsWith("Since the last scan"));
    }

    [Fact]
    public void Render_CoverAndSummary_CarryMarkers_RedactedVariantHidesNames()
    {
        var input = Input(Findings());
        var text = new List<string>();
        var dir = Directory.CreateTempSubdirectory("dh-preview-");
        try
        {
            ReportPdfRenderer.Render(ReportBuilder.Build(input, new ReportOptions(ReportKind.Full, true)), Path.Combine(dir.FullName, "r.pdf"), text.Add);
        }
        finally { dir.Delete(recursive: true); }
        Assert.Contains("CONFIDENTIAL", text);
        Assert.Contains("NAMES REDACTED", text);
        Assert.Contains("Executive summary", text);
        Assert.Contains("How the score works", text);
        Assert.Contains(text, t => t.StartsWith("Names redacted"));                 // redaction note on the cover
        var userObjects = input.Findings.SelectMany(f => f.Objects).Where(o => o.Type == AffectedObjectType.User).Select(o => o.Name).ToList();
        Assert.NotEmpty(userObjects);
        Assert.DoesNotContain(text, t => userObjects.Any(n => t.Contains(n, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(text, t => t.Contains("it.admin", StringComparison.OrdinalIgnoreCase));   // exception creator
    }

    [Fact]
    public void RenderEveryReportVariant()
    {
        var dir = OutDir();
        var findings = Findings();
        var input = Input(findings);
        var written = new List<string>();

        foreach (var kind in new[] { ReportKind.Summary, ReportKind.Full })
            foreach (var redact in new[] { false, true })
            {
                var path = Path.Combine(dir, $"dashboard-{kind.ToString().ToLowerInvariant()}{(redact ? "-redacted" : "")}.pdf");
                ReportPdfRenderer.Render(ReportBuilder.Build(input, new ReportOptions(kind, redact)), path);
                written.Add(path);
            }

        // findings-tab PDF (S21-4): a filtered selection through the same engine
        var selection = findings.Where(f => f.Severity == FindingSeverity.High).ToList();
        var scoped = input with { Findings = selection, Diff = null, ScoreDelta = null, InactiveUsers = [], ExpiringPasswords = [], DomainAdmins = [] };
        var fp = Path.Combine(dir, "findings-tab.pdf");
        ReportPdfRenderer.Render(ReportBuilder.Build(scoped, new ReportOptions(ReportKind.Full, false,
            $"severity = High · {selection.Count} of {findings.Count} findings")), fp);
        written.Add(fp);

        // score not available: every check failed (neutral page 1)
        var na = input with
        {
            Checks = input.Checks.Select(c => c with { Status = CheckStatus.Failed, Reason = "Access denied (insufficient permissions)" }).ToList(),
        };
        na = na with { Score = ScoreModel.Compute(FindingCatalog.All, findings, na.Checks, new ScanTotals(1842, 312, 655)) };
        var np = Path.Combine(dir, "dashboard-summary-score-na.pdf");
        ReportPdfRenderer.Render(ReportBuilder.Build(na, new ReportOptions(ReportKind.Summary, false)), np);
        written.Add(np);

        var pw = Path.Combine(dir, "password-report.pdf");
        new PdfExporter().ExportPasswordReport(PasswordUsers(), pw);
        written.Add(pw);

        var ds = Path.Combine(dir, "user-dossier.pdf");
        new PdfExporter().ExportUserDossier(Dossier(), ds);
        written.Add(ds);

        foreach (var p in written) AssertPdf(p);
    }
}
