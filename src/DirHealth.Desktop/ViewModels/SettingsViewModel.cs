using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.Services;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly AdConnector _connector;

    [ObservableProperty] private string _selectedSection = "Connection";

    [ObservableProperty] private string _domain = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool   _connectionTestPassed;
    [ObservableProperty] private string _connectionTestResult = "";

    [ObservableProperty] private bool _autoScanEnabled = false;
    [ObservableProperty] private int  _autoScanIntervalHours = 4;

    [ObservableProperty] private string _connectionInfo = "Not tested";

    [ObservableProperty] private string _updateStatusText = "";
    [ObservableProperty] private bool   _isCheckingForUpdates;
    public string CurrentVersion { get; } = UpdateChecker.GetCurrentVersion();

    public List<int> ScanIntervalOptions { get; } = [1, 2, 4, 8, 12, 24];
    public Action<bool, int>?    OnScheduleChanged  { get; set; }
    public Func<Task<string>>?  OnCheckForUpdates  { get; set; }

    partial void OnAutoScanEnabledChanged(bool value)      => OnScheduleChanged?.Invoke(value, AutoScanIntervalHours);
    partial void OnAutoScanIntervalHoursChanged(int value) => OnScheduleChanged?.Invoke(AutoScanEnabled, value);

    public SettingsViewModel(AdConnector connector)
    {
        _connector = connector;
        Domain     = connector.Domain ?? "";
        Username   = connector.Username ?? "";
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
        CredentialStore.Save(Domain ?? "", Username ?? "", Password ?? "");
        StatusMessage       = "Settings saved.";
    }
}
