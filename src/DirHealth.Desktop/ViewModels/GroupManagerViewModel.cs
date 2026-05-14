using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class GroupManagerViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool          _isLoading;
    [ObservableProperty] private bool          _isLoadingDetail;
    [ObservableProperty] private string        _filterText = "";
    [ObservableProperty] private AdGroupDetail? _selectedGroup;

    public ObservableCollection<AdGroup> Groups { get; } = new();
    private List<AdGroup> _allGroups = new();
    private DateTime _lastLoaded = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public GroupManagerViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_allGroups.Count > 0 && DateTime.Now - _lastLoaded < CacheTtl)
        {
            ApplyFilter();
            return;
        }
        IsLoading = true;
        Groups.Clear();
        _allGroups.Clear();
        try
        {
            _allGroups  = await _scanner.GetAllGroupsWithCountAsync();
            _lastLoaded = DateTime.Now;
            ApplyFilter();
        }
        catch (Exception ex) { StatusMessage = $"Failed to load groups: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Groups.Clear();
        var q = FilterText.Trim();
        foreach (var g in _allGroups)
        {
            if (string.IsNullOrEmpty(q) ||
                g.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                Groups.Add(g);
        }
    }

    [RelayCommand]
    public async Task SelectGroupAsync(AdGroup group)
    {
        IsLoadingDetail = true;
        SelectedGroup   = null;
        try
        {
            SelectedGroup = await _scanner.GetGroupDetailAsync(group.DistinguishedName);
        }
        catch (Exception ex) { StatusMessage = $"Failed to load group details: {ex.Message}"; }
        finally { IsLoadingDetail = false; }
    }

    internal void InvalidateCache()
    {
        _lastLoaded = DateTime.MinValue;
        _allGroups.Clear();
    }
}
