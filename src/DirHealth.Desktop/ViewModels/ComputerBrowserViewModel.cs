using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class ComputerBrowserViewModel : BaseViewModel
{
    private readonly AdScanner              _scanner;
    private readonly ComputerDetailViewModel _detail;

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool   _showDetail;

    public ObservableCollection<AdComputer> Computers { get; } = new();
    public ComputerDetailViewModel Detail => _detail;

    private List<AdComputer> _allComputers = new();

    public ComputerBrowserViewModel() : this(null!, null!) { }

    public ComputerBrowserViewModel(AdScanner scanner, ComputerDetailViewModel detail)
    {
        _scanner = scanner;
        _detail  = detail;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_scanner is null) return;
        IsLoading = true;
        Computers.Clear();
        _allComputers.Clear();
        try
        {
            _allComputers = await _scanner.GetAllComputersAsync();
            ApplyFilter();
        }
        finally { IsLoading = false; }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Computers.Clear();
        var query = FilterText.Trim();
        foreach (var c in _allComputers)
        {
            if (string.IsNullOrEmpty(query) ||
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.OperatingSystem.Contains(query, StringComparison.OrdinalIgnoreCase))
                Computers.Add(c);
        }
    }

    [RelayCommand]
    public async Task SelectComputerAsync(AdComputer computer)
    {
        if (_detail is null) return;
        ShowDetail = true;
        await _detail.LoadAsync(computer);
    }
}
