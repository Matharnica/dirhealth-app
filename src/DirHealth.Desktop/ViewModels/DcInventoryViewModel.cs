using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class DcInventoryViewModel : ListBrowserViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private List<AdDomainController>? _domainControllers;

    public int DcCount  => DomainControllers?.Count ?? 0;
    public int EolCount => DomainControllers?.Count(d => d.IsEol) ?? 0;
    public int GcCount  => DomainControllers?.Count(d => d.IsGlobalCatalog) ?? 0;

    protected override bool IsEmpty => DomainControllers is { Count: 0 };
    public override string EmptyMessage => "No domain controllers found.";

    public DcInventoryViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        BeginLoad();
        DomainControllers = null;
        try
        {
            DomainControllers = await _scanner.GetAllDomainControllersAsync();
            OnPropertyChanged(nameof(DcCount));
            OnPropertyChanged(nameof(EolCount));
            OnPropertyChanged(nameof(GcCount));
            StatusMessage = EolCount > 0
                ? $"{DcCount} DC(s) — {EolCount} on end-of-life OS"
                : $"{DcCount} domain controller(s) found";
            EndLoad();
        }
        catch (Exception ex) { SetError($"Error: {ex.Message}"); }
    }
}
