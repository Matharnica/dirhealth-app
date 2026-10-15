using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Export.Reports;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;
    private readonly ScoreHistoryStore _historyStore;
    private readonly ScanCacheStore _cacheStore = new();
    private readonly ExceptionStore _exceptions;
    private ScanTotals? _totals;
    private int _cachedFormulaVersion = ScoreModel.FormulaVersion;

    private List<AdUser> _cachedInactiveUsers     = [];
    private List<AdUser> _cachedExpiringPasswords = [];
    private List<string> _cachedDomainAdmins      = [];
    private bool         _liveScanCompleted;

    [ObservableProperty] private int    _complianceScore;
    [ObservableProperty] private int    _findingsCount;
    [ObservableProperty] private int    _inactiveUsersCount;
    [ObservableProperty] private int    _passwordIssuesCount;
    [ObservableProperty] private int    _groupIssuesCount;
    [ObservableProperty] private string _connectedDomain = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportFullReportCommand))]
    [NotifyPropertyChangedFor(nameof(IsFirstRun))]
    private string _lastScanTime = "Never";
    [ObservableProperty] private List<AdFinding> _findings = [];
    [ObservableProperty] private ScanDiff? _lastDiff;
    [ObservableProperty] private bool _hasDiff;

    /// <summary>Status of every check in the last scan (S16-4).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckSummary), nameof(FailedChecks), nameof(HasFailedChecks), nameof(FailedChecksNote))]
    private List<CheckResult> _checks = [];

    public List<CheckResult> FailedChecks => Checks.Where(c => c.Status == CheckStatus.Failed).ToList();
    public bool HasFailedChecks => FailedChecks.Count > 0;

    /// <summary>"16 passed · 3 found · 1 failed · 0 not applicable" — empty before the first S16 scan.</summary>
    public string CheckSummary => Checks.Count == 0 ? "" : Summarize(Checks);

    /// <summary>Honest note while the score model (S17) cannot yet exclude failed checks.</summary>
    public string FailedChecksNote => HasFailedChecks
        ? $"{FailedChecks.Count} check(s) could not run — they found nothing because they did not look. The score may be too high until they run."
        : "";

    public static string Summarize(IEnumerable<CheckResult> checks)
    {
        var list = checks.ToList();
        int N(CheckStatus s) => list.Count(c => c.Status == s);
        return $"{N(CheckStatus.Passed)} passed · {N(CheckStatus.Found)} found · {N(CheckStatus.Failed)} failed · {N(CheckStatus.NotApplicable)} not applicable";
    }

    public Action<string>?    NavigateToCategory { get; set; }
    public Action<AdFinding>? NavigateToFinding  { get; set; }
    public IReadOnlyList<ScoreEntry> ScoreHistory => _historyStore.Entries;
    public Action<int, int>? OnScoreDrop { get; set; }

    public bool IsFirstRun => LastScanTime == "Never";

    public string ScoreTrendText
    {
        get
        {
            if (ScoreHistory.Count < 2) return "";
            if (!ScoreHistoryStore.Comparable(ScoreHistory)) return "new score model";
            var delta = ScoreHistory[^1].Score - ScoreHistory[^2].Score;
            return delta > 0 ? $"▲ +{delta}" : delta < 0 ? $"▼ {delta}" : "= no change";
        }
    }

    public bool ScoreTrendIsPositive => ScoreHistoryStore.Comparable(ScoreHistory) && ScoreHistory[^1].Score > ScoreHistory[^2].Score;
    public bool ScoreTrendIsNegative => ScoreHistoryStore.Comparable(ScoreHistory) && ScoreHistory[^1].Score < ScoreHistory[^2].Score;

    // ── Score v2 (ADR 0002), recomputed from raw scan data + exceptions ────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreDisplay), nameof(ScoreCoverageText), nameof(ScoreBreakdown), nameof(ScoreNote))]
    private ScoreResult? _scoreResult;

    /// <summary>"74", "≤ 74" (some checks failed) or "n/a"; old caches show their stored number.</summary>
    public string ScoreDisplay => ScoreResult?.Display ?? ComplianceScore.ToString();
    public string ScoreCoverageText => ScoreResult?.CoverageText ?? "";
    /// <summary>Per-rule "+X if fixed", largest first (S17-4 dashboard breakdown).</summary>
    public List<RuleScore> ScoreBreakdown => ScoreResult?.Rules.Where(r => r.PointsIfFixed > 0).ToList() ?? [];
    /// <summary>
    /// One-time release notice (S19): shown after the first scan with a new score formula — i.e. while
    /// the last two history entries use different formulas — and gone after the next scan.
    /// </summary>
    public string ScoreModelNotice => ScoreHistory.Count >= 2 && !ScoreHistoryStore.Comparable(ScoreHistory)
        ? "This version checks more and scores differently (new checks, exploitable issues weigh more). " +
          "Your score changed without any change in your Active Directory — compare from the next scan on."
        : "";

    public string ScoreNote => ScoreResult is null && LastScanTime != "Never"
        ? "Score from an older DirHealth version — run a scan to use the new score model."
        : ScoreResult is { Available: false } ? "Too many checks failed to compute a score."
        : ScoreResult is { IsUpperBound: true } ? "Upper bound: failed checks could only lower it."
        : "";

    /// <summary>Recomputes the displayed score after a scan or when exceptions change (ADR 0001/0002).</summary>
    public void RecomputeScore()
    {
        _exceptions.Apply(Findings, DateTime.UtcNow);
        if (_totals is null || _cachedFormulaVersion != ScoreModel.FormulaVersion)
        {
            ScoreResult = null;   // old cache: keep its stored number, say so
            return;
        }
        ScoreResult     = ScoreModel.Compute(FindingCatalog.All, Findings, Checks, _totals);
        ComplianceScore = ScoreResult.Score;
    }

    public DashboardViewModel(AdScanner scanner, ExceptionStore? exceptions = null, ScoreHistoryStore? history = null)
    {
        _scanner      = scanner;
        _exceptions   = exceptions ?? new ExceptionStore();
        _historyStore = history ?? new ScoreHistoryStore();
        LoadCachedResults();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
                ExportFullReportCommand.NotifyCanExecuteChanged();
        };

        var refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        refreshTimer.Tick += (_, _) =>
        {
            if (LastScanTime != "Never")
                OnPropertyChanged(nameof(LastScanTime));
        };
        refreshTimer.Start();
    }

    private void LoadCachedResults()
    {
        ConnectedDomain = _scanner.DomainName;
        var cache = _cacheStore.Load();
        if (cache is null) return;

        Findings            = cache.Findings;
        ComplianceScore     = cache.ComplianceScore;
        FindingsCount       = cache.FindingsCount;
        InactiveUsersCount  = cache.InactiveUsersCount;
        PasswordIssuesCount = cache.PasswordIssuesCount;
        GroupIssuesCount    = cache.GroupIssuesCount;
        LastScanTime        = cache.LastScanTime;
        Checks              = cache.Checks ?? [];
        _totals               = cache.Totals;
        _cachedFormulaVersion = cache.FormulaVersion;
        RecomputeScore();
        StatusMessage       = $"Last scan: {cache.LastScanTime} — {cache.FindingsCount} finding(s)";
    }

    [RelayCommand]
    public void OpenCategory(string category) => NavigateToCategory?.Invoke(category);

    [RelayCommand]
    public void OpenFinding(AdFinding finding) => NavigateToFinding?.Invoke(finding);

    [RelayCommand]
    public async Task RunScanAsync()
    {
        IsBusy = true;
        StatusMessage = "Scanning Active Directory...";
        try
        {
            var previousCache = _cacheStore.Load();

            var result          = await _scanner.RunCompleteScanAsync();
            Findings            = result.Findings;
            ComplianceScore     = result.Score;
            Checks              = result.Checks;
            _totals               = result.Totals;
            _cachedFormulaVersion = ScoreModel.FormulaVersion;
            RecomputeScore();
            FindingsCount       = Findings.Count;
            InactiveUsersCount  = Findings.FirstOrDefault(f => f.Category == "InactiveUsers")?.Count ?? 0;
            PasswordIssuesCount = Findings.Where(f => f.Category is "PasswordNeverExpires" or "ExpiredPasswords").Sum(f => f.Count);
            GroupIssuesCount    = Findings.Where(f => f.Category is "EmptyGroups" or "SingleMemberGroups").Sum(f => f.Count);
            LastScanTime        = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            ConnectedDomain     = _scanner.DomainName;
            StatusMessage       = HasFailedChecks
                ? $"Scan complete — {FindingsCount} finding(s), {FailedChecks.Count} check(s) failed"
                : $"Scan complete — {FindingsCount} finding(s)";

            _cachedInactiveUsers     = result.InactiveUsers;
            _cachedExpiringPasswords = result.ExpiringPasswords;
            _cachedDomainAdmins      = result.DomainAdmins;
            _liveScanCompleted       = true;

            var newCache = new ScanCache
            {
                Findings            = Findings,
                ComplianceScore     = ComplianceScore,
                FindingsCount       = FindingsCount,
                InactiveUsersCount  = InactiveUsersCount,
                PasswordIssuesCount = PasswordIssuesCount,
                GroupIssuesCount    = GroupIssuesCount,
                LastScanTime        = LastScanTime,
                Checks              = Checks,
                Totals              = _totals,
                FormulaVersion      = ScoreModel.FormulaVersion,
            };

            if (previousCache != null)
            {
                LastDiff = ScanDiffCalculator.Calculate(previousCache, newCache);
                HasDiff  = LastDiff.HasChanges;
            }

            _cacheStore.Save(newCache);

            var previous = _historyStore.PreviousComparableScore(ScoreModel.FormulaVersion);
            _historyStore.Add(ComplianceScore, ScoreModel.FormulaVersion);
            OnPropertyChanged(nameof(ScoreHistory));
            OnPropertyChanged(nameof(ScoreTrendText));
            OnPropertyChanged(nameof(ScoreTrendIsPositive));
            OnPropertyChanged(nameof(ScoreTrendIsNegative));
            OnPropertyChanged(nameof(ScoreModelNotice));
            if (previous.HasValue && ComplianceScore < previous.Value)
                OnScoreDrop?.Invoke(previous.Value, ComplianceScore);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasScanData))]
    public async Task ExportFullReportAsync()
    {
        // S18-1/S18-4: Summary or Full, optionally with redacted user names
        var options = AskReportOptions();
        if (options is null) return;
        var path = PickReportPath($"DirHealth-{(options.Kind == ReportKind.Summary ? "Summary" : "FullReport")}-{DateTime.Now:yyyyMMdd}");
        if (path is null) return;

        IsBusy = true;
        StatusMessage = "Building report…";
        try
        {
            var model = BuildReportModel(options, DateTime.Now);
            await Task.Run(() => ReportPdfRenderer.Render(model, path));
            ExportMetadata.WriteFor(path, SafeExportContext(), $"Report ({options.Kind})",
                options.RedactUserNames ? "user names redacted" : "", model.Findings.Count);
            StatusMessage = $"Report saved: {System.IO.Path.GetFileName(path)} (+ .meta.txt)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Export dialog hook; replaced in tests.</summary>
    public static Func<ReportOptions?> AskReportOptions { get; set; } = () =>
    {
        var dlg = new Views.Dashboard.ReportExportDialog { Owner = System.Windows.Application.Current?.MainWindow };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    };

    /// <summary>PDF save dialog hook; replaced in tests.</summary>
    public static Func<string, string?> PickReportPath { get; set; } = name =>
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = name, DefaultExt = ".pdf", Filter = "PDF files|*.pdf" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    };

    private Core.Export.ExportContext SafeExportContext()
    {
        try { return _scanner.GetExportContext(); }
        catch { return new Core.Export.ExportContext(ConnectedDomain, "unknown", Environment.UserName); }
    }

    /// <summary>The report model for the current scan (pure inputs, testable).</summary>
    public ReportModel BuildReportModel(ReportOptions options, DateTime nowLocal)
    {
        int? scoreDelta = ScoreHistoryStore.Comparable(ScoreHistory)
            ? ScoreHistory[^1].Score - ScoreHistory[^2].Score
            : null;   // no delta across a formula change (ADR 0002)
        _exceptions.Apply(Findings, DateTime.UtcNow);
        var input = new ReportInput(
            Domain: string.IsNullOrEmpty(ConnectedDomain) ? _scanner?.DomainName ?? "" : ConnectedDomain,
            Findings: Findings, Score: ScoreResult, StoredScore: ComplianceScore, Checks: Checks,
            Exceptions: _exceptions.All, Diff: LastDiff, ScoreDelta: scoreDelta,
            InactiveUsers: _cachedInactiveUsers, ExpiringPasswords: _cachedExpiringPasswords, DomainAdmins: _cachedDomainAdmins,
            CreatedLocal: nowLocal, Version: ExportMetadata.AppVersion);
        return ReportBuilder.Build(input, options);
    }

    private bool HasScanData() => _liveScanCompleted && !IsBusy;

}
