using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class DomainAdminsViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool          _isLoading;
    [ObservableProperty] private AdGroupDetail? _group;

    public DomainAdminsViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Group     = null;
        try
        {
            Group         = await _scanner.GetDomainAdminsAsync();
            StatusMessage = $"{Group.Members.Count} member(s) in Domain Admins";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
