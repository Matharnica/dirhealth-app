using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly AdConnector _connector;
    private readonly string      _credentialPath;

    [ObservableProperty] private string _selectedSection = "Connection";

    [ObservableProperty] private string _domain = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    // Same meaning as the login window's "Remember credentials" (M1): the login writes
    // credentials.dat only when ticked and deletes it otherwise, so the file's existence
    // is the one source of truth for the choice. Save() follows this flag.
    [ObservableProperty] private bool   _rememberCredentials;
    [ObservableProperty] private bool   _connectionTestPassed;
    [ObservableProperty] private string _connectionTestResult = "";

    [ObservableProperty] private bool _autoScanEnabled = false;
    [ObservableProperty] private int  _autoScanIntervalHours = 4;

    [ObservableProperty] private string _connectionInfo = "Not tested";

    [ObservableProperty] private string _updateStatusText = "";
    [ObservableProperty] private bool   _isCheckingForUpdates;
    public string CurrentVersion { get; } = UpdateChecker.GetCurrentVersion();

    public List<int> ScanIntervalOptions { get; } = [1, 2, 4, 8, 12, 24];
    public Action<bool, int>?    OnScheduleChanged    { get; set; }
    public Func<Task<string>>?   OnCheckForUpdates    { get; set; }
    public Action?               OnCredentialsSaved   { get; set; }

    partial void OnAutoScanEnabledChanged(bool value)      => OnScheduleChanged?.Invoke(value, AutoScanIntervalHours);
    partial void OnAutoScanIntervalHoursChanged(int value) => OnScheduleChanged?.Invoke(AutoScanEnabled, value);

    public SettingsViewModel(AdConnector connector) : this(connector, CredentialStore.DefaultPath) { }

    internal SettingsViewModel(AdConnector connector, string credentialPath)
    {
        _connector      = connector;
        _credentialPath = credentialPath;
        RememberCredentials = CredentialStore.Load(credentialPath) is not null;
        Domain     = connector.Domain ?? "";
        Username   = connector.Username ?? "";
        Password   = connector.Password ?? "";
    }

    [RelayCommand]
    void SelectSection(string section) => SelectedSection = section;

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (OnCheckForUpdates is null) return;
        IsCheckingForUpdates = true;
        UpdateStatusText     = "Checking…";
        try
        {
            UpdateStatusText = await OnCheckForUpdates();
        }
        catch (Exception ex) { UpdateStatusText = $"Check failed: {ex.Message}"; }
        finally { IsCheckingForUpdates = false; }
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        IsBusy = true;
        ConnectionTestResult = "Testing...";
        try
        {
            _connector.Domain   = string.IsNullOrWhiteSpace(Domain) ? null : Domain;
            _connector.Username = string.IsNullOrWhiteSpace(Username) ? null : Username;
            _connector.Password = string.IsNullOrWhiteSpace(Password) ? null : Password;

            ConnectionTestPassed = await Task.Run(() => _connector.TestConnection());
            ConnectionTestResult = ConnectionTestPassed ? "✓ Connected" : "✗ Failed";
            ConnectionInfo       = ConnectionTestPassed
                ? $"✓ {(string.IsNullOrWhiteSpace(Domain) ? "Current domain" : Domain)}"
                : "✗ Not connected";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void Save()
    {
        _connector.Domain   = string.IsNullOrWhiteSpace(Domain) ? null : Domain;
        _connector.Username = string.IsNullOrWhiteSpace(Username) ? null : Username;
        _connector.Password = string.IsNullOrWhiteSpace(Password) ? null : Password;
        // M1: persist the Domain Admin password only on explicit opt-in. Unticked means
        // "keep nothing on disk" — an existing credentials.dat is deleted, like the login does.
        if (RememberCredentials)
            CredentialStore.Save(_credentialPath, Domain ?? "", Username ?? "", Password ?? "");
        else
            CredentialStore.Clear(_credentialPath);
        StatusMessage       = RememberCredentials ? "Settings saved." : "Settings saved (credentials not stored on disk).";
        OnCredentialsSaved?.Invoke();

        var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { StatusMessage = ""; timer.Stop(); };
        timer.Start();
    }
}
