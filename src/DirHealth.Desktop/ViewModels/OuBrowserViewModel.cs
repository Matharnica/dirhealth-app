using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class OuBrowserViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string  _filterText  = "";
    [ObservableProperty] private AdOU?   _selectedOU;
    [ObservableProperty] private int     _selectedOuUserCount;
    [ObservableProperty] private int     _selectedOuComputerCount;
    [ObservableProperty] private int     _selectedOuGroupCount;
    [ObservableProperty] private bool    _isLoadingCounts;

    public ObservableCollection<AdOU> OUs { get; } = new();
    private List<AdOU> _allOUs = new();

    public OuBrowserViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        OUs.Clear();
        _allOUs.Clear();
        try
        {
            _allOUs = await _scanner.GetAllOUsAsync();
            ApplyFilter();
        }
        finally { IsLoading = false; }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        OUs.Clear();
        var q = FilterText.Trim();
        foreach (var ou in _allOUs)
        {
            if (string.IsNullOrEmpty(q) ||
                ou.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                ou.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                OUs.Add(ou);
        }
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
        finally { IsLoadingCounts = false; }
    }
}
