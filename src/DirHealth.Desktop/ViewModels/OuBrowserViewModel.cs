using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public partial class OuBrowserViewModel : ListBrowserViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private string  _filterText  = "";
    [ObservableProperty] private AdOU?   _selectedOU;
    [ObservableProperty] private int     _selectedOuUserCount;
    [ObservableProperty] private int     _selectedOuComputerCount;
    [ObservableProperty] private int     _selectedOuGroupCount;
    [ObservableProperty] private bool    _isLoadingCounts;

    /// <summary>Shared sortable/exportable table (S14-3).</summary>
    public TableViewModel<AdOU> Table { get; } = new("Organizational Units", ViewColumns.OrganizationalUnits) { Dn = o => o.DistinguishedName };
    private List<AdOU> _allOUs = new();

    protected override bool IsEmpty => _allOUs.Count == 0;
    public override string EmptyMessage => "No organizational units found.";

    public OuBrowserViewModel(AdScanner scanner)
    {
        _scanner = scanner;
        Table.FilterDescription = () => $"text='{FilterText}'";
        if (scanner is not null) Table.ContextProvider = scanner.GetExportContext;
        Table.SelectionChanged += row => { if (row is AdOU ou) _ = SelectOUAsync(ou); };
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        BeginLoad();
        Table.Clear();
        _allOUs.Clear();
        try
        {
            _allOUs = await _scanner.GetAllOUsAsync();
            Table.SetRows(_allOUs);
            ApplyFilter();
            EndLoad();
        }
        catch (Exception ex) { SetError($"Failed to load OUs: {ex.Message}"); }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FilterText.Trim();
        Table.SetFilter(string.IsNullOrEmpty(q) ? null : ou =>
            ou.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            ou.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    public async Task SelectOUAsync(AdOU ou)
    {
        SelectedOU               = ou;
        SelectedOuUserCount      = 0;
        SelectedOuComputerCount  = 0;
        SelectedOuGroupCount     = 0;
        IsLoadingCounts          = true;
        try
        {
            var (users, computers, groups) = await _scanner.GetOUCountsAsync(ou.DistinguishedName);
            SelectedOuUserCount     = users;
            SelectedOuComputerCount = computers;
            SelectedOuGroupCount    = groups;
        }
        catch (Exception ex) { StatusMessage = $"Failed to load OU details: {ex.Message}"; }
        finally { IsLoadingCounts = false; }
    }
}
