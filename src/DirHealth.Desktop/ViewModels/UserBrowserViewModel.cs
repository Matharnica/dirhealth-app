using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

/// <summary>Choices from the user export dialog (S14-4). Opt-in columns default to off.</summary>
public record UserExportOptions(bool FilteredOnly, bool IncludeMail = false, bool IncludeManager = false, bool IncludeDescription = false);

public partial class UserBrowserViewModel : ListBrowserViewModel
{
    private readonly AdScanner          _scanner;
    private readonly UserDetailViewModel _detail;

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _userFilter = "All";
    [ObservableProperty] private bool   _showDetail;

    // S15-6 quick-filter chips (local, AND-combined)
    [ObservableProperty] private bool _chipDisabled;
    [ObservableProperty] private bool _chipPasswordNeverExpires;
    [ObservableProperty] private bool _chipAdmin;
    [ObservableProperty] private bool _chipInactive;

    partial void OnChipDisabledChanged(bool value)             => ApplyFilter();
    partial void OnChipPasswordNeverExpiresChanged(bool value) => ApplyFilter();
    partial void OnChipAdminChanged(bool value)                => ApplyFilter();
    partial void OnChipInactiveChanged(bool value)             => ApplyFilter();

    private QuickFilters.UserChips Chips => new(ChipDisabled, ChipPasswordNeverExpires, ChipAdmin, ChipInactive);

    /// <summary>Shared sortable/exportable table (S14-1).</summary>
    public TableViewModel<AdUser> Table { get; }
    public UserDetailViewModel Detail => _detail;
    public List<string> UserFilterOptions { get; } = ["All", "Active", "Disabled", "Expiring 30d", "Never logged in"];

    private List<AdUser> _allUsers = new();
    private DateTime _lastLoaded = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    protected override bool IsEmpty => _allUsers.Count == 0;
    public override string EmptyMessage => "No user accounts found.";

    public UserBrowserViewModel() : this(null!, null!) { }

    public UserBrowserViewModel(AdScanner scanner, UserDetailViewModel detail)
    {
        _scanner = scanner;
        _detail  = detail;
        Table = new TableViewModel<AdUser>("Users", UserColumns.Grid)
        {
            Dn  = u => u.DistinguishedName,
            Sid = u => u.Sid,
            Sam = u => u.SamAccountName,
        };
        Table.FilterDescription = () => $"text='{FilterText}', status={UserFilter}, chips={Chips}";
        if (scanner is not null) Table.ContextProvider = scanner.GetExportContext;
        Table.SelectionChanged += row => { if (row is AdUser u) _ = SelectUserAsync(u); };
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_scanner is null) return;
        if (_allUsers.Count > 0 && DateTime.Now - _lastLoaded < CacheTtl)
        {
            ApplyFilter();
            return;
        }
        BeginLoad();
        Table.Clear();
        _allUsers.Clear();
        try
        {
            _allUsers   = await _scanner.GetAllUsersAsync();
            _lastLoaded = DateTime.Now;
            Table.SetRows(_allUsers);
            ApplyFilter();
            EndLoad();
        }
        catch (Exception ex) { SetError($"Failed to load users: {ex.Message}"); }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnUserFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query  = FilterText.Trim();
        var status = UserFilter;
        var chips  = Chips;
        var now    = DateTime.UtcNow;
        Table.SetFilter(u => MatchesFilter(u, query, status) && QuickFilters.Matches(u, chips, now));
    }

    public static bool MatchesFilter(AdUser u, string query, string status)
    {
        if (!string.IsNullOrEmpty(query) &&
            !u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !u.SamAccountName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !u.Email.Contains(query, StringComparison.OrdinalIgnoreCase))
            return false;

        return status switch
        {
            "Active"          => u.IsEnabled,
            "Disabled"        => !u.IsEnabled,
            "Expiring 30d"    => u.DaysUntilPasswordExpiry is >= 0 and <= 30,
            "Never logged in" => u.LastLogon is null,
            _                 => true
        };
    }

    [RelayCommand]
    public async Task SelectUserAsync(AdUser user)
    {
        if (_detail is null) return;
        ShowDetail = true;
        await _detail.LoadAsync(user);
    }

    /// <summary>
    /// ADRecon-style user export (S14-4) with opt-in columns; writes CSV + .meta.txt.
    /// Returns the CSV path, or null when cancelled or failed (message in Table.StatusMessage).
    /// </summary>
    public string? ExportFullUsers(UserExportOptions options)
    {
        var columns = UserColumns.Export(options.IncludeMail, options.IncludeManager, options.IncludeDescription);
        var rows    = options.FilteredOnly ? Table.RowsForFilteredExport() : Table.RowsForFullExport();
        var optIn   = string.Join(", ", new[]
        {
            options.IncludeMail ? "Mail" : null,
            options.IncludeManager ? "Manager" : null,
            options.IncludeDescription ? "Description" : null,
        }.Where(x => x is not null));
        var filter = (options.FilteredOnly ? Table.FilterDescription?.Invoke() ?? "" : "(none — all rows)")
                   + $"; opt-in columns: {(optIn.Length == 0 ? "none" : optIn)}";
        var exportTable = new TableViewModel<AdUser>("Users-full", columns) { ContextProvider = Table.ContextProvider };
        var path = exportTable.Export(rows, filter, options.FilteredOnly ? "filtered" : "all");
        Table.StatusMessage = exportTable.StatusMessage;
        return path;
    }

    internal void InvalidateCache()
    {
        _lastLoaded = DateTime.MinValue;
        _allUsers.Clear();
    }
}
