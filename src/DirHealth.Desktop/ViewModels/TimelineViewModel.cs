using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class TimelineViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private List<AdRecentChange>? _changes;
    [ObservableProperty] private int  _selectedDays = 30;

    public int[] AvailablePeriods { get; } = [7, 30, 90];

    public int CreatedCount  => Changes?.Count(c => c.Action == "Created")  ?? 0;
    public int ModifiedCount => Changes?.Count(c => c.Action == "Modified") ?? 0;

    public TimelineViewModel(AdScanner scanner) { _scanner = scanner; }

    partial void OnSelectedDaysChanged(int value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Changes   = null;
        try
        {
            Changes = await _scanner.GetRecentChangesAsync(SelectedDays);
            OnPropertyChanged(nameof(CreatedCount));
            OnPropertyChanged(nameof(ModifiedCount));
            StatusMessage = Changes.Count == 0
                ? $"No changes in the last {SelectedDays} days."
                : $"{Changes.Count} change(s) in the last {SelectedDays} days";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
