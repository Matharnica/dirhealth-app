using System.Text.Json;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

// Sprint 17: per-object exceptions (ADR 0001), score model v2 (ADR 0002).
public class Sprint17Tests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dh-s17-");
    public void Dispose() => _dir.Delete(recursive: true);
    private string P(string name) => Path.Combine(_dir.FullName, name);

    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static FindingRule Rule(string c) => FindingCatalog.Get(c)!;

    private static AffectedObject User(string name, string sid = "") =>
        new() { Name = name, Sid = sid, DistinguishedName = $"CN={name},DC=x", Type = AffectedObjectType.User };

    private static AdFinding Finding(string category, IEnumerable<AffectedObject> objects, FindingSeverity sev = FindingSeverity.High)
    {
        var list = objects.ToList();
        return new AdFinding { Category = category, Count = list.Count, Severity = sev, Objects = list };
    }

    private static IEnumerable<AffectedObject> Users(int n) =>
        Enumerable.Range(0, n).Select(i => User($"u{i}", $"S-1-5-21-1-2-3-{2000 + i}"));

    // ── ADR 0001: validation (negative tests from the gate) ─────────────────
    [Theory]
    [InlineData("",        30, "A reason is required.")]
    [InlineData("   ",     30, "A reason is required.")]
    [InlineData("ok",       0, "The expiry date must be in the future.")]
    [InlineData("ok",     -1, "The expiry date must be in the future.")]
    [InlineData("ok",    366, "An exception can last at most 365 days.")]
    public void Validate_RejectsBadInput(string reason, int days, string expected)
    {
        Assert.Equal(expected, ExceptionStore.Validate(reason, Now.AddDays(days), Now));
    }

    [Fact]
    public void Validate_AcceptsUpTo365Days()
    {
        Assert.Null(ExceptionStore.Validate("service account", Now.AddDays(365), Now));
        Assert.Null(ExceptionStore.Validate("x", Now.AddSeconds(1), Now));
    }

    [Fact]
    public void Add_InvalidThrows_AndWritesNothing()
    {
        var store = new ExceptionStore(P("exceptions.json"));
        Assert.Throws<ArgumentException>(() => store.Add("DH-KRB-001", "S-1", "svc", " ", Now.AddDays(30), "me", Now));
        Assert.False(File.Exists(P("exceptions.json")));
    }

    [Fact]
    public void Store_RoundTrip_AndRemove()
    {
        var s1 = new ExceptionStore(P("exceptions.json"));
        s1.Add("DH-KRB-001", "S-1-5-21-1-2-3-1000", "svc", "rotated quarterly", Now.AddDays(90), "CORP\\admin", Now);
        var s2 = new ExceptionStore(P("exceptions.json"));
        var e  = Assert.Single(s2.All);
        Assert.Equal("CORP\\admin", e.CreatedBy);
        Assert.Equal(DateTimeKind.Utc, e.ExpiresUtc.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : e.ExpiresUtc.Kind);
        Assert.True(s2.Remove("DH-KRB-001", "s-1-5-21-1-2-3-1000"));   // key match is case-insensitive
        Assert.Empty(new ExceptionStore(P("exceptions.json")).All);
    }

    [Fact]
    public void CorruptFile_IsCopiedAside_AppStartsEmpty_ErrorVisible()
    {
        File.WriteAllText(P("exceptions.json"), "{ not json");
        var store = new ExceptionStore(P("exceptions.json"));
        Assert.Empty(store.All);
        Assert.NotNull(store.LoadError);
        Assert.Single(_dir.GetFiles("exceptions.corrupt-*.json"));
        Assert.Equal("{ not json", File.ReadAllText(_dir.GetFiles("exceptions.corrupt-*.json")[0].FullName));
    }

    [Fact]
    public void NewerFileVersion_IsNotOverwrittenSilently()
    {
        File.WriteAllText(P("exceptions.json"), """{ "Version": 99, "Exceptions": [] }""");
        var store = new ExceptionStore(P("exceptions.json"));
        Assert.Contains("newer", store.LoadError);
        Assert.Single(_dir.GetFiles("exceptions.corrupt-*.json"));
    }

    [Fact]
    public void LegacyAcknowledgements_AreReadOnly_AndNeverRewritten()
    {
        // v2.9 format — after S17 it is only read for the hint, never written (downgrade-safe)
        const string legacy = """{ "InactiveUsers": { "Note": "seasonal staff", "AcknowledgedAt": "2026-01-01T00:00:00Z" } }""";
        File.WriteAllText(P("acknowledged.json"), legacy);
        var ack = new AcknowledgeStore(P("acknowledged.json"));
        Assert.Equal("seasonal staff", ack.Legacy("InactiveUsers")!.Note);
        Assert.Null(ack.Legacy("AsRepRoasting"));

        var store = new ExceptionStore(P("exceptions.json"));
        store.Add("DH-USR-001", "S-1-5-21-1-2-3-1", "u", "r", Now.AddDays(5), "me", Now);
        Assert.Equal(legacy, File.ReadAllText(P("acknowledged.json")));
    }

    [Fact]
    public void Key_ObjectRulesNeedSid_PolicyRulesUseName()
    {
        Assert.Null(ExceptionKey.For(Rule("AsRepRoasting"), User("old", sid: "")));      // pre-v2.10 scan
        Assert.Equal("S-1-5-21-1-2-3-9", ExceptionKey.For(Rule("AsRepRoasting"), User("u", "s-1-5-21-1-2-3-9")));
        var pso = new AffectedObject { Name = "PSO-Admins", Type = AffectedObjectType.PasswordPolicy };
        Assert.Equal("name:PSO-Admins", ExceptionKey.For(Rule("FineGrainedPasswordPolicy"), pso));
    }

    [Fact]
    public void Apply_ActiveExpiredAndFullyExcepted()
    {
        var store = new ExceptionStore(P("exceptions.json"));
        var a = User("a", "S-1-5-21-1-2-3-1");
        var b = User("b", "S-1-5-21-1-2-3-2");
        var f = Finding("AsRepRoasting", [a, b]);
        store.Add("DH-KRB-002", a.Sid, "a", "r", Now.AddDays(10), "me", Now);

        store.Apply([f], Now);
        Assert.Equal(ExceptionState.Active, a.Exception);
        Assert.Equal(ExceptionState.None, b.Exception);
        Assert.False(f.IsAcknowledged);

        store.Add("DH-KRB-002", b.Sid, "b", "r", Now.AddDays(10), "me", Now);
        store.Apply([f], Now);
        Assert.True(f.IsAcknowledged);                       // all objects excepted

        store.Apply([f], Now.AddDays(11));                   // later: both expired
        Assert.Equal(ExceptionState.Expired, a.Exception);
        Assert.Contains("expired", a.ExceptionLabel);
        Assert.False(f.IsAcknowledged);
    }

    // ── ADR 0002: score model ────────────────────────────────────────────────
    private static readonly ScanTotals Totals = new(Users: 100, Groups: 100, Computers: 100);

    private static ScoreResult Score(IReadOnlyList<AdFinding> findings, IReadOnlyList<CheckResult>? checks = null, ScanTotals? totals = null) =>
        ScoreModel.Compute(FindingCatalog.All, findings, checks ?? [], totals ?? Totals);

    [Fact]
    public void CleanDomain_Scores100()
    {
        var r = Score([]);
        Assert.Equal(100, r.Score);
        Assert.Equal("100", r.Display);
        Assert.All(r.Rules, x => Assert.Equal(0, x.PointsIfFixed));
    }

    [Fact]
    public void AdrScenario_S1_OnlyExploitable_Is33()
    {
        var r = Score(
        [
            Finding("UnconstrainedDelegationComputers", [new AffectedObject { Name = "PC1", Sid = "S-1-5-21-1-2-3-50", Type = AffectedObjectType.Computer }]),
            Finding("AsRepRoasting", Users(5)),
            Finding("KerberoastableAccounts", Users(3)),
        ]);
        Assert.Equal(33, r.Score);   // 100 − 2.5 × 27 = 32.5 → AwayFromZero
        Assert.True(r.Score < 60, "gate condition: exploitable-only must be clearly below 60");
    }

    [Fact]
    public void AdrScenario_S2_OneDelegatedComputer_Is75()
    {
        var r = Score([Finding("UnconstrainedDelegationComputers", [new AffectedObject { Name = "PC1", Type = AffectedObjectType.Computer }])]);
        Assert.Equal(75, r.Score);
        Assert.Equal(25, r.PointsFor("UnconstrainedDelegationComputers"));
    }

    [Fact]
    public void AdrScenario_S3_HeavyHygiene_Is63_AndAboveExploitable()
    {
        var r = Score(
        [
            Finding("InactiveUsers", Users(30)), Finding("PasswordNeverExpires", Users(50)),
            Finding("ExpiredPasswords", Users(30)), Finding("InactiveComputers", Users(40)),
            Finding("EmptyGroups", Users(35)), Finding("SingleMemberGroups", Users(35)),
        ]);
        Assert.Equal(63, r.Score);   // 100 − 37.5 = 62.5 → AwayFromZero
    }

    [Fact]
    public void AdrScenario_S4_TypicalSmb_Is58()
    {
        var r = Score(
        [
            Finding("InactiveUsers", Users(10)), Finding("PasswordNeverExpires", Users(20)),
            Finding("KerberoastableAccounts", Users(2)),
            Finding("WeakPasswordLength", [new AffectedObject { Name = "minPwdLength = 8", Type = AffectedObjectType.DomainSetting }], FindingSeverity.Medium),
            Finding("NoAccountLockout", [new AffectedObject { Name = "lockoutThreshold = 0", Type = AffectedObjectType.DomainSetting }], FindingSeverity.High),
        ]);
        Assert.Equal(58, r.Score);
    }

    [Fact]
    public void PointsIfFixed_SumTo100MinusScore()
    {
        var r = Score(
        [
            Finding("InactiveUsers", Users(7)), Finding("KerberoastableAccounts", Users(1)),
            Finding("AsRepRoasting", Users(2)), Finding("SidHistory", Users(3)),
        ]);
        Assert.Equal(100 - r.Score, r.Rules.Sum(x => x.PointsIfFixed));
    }

    [Fact]
    public void ScoreClampedAtZero_PointsStillSumTo100()
    {
        var all = FindingCatalog.All.Select(rule => Finding(rule.Category, Users(60))).ToList();
        var r = Score(all);
        Assert.Equal(0, r.Score);
        Assert.Equal(100, r.Rules.Sum(x => x.PointsIfFixed));
    }

    [Fact]
    public void FailedCheck_CannotRaiseScore_AndMarksUpperBound()
    {
        var findings = new List<AdFinding> { Finding("InactiveUsers", Users(30)) };
        var baseline = Score(findings);
        var failed   = Score(findings, [new CheckResult("AsRepRoasting", "DH-KRB-002", CheckStatus.Failed, "timeout", 0)]);
        Assert.Equal(baseline.Score, failed.Score);          // no reward for a check that did not run
        Assert.True(failed.IsUpperBound);
        Assert.StartsWith("≤ ", failed.Display);
        Assert.Equal(FindingCatalog.All.Count - 1, failed.EvaluatedRules);
    }

    [Fact]
    public void TooManyFailedChecks_ScoreNotAvailable()
    {
        // fail rules until more than half of the catalog weight is not evaluated
        double half = FindingCatalog.All.Sum(r => r.Weight) / 2.0, acc = 0;
        var checks = FindingCatalog.All.OrderByDescending(r => r.Weight).TakeWhile(r => (acc += r.Weight) - r.Weight <= half)
            .Select(r => new CheckResult(r.Category, r.RuleId, CheckStatus.Failed, "x", 0)).ToList();
        var r = Score([], checks);
        Assert.False(r.Available);
        Assert.Equal("n/a", r.Display);
    }

    [Fact]
    public void ActiveException_RemovesObjectFromCount_ExpiredCountsAgain()
    {
        var obj = new AffectedObject { Name = "PC1", Sid = "S-1-5-21-1-2-3-50", Type = AffectedObjectType.Computer };
        var f   = Finding("UnconstrainedDelegationComputers", [obj]);
        obj.Exception = ExceptionState.Active;
        Assert.Equal(100, Score([f]).Score);
        obj.Exception = ExceptionState.Expired;
        Assert.Equal(75, Score([f]).Score);
    }

    [Fact]
    public void ShareRule_ExceptionsKeepPopulation()
    {
        var users = Users(30).ToList();
        foreach (var u in users.Take(15)) u.Exception = ExceptionState.Active;
        var p = ScoreModel.PresenceOf(Rule("InactiveUsers"), [Finding("InactiveUsers", users)], Totals);
        Assert.Equal(0.5, p, 3);    // 15 open of 100 users, saturation 30 %
    }

    [Fact]
    public void Informational_AdminCount_DeductsNothing()
    {
        Assert.Equal(100, Score([Finding("AdminSdHolderAccounts", Users(10))]).Score);
    }

    [Fact]
    public void Eol_DomainControllerCountsThreeTimes()
    {
        var dc = new AffectedObject { Name = "DC1", IsDomainController = true, Type = AffectedObjectType.Computer };
        Assert.Equal(0.6, ScoreModel.PresenceOf(Rule("EolOperatingSystems"), [Finding("EolOperatingSystems", [dc])], Totals), 3);
    }

    [Fact]
    public void DistributePoints_IsDeterministicOnTies()
    {
        var items = new List<(FindingRule, double)> { (Rule("EmptyGroups"), 0.5), (Rule("SingleMemberGroups"), 0.5) };
        var a = ScoreModel.DistributePoints(items, 1);
        var b = ScoreModel.DistributePoints(items.AsEnumerable().Reverse().ToList(), 1);
        Assert.Equal(a, b);
        Assert.Equal(1, a.Values.Sum());
    }

    // ── formula version: no false "score dropped", no cross-version delta ───
    [Fact]
    public void History_OldEntriesAreVersion1_NotComparableToV2()
    {
        File.WriteAllText(P("history.json"), """[ { "Timestamp": "2026-10-01T00:00:00Z", "Score": 88 } ]""");
        var h = new ScoreHistoryStore(P("history.json"));
        Assert.Equal(1, h.Entries[0].FormulaVersion);
        Assert.Null(h.PreviousComparableScore(ScoreModel.FormulaVersion));   // → no drop notice after update
        h.Add(60, ScoreModel.FormulaVersion);
        Assert.False(ScoreHistoryStore.Comparable(h.Entries));
        Assert.Equal(60, h.PreviousComparableScore(ScoreModel.FormulaVersion));
    }

    [Fact]
    public void ScanDiff_AcrossFormulaChange_HasNoDelta()
    {
        var oldCache = new ScanCache { ComplianceScore = 90 };                         // FormulaVersion 1
        var newCache = new ScanCache { ComplianceScore = 60, FormulaVersion = ScoreModel.FormulaVersion };
        var d = ScanDiffCalculator.Calculate(oldCache, newCache);
        Assert.False(d.ScoreComparable);
        Assert.Equal(0, d.ScoreDelta);
    }

    [Fact]
    public void OldScanCache_LoadsWithVersion1_AndNoTotals()
    {
        var c = JsonSerializer.Deserialize<ScanCache>("""{ "Findings": [], "ComplianceScore": 77 }""")!;
        Assert.Equal(1, c.FormulaVersion);
        Assert.Null(c.Totals);
    }

    // ── S17-2 view model ─────────────────────────────────────────────────────
    private FindingsViewModel Vm(out ExceptionStore store)
    {
        store = new ExceptionStore(P("exceptions.json"));
        return new FindingsViewModel(store, new AcknowledgeStore(P("acknowledged.json")));
    }

    [Fact]
    public void Vm_AddException_GreysObject_RaisesChanged()
    {
        var vm  = Vm(out var store);
        var obj = User("svc", "S-1-5-21-1-2-3-77");
        vm.LoadFindings([Finding("AsRepRoasting", [obj])]);
        vm.SelectedFinding = vm.Findings[0];
        vm.ObjectsTable.SelectedItem = obj;
        bool changed = false;
        vm.ExceptionsChanged += () => changed = true;

        vm.ExceptionReason = "legacy Java client, ticket 4711";
        vm.AddExceptionCommand.Execute(null);

        Assert.True(changed);
        Assert.True(obj.IsExcepted);
        Assert.Single(store.All);
        Assert.Equal(0, vm.OpenFindingsCount);               // the only object is excepted
        Assert.Equal("", vm.ExceptionReason);
    }

    [Fact]
    public void Vm_AddException_WithoutReason_IsRefused()
    {
        var vm  = Vm(out var store);
        var obj = User("svc", "S-1-5-21-1-2-3-77");
        vm.LoadFindings([Finding("AsRepRoasting", [obj])]);
        vm.SelectedFinding = vm.Findings[0];
        vm.ObjectsTable.SelectedItem = obj;
        vm.AddExceptionCommand.Execute(null);
        Assert.Equal("A reason is required.", vm.StatusMessage);
        Assert.Empty(store.All);
    }

    [Fact]
    public void Vm_ObjectWithoutSid_ExplainsWhy()
    {
        var vm  = Vm(out var store);
        var obj = User("oldscan");                            // no SID: cache from v2.9
        vm.LoadFindings([Finding("AsRepRoasting", [obj])]);
        vm.SelectedFinding = vm.Findings[0];
        vm.ObjectsTable.SelectedItem = obj;
        vm.ExceptionReason = "x";
        vm.AddExceptionCommand.Execute(null);
        Assert.Contains("no SID", vm.StatusMessage);
        Assert.Empty(store.All);
    }

    [Fact]
    public void Vm_LegacyNote_IsShownButHidesNothing()
    {
        File.WriteAllText(P("acknowledged.json"),
            """{ "AsRepRoasting": { "Note": "old app", "AcknowledgedAt": "2026-01-01T00:00:00Z" } }""");
        var vm = Vm(out _);
        vm.LoadFindings([Finding("AsRepRoasting", [User("a", "S-1-5-21-1-2-3-1")])]);
        Assert.Equal(1, vm.OpenFindingsCount);               // no longer hidden (ADR 0001)
        vm.SelectedFinding = vm.Findings[0];
        Assert.Contains("old app", vm.LegacyNote);
        Assert.Contains("no longer hide", vm.LegacyNote);
    }
}
