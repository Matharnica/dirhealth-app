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

    public GroupManagerViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Groups.Clear();
        _allGroups.Clear();
        try
        {
            _allGroups = await _scanner.GetAllGroupsWithCountAsync();
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
}
