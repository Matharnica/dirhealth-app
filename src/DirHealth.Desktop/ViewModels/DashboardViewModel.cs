using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;
    private readonly PdfExporter _pdfExporter = new();
    private readonly ScoreHistoryStore _historyStore = new();
    private readonly ScanCacheStore _cacheStore = new();

    private List<AdUser> _cachedInactiveUsers     = [];
    private List<AdUser> _cachedExpiringPasswords = [];
    private List<string> _cachedDomainAdmins      = [];
    private bool         _liveScanCompleted;

    [ObservableProperty] private int _complianceScore;
    [ObservableProperty] private int _findingsCount;
    [ObservableProperty] private int _inactiveUsersCount;
    [ObservableProperty] private int _passwordIssuesCount;
    [ObservableProperty] private int _groupIssuesCount;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportFullReportCommand))]
    private string _lastScanTime = "Never";
    [ObservableProperty] private List<AdFinding> _findings = [];
    [ObservableProperty] private ScanDiff? _lastDiff;
    [ObservableProperty] private bool _hasDiff;

    public Action<string>?    NavigateToCategory { get; set; }
    public Action<AdFinding>? NavigateToFinding  { get; set; }
    public IReadOnlyList<ScoreEntry> ScoreHistory => _historyStore.Entries;
    public Action<int, int>? OnScoreDrop { get; set; }

    public DashboardViewModel(AdScanner scanner)
    {
        _scanner = scanner;
        LoadCachedResults();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
                ExportFullReportCommand.NotifyCanExecuteChanged();
        };
    }

    private void LoadCachedResults()
    {
        var cache = _cacheStore.Load();
        if (cache is null) return;

        Findings            = cache.Findings;
        ComplianceScore     = cache.ComplianceScore;
        FindingsCount       = cache.FindingsCount;
        InactiveUsersCount  = cache.InactiveUsersCount;
        PasswordIssuesCount = cache.PasswordIssuesCount;
        GroupIssuesCount    = cache.GroupIssuesCount;
        LastScanTime        = cache.LastScanTime;
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
            var previousCache   = _cacheStore.Load();

            Findings            = await _scanner.RunFullScanAsync();
            ComplianceScore     = await _scanner.ComputeComplianceScoreAsync();
            FindingsCount       = Findings.Count;
            InactiveUsersCount  = Findings.FirstOrDefault(f => f.Category == "InactiveUsers")?.Count ?? 0;
            PasswordIssuesCount = Findings.Where(f => f.Category is "PasswordNeverExpires" or "ExpiredPasswords").Sum(f => f.Count);
            GroupIssuesCount    = Findings.Where(f => f.Category is "EmptyGroups" or "SingleMemberGroups").Sum(f => f.Count);
            LastScanTime        = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            StatusMessage       = $"Scan complete — {FindingsCount} finding(s)";

            var newCache = new ScanCache
            {
                Findings            = Findings,
                ComplianceScore     = ComplianceScore,
                FindingsCount       = FindingsCount,
                InactiveUsersCount  = InactiveUsersCount,
                PasswordIssuesCount = PasswordIssuesCount,
                GroupIssuesCount    = GroupIssuesCount,
                LastScanTime        = LastScanTime
            };

            // Calculate diff before saving (Save() moves current → previous)
            if (previousCache != null)
            {
                LastDiff = ScanDiffCalculator.Calculate(previousCache, newCache);
                HasDiff  = LastDiff.HasChanges;
            }

            _cacheStore.Save(newCache);

            _cachedInactiveUsers     = await _scanner.GetInactiveUsersAsync(90);
            _cachedExpiringPasswords = await _scanner.GetExpiringPasswordUsersAsync(30);
            var adminsGroup          = await _scanner.GetDomainAdminsAsync();
            _cachedDomainAdmins      = adminsGroup?.Members?.Select(m => m.Name).ToList() ?? [];
            _liveScanCompleted       = true;

            var previous = _historyStore.PreviousScore();
            _historyStore.Add(ComplianceScore);
            OnPropertyChanged(nameof(ScoreHistory));
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
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"DirHealth-FullReport-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".pdf",
            Filter     = "PDF files|*.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        StatusMessage = "Building full report…";
        try
        {
            var data = new FullReportData(
                Domain:            _scanner.DomainName,
                Score:             ComplianceScore,
                Findings:          Findings,
                InactiveUsers:     _cachedInactiveUsers,
                ExpiringPasswords: _cachedExpiringPasswords,
                DomainAdmins:      _cachedDomainAdmins
            );

            _pdfExporter.ExportFullReport(data, dlg.FileName);
            StatusMessage = $"Full report saved: {System.IO.Path.GetFileName(dlg.FileName)}";
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

    private bool HasScanData() => _liveScanCompleted && !IsBusy;

}
