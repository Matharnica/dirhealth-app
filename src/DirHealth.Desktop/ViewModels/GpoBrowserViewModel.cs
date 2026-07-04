using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class GpoBrowserViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private bool _hasError;

    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState),
        nameof(GpoCount), nameof(OrphanedCount), nameof(DisabledCount))]
    private List<AdGpo>? _gpos;

    public int GpoCount      => Gpos?.Count ?? 0;
    public int OrphanedCount => Gpos?.Count(g => g.IsOrphaned) ?? 0;
    public int DisabledCount => Gpos?.Count(g => g.IsDisabled) ?? 0;

    // Three distinct content states: not-yet-loaded, loaded-but-empty, error (HasError).
    public bool ShowPlaceholder => !IsLoading && !HasError && Gpos is null;
    public bool ShowEmptyState  => !IsLoading && !HasError && Gpos is { Count: 0 };

    public GpoBrowserViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError  = false;
        Gpos      = null;
        try
        {
            Gpos = await _scanner.GetAllGposAsync();
            StatusMessage = Gpos.Count == 0
                ? "No group policy objects found."
                : $"{Gpos.Count} GPO(s) — {OrphanedCount} orphaned";
        }
        catch (Exception ex)
        {
            HasError      = true;
            ErrorMessage  = ex.Message;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}
