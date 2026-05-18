using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using DirHealth.Desktop.Core.Services;

namespace DirHealth.Desktop.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    public DashboardViewModel       Dashboard       { get; }
    public FindingsViewModel        Findings        { get; }
    public SettingsViewModel        Settings        { get; }
    public AdSearchViewModel        AdSearch        { get; }
    public ComputerBrowserViewModel ComputerBrowser { get; }
    public UserBrowserViewModel     UserBrowser     { get; }
    public OuBrowserViewModel       OuBrowser       { get; }
    public GroupManagerViewModel    GroupManager    { get; }
    public PasswordReportViewModel  PasswordReport  { get; }
    public DomainAdminsViewModel      DomainAdmins      { get; }
    public DcInventoryViewModel       DcInventory       { get; }
    public PrivilegedGroupsViewModel  PrivilegedGroups  { get; }
    public DomainTrustViewModel       DomainTrust       { get; }
    public TimelineViewModel          Timeline          { get; }

    [ObservableProperty] private BaseViewModel _currentView;
    [ObservableProperty] private bool          _showScoreDropAlert;
    [ObservableProperty] private string        _scoreDropMessage = "";
    [ObservableProperty] private bool          _showUpdateBanner;
    [ObservableProperty] private string        _updateVersion = "";
    [ObservableProperty] private bool          _isDownloadingUpdate;
    [ObservableProperty] private int           _downloadProgress;

    public string UpdateButtonText => IsDownloadingUpdate
        ? (DownloadProgress > 0 ? $"Downloading {DownloadProgress}%" : "Downloading…")
        : "Update now";

    partial void OnIsDownloadingUpdateChanged(bool value) => OnPropertyChanged(nameof(UpdateButtonText));
    partial void OnDownloadProgressChanged(int value)    => OnPropertyChanged(nameof(UpdateButtonText));

    private string  _updateDownloadUrl = "";
    private bool    _updateHasDirectDownload;
    private long    _updateFileSize;
    private string? _updateExpectedSha256;

    public string AppVersion      { get; } = UpdateChecker.GetCurrentVersion();
    public int    FindingsCount   => Dashboard.FindingsCount;
    public string CurrentViewName => _currentView?.GetType().Name ?? "";

    partial void OnCurrentViewChanged(BaseViewModel value) => OnPropertyChanged(nameof(CurrentViewName));

    public MainViewModel(
        DashboardViewModel dashboard,
        FindingsViewModel findings,
        SettingsViewModel settings,
        AdSearchViewModel adSearch,
        ComputerBrowserViewModel computerBrowser,
        UserBrowserViewModel userBrowser,
        OuBrowserViewModel ouBrowser,
        GroupManagerViewModel groupManager,
        PasswordReportViewModel passwordReport,
        DomainAdminsViewModel domainAdmins,
        DcInventoryViewModel dcInventory,
        PrivilegedGroupsViewModel privilegedGroups,
        DomainTrustViewModel domainTrust,
        TimelineViewModel timeline)
    {
        Dashboard       = dashboard;
        Findings        = findings;
        Settings        = settings;
        AdSearch        = adSearch;
        ComputerBrowser = computerBrowser;
        UserBrowser     = userBrowser;
        OuBrowser       = ouBrowser;
        GroupManager    = groupManager;
        PasswordReport  = passwordReport;
        DomainAdmins     = domainAdmins;
        DcInventory      = dcInventory;
        PrivilegedGroups = privilegedGroups;
        DomainTrust      = domainTrust;
        Timeline         = timeline;
        _currentView    = dashboard;

        Dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.Findings))
                Findings.LoadFindings(Dashboard.Findings, Dashboard.ComplianceScore);
            if (e.PropertyName == nameof(DashboardViewModel.ComplianceScore))
                Findings.Score = Dashboard.ComplianceScore;
            if (e.PropertyName == nameof(DashboardViewModel.FindingsCount))
                OnPropertyChanged(nameof(FindingsCount));
        };

        if (Dashboard.Findings.Count > 0)
            Findings.LoadFindings(Dashboard.Findings, Dashboard.ComplianceScore);

        Dashboard.NavigateToCategory = category =>
        {
            Findings.FilterText      = "";
            Findings.SelectedFinding = null;
            Findings.LoadFindings(Dashboard.Findings, Dashboard.ComplianceScore);
            CurrentView = Findings;
            var match = Findings.Findings.FirstOrDefault(f => f.Category == category);
            if (match is not null) Findings.SelectedFinding = match;
        };

        Dashboard.NavigateToFinding = finding =>
        {
            Findings.FilterText      = "";
            Findings.SelectedFinding = null;
            Findings.LoadFindings(Dashboard.Findings, Dashboard.ComplianceScore);
            CurrentView              = Findings;
            Findings.SelectedFinding = finding;
        };

        Dashboard.OnScoreDrop = (previous, current) =>
        {
            ScoreDropMessage   = $"Score dropped from {previous} to {current} since last scan.";
            ShowScoreDropAlert = true;
        };

        Settings.OnCredentialsSaved = () =>
        {
            UserBrowser.InvalidateCache();
            ComputerBrowser.InvalidateCache();
            GroupManager.InvalidateCache();
        };
    }

    [RelayCommand] public void ShowDashboard()  => CurrentView = Dashboard;
    [RelayCommand] public void ShowFindings()   => CurrentView = Findings;
    [RelayCommand] public void ShowAdSearch()   => CurrentView = AdSearch;

    [RelayCommand]
    public void ShowSettings()
    {
        Settings.SelectedSection = "Connection";
        CurrentView = Settings;
    }

    [RelayCommand]
    public async Task ShowComputerBrowserAsync()
    {
        CurrentView = ComputerBrowser;
        await ComputerBrowser.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowUserBrowserAsync()
    {
        CurrentView = UserBrowser;
        await UserBrowser.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowOuBrowserAsync()
    {
        CurrentView = OuBrowser;
        await OuBrowser.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowGroupManagerAsync()
    {
        CurrentView = GroupManager;
        await GroupManager.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowPasswordReportAsync()
    {
        CurrentView = PasswordReport;
        await PasswordReport.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowDomainAdminsAsync()
    {
        CurrentView = DomainAdmins;
        await DomainAdmins.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowDcInventoryAsync()
    {
        CurrentView = DcInventory;
        await DcInventory.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowPrivilegedGroupsAsync()
    {
        CurrentView = PrivilegedGroups;
        await PrivilegedGroups.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowDomainTrustAsync()
    {
        CurrentView = DomainTrust;
        await DomainTrust.LoadAsync();
    }

    [RelayCommand]
    public async Task ShowTimelineAsync()
    {
        CurrentView = Timeline;
        await Timeline.LoadAsync();
    }

    [RelayCommand]
    public void DismissScoreAlert() => ShowScoreDropAlert = false;

    [RelayCommand]
    public void DismissUpdateBanner() => ShowUpdateBanner = false;

    [RelayCommand]
    public async Task DownloadAndInstallUpdateAsync()
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;
        if (!_updateHasDirectDownload)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_updateDownloadUrl) { UseShellExecute = true });
            return;
        }
        IsDownloadingUpdate = true;
        DownloadProgress    = 0;
        try
        {
            if (!Uri.TryCreate(_updateDownloadUrl, UriKind.Absolute, out var uri) ||
                (uri.Host != "github.com" && uri.Host != "objects.githubusercontent.com") ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                ScoreDropMessage    = "Update failed: unexpected download host.";
                ShowScoreDropAlert  = true;
                IsDownloadingUpdate = false;
                return;
            }
            var tmp = Path.Combine(Path.GetTempPath(), $"DirHealth-Setup-{Guid.NewGuid():N}.exe");
            using var http = new System.Net.Http.HttpClient();
            using var response = await http.GetAsync(_updateDownloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = _updateFileSize > 0 ? _updateFileSize
                      : response.Content.Headers.ContentLength ?? -1;
            using (var src  = await response.Content.ReadAsStreamAsync())
            using (var dest = File.Create(tmp))
            {
                var buf = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await src.ReadAsync(buf)) > 0)
                {
                    await dest.WriteAsync(buf.AsMemory(0, read));
                    downloaded += read;
                    if (total > 0)
                        DownloadProgress = (int)(downloaded * 100 / total);
                }
            } // file handle closed here before verifying and starting installer
            if (string.IsNullOrEmpty(_updateExpectedSha256))
            {
                try { File.Delete(tmp); } catch { }
                ScoreDropMessage    = "Update aborted: no integrity checksum in release. Download manually from GitHub Releases.";
                ShowScoreDropAlert  = true;
                IsDownloadingUpdate = false;
                return;
            }
            using var fs = File.OpenRead(tmp);
            var actualHash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            if (actualHash != _updateExpectedSha256)
            {
                try { File.Delete(tmp); } catch { }
                ScoreDropMessage    = "Update aborted: integrity check failed. Download manually from GitHub Releases.";
                ShowScoreDropAlert  = true;
                IsDownloadingUpdate = false;
                return;
            }
            System.Diagnostics.Process.Start(tmp, "/SILENT /CLOSEAPPLICATIONS");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ScoreDropMessage    = $"Update failed: {ex.Message}";
            ShowScoreDropAlert  = true;
            IsDownloadingUpdate = false;
        }
    }

    public void SetUpdateAvailable(UpdateInfo info)
    {
        _updateDownloadUrl       = info.DownloadUrl;
        _updateHasDirectDownload = info.HasDirectDownload;
        _updateFileSize          = info.FileSize;
        _updateExpectedSha256    = info.ExpectedSha256;
        UpdateVersion            = info.Version;
        ShowUpdateBanner         = true;
    }
}
