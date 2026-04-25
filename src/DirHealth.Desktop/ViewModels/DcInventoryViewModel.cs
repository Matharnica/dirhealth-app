using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class DcInventoryViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private List<AdDomainController>? _domainControllers;

    public int DcCount  => DomainControllers?.Count ?? 0;
    public int EolCount => DomainControllers?.Count(d => d.IsEol) ?? 0;
    public int GcCount  => DomainControllers?.Count(d => d.IsGlobalCatalog) ?? 0;

    public DcInventoryViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading          = true;
        DomainControllers  = null;
        try
        {
            DomainControllers = await _scanner.GetAllDomainControllersAsync();
            OnPropertyChanged(nameof(DcCount));
            OnPropertyChanged(nameof(EolCount));
            OnPropertyChanged(nameof(GcCount));
            StatusMessage = EolCount > 0
                ? $"{DcCount} DC(s) — {EolCount} on end-of-life OS"
                : $"{DcCount} domain controller(s) found";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
