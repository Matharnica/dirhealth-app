using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

/// <summary>
/// Computer detail. Fix H1 (2026-11-16): selecting a computer shows AD data only. Ping and WMI
/// run only after "Load live details" and only against dNSHostName (never the cn), with
/// Kerberos enforced in <see cref="AdWmiClient"/>. Selecting another computer drops live data.
/// </summary>
public partial class ComputerDetailViewModel : BaseViewModel
{
    internal const string NoDnsHostNameMessage = "No DNS host name in AD — live details unavailable.";

    private readonly IWmiClient _wmi;
    // Bumped on every selection/load; results of an older load are dropped.
    private int _generation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadLiveDetailsCommand))]
    private AdComputer? _computer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshEventLogCommand))]
    private bool _isOnline;

    [ObservableProperty] private long _pingMs = -1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadLiveDetailsCommand), nameof(RefreshEventLogCommand))]
    private bool _isLoading;

    [ObservableProperty] private string _wmiError = "";

    /// <summary>True once live details were requested for the current computer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshEventLogCommand))]
    private bool _liveLoaded;

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

    public ComputerDetailViewModel(IWmiClient wmi)
    {
        _wmi = wmi;
    }

    /// <summary>Shows a computer's AD data. Makes no network connection to the computer.</summary>
    public Task LoadAsync(AdComputer computer)
    {
        _generation++;
        Computer   = computer;
        IsLoading  = false;
        ResetLive();
        WmiError   = HasValidDnsHostName(computer) ? "" : NoDnsHostNameMessage;
        return Task.CompletedTask;
    }

    private static bool HasValidDnsHostName(AdComputer? c) =>
        c is not null && WmiAuthority.IsValidFqdn(c.DnsHostName);

    private void ResetLive()
    {
        LiveLoaded = false;
        IsOnline   = false;
        PingMs     = -1;
        Disks.Clear(); LocalAdmins.Clear(); LoggedOn.Clear(); EventLog.Clear();
    }

    private bool CanLoadLiveDetails() => !IsLoading && HasValidDnsHostName(Computer);

    /// <summary>Ping + disks + local admins + sessions + event log — only on explicit click.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadLiveDetails))]
    public async Task LoadLiveDetailsAsync()
    {
        var computer = Computer;
        if (!HasValidDnsHostName(computer)) { WmiError = NoDnsHostNameMessage; return; }

        var gen  = ++_generation;
        var host = computer!.DnsHostName;
        IsLoading = true;
        WmiError  = "";
        ResetLive();
        try
        {
            var ms = await _wmi.PingTimeMs(host);
            if (gen != _generation) return;
            PingMs     = ms;
            IsOnline   = ms >= 0;
            LiveLoaded = true;
            if (!IsOnline)
            {
                WmiError = $"{host} did not answer ping — host offline or ICMP blocked. No WMI connection was made.";
                return;
            }

            var errors = new List<string>();
            var disks = await TryQuery(() => _wmi.GetDisksAsync(host), errors);
            // First query failed (typically: connection refused/Kerberos) — the others would
            // only repeat the same timeout three more times.
            var firstFailed = errors.Count > 0;
            List<WmiLocalAdmin>    admins   = firstFailed ? [] : await TryQuery(() => _wmi.GetLocalAdminsAsync(host), errors);
            List<WmiLoggedOnUser>  loggedOn = firstFailed ? [] : await TryQuery(() => _wmi.GetLoggedOnUsersAsync(host), errors);
            List<WmiEventLogEntry> events   = firstFailed ? [] : await TryQuery(() => QueryEventLog(host), errors);
            if (gen != _generation) return;

            foreach (var d in disks)    Disks.Add(d);
            foreach (var a in admins)   LocalAdmins.Add(a);
            foreach (var u in loggedOn) LoggedOn.Add(u);
            foreach (var e in events)   EventLog.Add(e);
            WmiError = string.Join(Environment.NewLine, errors.Distinct());
        }
        catch (Exception ex)
        {
            if (gen == _generation) WmiError = WmiErrors.Describe(ex);
        }
        finally
        {
            if (gen == _generation) IsLoading = false;
        }
    }

    private bool CanRefreshEventLog() => LiveLoaded && IsOnline && !IsLoading;

    /// <summary>Reloads the event log — enabled only after live details were loaded.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshEventLog))]
    public async Task RefreshEventLogAsync()
    {
        if (!CanRefreshEventLog() || !HasValidDnsHostName(Computer)) return;
        var gen  = _generation;
        var host = Computer!.DnsHostName;
        IsLoading = true;
        try
        {
            var entries = await QueryEventLog(host);
            if (gen != _generation) return;
            EventLog.Clear();
            foreach (var e in entries) EventLog.Add(e);
            WmiError = "";
        }
        catch (Exception ex)
        {
            if (gen == _generation) WmiError = WmiErrors.Describe(ex);
        }
        finally
        {
            if (gen == _generation) IsLoading = false;
        }
    }

    private Task<List<WmiEventLogEntry>> QueryEventLog(string host)
    {
        var severity = SelectedSeverity == "All" ? null : SelectedSeverity;
        return _wmi.GetEventLogAsync(host, SelectedLog, severity, MaxEntries);
    }

    private static async Task<List<T>> TryQuery<T>(Func<Task<List<T>>> query, List<string> errors)
    {
        try { return await query(); }
        catch (Exception ex) { errors.Add(WmiErrors.Describe(ex)); return []; }
    }
}
