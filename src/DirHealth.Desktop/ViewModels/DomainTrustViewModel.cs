using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class DomainTrustViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private List<AdDomainTrust>? _trusts;

    public int TrustCount         => Trusts?.Count ?? 0;
    public int BidirectionalCount => Trusts?.Count(t => t.IsBidirectional) ?? 0;
    public int ForestTrustCount   => Trusts?.Count(t => t.IsForestTrust) ?? 0;

    public DomainTrustViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Trusts    = null;
        try
        {
            Trusts = await _scanner.GetDomainTrustsAsync();
            OnPropertyChanged(nameof(TrustCount));
            OnPropertyChanged(nameof(BidirectionalCount));
            OnPropertyChanged(nameof(ForestTrustCount));
            StatusMessage = Trusts.Count == 0
                ? "No domain trusts found."
                : $"{Trusts.Count} trust(s) found";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
