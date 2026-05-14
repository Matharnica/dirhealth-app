using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class PrivilegedGroupsViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private List<PrivilegedGroupSummary>? _groups;

    public int TotalMembers => Groups?.Sum(g => g.MemberCount) ?? 0;
    public int EmptyGroups  => Groups?.Count(g => g.MemberCount == 0) ?? 0;

    public PrivilegedGroupsViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Groups    = null;
        try
        {
            Groups = await _scanner.GetPrivilegedGroupSummariesAsync();
            OnPropertyChanged(nameof(TotalMembers));
            OnPropertyChanged(nameof(EmptyGroups));
            StatusMessage = $"{Groups.Count} privileged groups · {TotalMembers} total members";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
