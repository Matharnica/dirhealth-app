using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public partial class ComputerBrowserViewModel : ListBrowserViewModel
{
    private readonly AdScanner              _scanner;
    private readonly ComputerDetailViewModel _detail;

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool   _showDetail;

    // S15-6 quick-filter chips. Password/admin chips do not apply to computer accounts.
    [ObservableProperty] private bool _chipDisabled;
    [ObservableProperty] private bool _chipInactive;

    partial void OnChipDisabledChanged(bool value) => ApplyFilter();
    partial void OnChipInactiveChanged(bool value) => ApplyFilter();

    /// <summary>Shared sortable/exportable table (S14-2).</summary>
    public TableViewModel<AdComputer> Table { get; }
    public ComputerDetailViewModel Detail => _detail;

    private List<AdComputer> _allComputers = new();
    private DateTime _lastLoaded = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    protected override bool IsEmpty => _allComputers.Count == 0;
    public override string EmptyMessage => "No computer accounts found.";

    public ComputerBrowserViewModel() : this(null!, null!) { }

    public ComputerBrowserViewModel(AdScanner scanner, ComputerDetailViewModel detail)
    {
        _scanner = scanner;
        _detail  = detail;
        Table = new TableViewModel<AdComputer>("Computers", ViewColumns.Computers) { Dn = c => c.DistinguishedName };
        Table.FilterDescription = () => $"text='{FilterText}', chips={new QuickFilters.ComputerChips(ChipDisabled, ChipInactive)}";
        if (scanner is not null) Table.ContextProvider = scanner.GetExportContext;
        Table.SelectionChanged += row => { if (row is AdComputer c) _ = SelectComputerAsync(c); };
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_scanner is null) return;
        if (_allComputers.Count > 0 && DateTime.Now - _lastLoaded < CacheTtl)
        {
            ApplyFilter();
            return;
        }
        BeginLoad();
        Table.Clear();
        _allComputers.Clear();
        try
        {
            _allComputers = await _scanner.GetAllComputersAsync();
            _lastLoaded   = DateTime.Now;
            Table.SetRows(_allComputers);
            ApplyFilter();
            EndLoad();
        }
        catch (Exception ex) { SetError($"Failed to load computers: {ex.Message}"); }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = FilterText.Trim();
        var chips = new QuickFilters.ComputerChips(ChipDisabled, ChipInactive);
        var now   = DateTime.UtcNow;
        if (string.IsNullOrEmpty(query) && !chips.Any) { Table.SetFilter(null); return; }
        Table.SetFilter(c =>
            (string.IsNullOrEmpty(query) ||
             c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             c.OperatingSystem.Contains(query, StringComparison.OrdinalIgnoreCase))
            && QuickFilters.Matches(c, chips, now));
    }

    [RelayCommand]
    public async Task SelectComputerAsync(AdComputer computer)
    {
        if (_detail is null) return;
        ShowDetail = true;
        await _detail.LoadAsync(computer);
    }

    internal void InvalidateCache()
    {
        _lastLoaded = DateTime.MinValue;
        _allComputers.Clear();
    }
}
