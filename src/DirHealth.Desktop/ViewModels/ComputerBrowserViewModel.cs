using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class ComputerBrowserViewModel : ListBrowserViewModel
{
    private readonly AdScanner              _scanner;
    private readonly ComputerDetailViewModel _detail;

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool   _showDetail;

    public ObservableCollection<AdComputer> Computers { get; } = new();
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
        Computers.Clear();
        _allComputers.Clear();
        try
        {
            _allComputers = await _scanner.GetAllComputersAsync();
            _lastLoaded   = DateTime.Now;
            ApplyFilter();
            EndLoad();
        }
        catch (Exception ex) { SetError($"Failed to load computers: {ex.Message}"); }
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

    internal void InvalidateCache()
    {
        _lastLoaded = DateTime.MinValue;
        _allComputers.Clear();
    }
}
