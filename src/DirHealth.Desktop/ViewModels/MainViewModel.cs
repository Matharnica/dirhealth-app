using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
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
    public DomainAdminsViewModel    DomainAdmins    { get; }
    public DcInventoryViewModel     DcInventory     { get; }

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

    private string _updateDownloadUrl = "";
    private bool   _updateHasDirectDownload;
    private long   _updateFileSize;

    public string AppVersion { get; } = UpdateChecker.GetCurrentVersion();

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
        DcInventoryViewModel dcInventory)
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
        DomainAdmins    = domainAdmins;
        DcInventory     = dcInventory;
        _currentView    = dashboard;

        Dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.Findings))
                Findings.LoadFindings(Dashboard.Findings, Dashboard.ComplianceScore);
            if (e.PropertyName == nameof(DashboardViewModel.ComplianceScore))
                Findings.Score = Dashboard.ComplianceScore;
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
            var tmp = Path.Combine(Path.GetTempPath(), "DirHealth-Setup.exe");
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
            } // file handle closed here before starting installer
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
        UpdateVersion            = info.Version;
        ShowUpdateBanner         = true;
    }
}
