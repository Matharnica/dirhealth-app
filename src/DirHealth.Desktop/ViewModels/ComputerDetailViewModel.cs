using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class ComputerDetailViewModel : BaseViewModel
{
    private readonly AdWmiClient _wmi;

    [ObservableProperty] private AdComputer? _computer;
    [ObservableProperty] private bool        _isOnline;
    [ObservableProperty] private long        _pingMs = -1;
    [ObservableProperty] private bool        _isLoading;
    [ObservableProperty] private string      _wmiError = "";

    public ObservableCollection<WmiDisk>          Disks       { get; } = new();
    public ObservableCollection<WmiLocalAdmin>    LocalAdmins { get; } = new();
    public ObservableCollection<WmiLoggedOnUser>  LoggedOn    { get; } = new();
    public ObservableCollection<WmiEventLogEntry> EventLog    { get; } = new();

    [ObservableProperty] private string _selectedLog      = "System";
    [ObservableProperty] private string _selectedSeverity = "All";
    [ObservableProperty] private int    _maxEntries       = 50;

    public List<string> LogSources  { get; } = new() { "System", "Security", "Application" };
    public List<string> Severities  { get; } = new() { "All", "Error", "Warning", "Information" };
    public List<int>    EntryCounts { get; } = new() { 50, 100, 500 };

    public ComputerDetailViewModel(AdWmiClient wmi)
    {
        _wmi = wmi;
    }

    public async Task LoadAsync(AdComputer computer)
    {
        Computer  = computer;
        IsLoading = true;
        WmiError  = "";
        Disks.Clear(); LocalAdmins.Clear(); LoggedOn.Clear(); EventLog.Clear();

        try
        {
            var hostname = computer.Name;
            PingMs   = await _wmi.PingTimeMs(hostname);
            IsOnline = PingMs >= 0;

            if (IsOnline)
            {
                var disks    = await _wmi.GetDisksAsync(hostname);
                var admins   = await _wmi.GetLocalAdminsAsync(hostname);
                var loggedOn = await _wmi.GetLoggedOnUsersAsync(hostname);
                foreach (var d in disks)    Disks.Add(d);
                foreach (var a in admins)   LocalAdmins.Add(a);
                foreach (var u in loggedOn) LoggedOn.Add(u);
                await RefreshEventLogAsync();
            }
        }
        catch (Exception ex)
        {
            WmiError = $"WMI error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshEventLogAsync()
    {
        if (Computer is null || !IsOnline) return;
        EventLog.Clear();
        var severity = SelectedSeverity == "All" ? null : SelectedSeverity;
        var entries  = await _wmi.GetEventLogAsync(Computer.Name, SelectedLog, severity, MaxEntries);
        foreach (var e in entries) EventLog.Add(e);
    }
}
