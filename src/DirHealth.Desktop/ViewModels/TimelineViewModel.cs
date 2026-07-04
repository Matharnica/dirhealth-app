using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels;

public partial class TimelineViewModel : ListBrowserViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private List<AdRecentChange>? _changes;
    [ObservableProperty] private int  _selectedDays = 30;

    public int[] AvailablePeriods { get; } = [7, 30, 90];

    public int CreatedCount  => Changes?.Count(c => c.Action == "Created")  ?? 0;
    public int ModifiedCount => Changes?.Count(c => c.Action == "Modified") ?? 0;

    protected override bool IsEmpty => Changes is { Count: 0 };
    public override string EmptyMessage => $"No changes in the last {SelectedDays} days.";

    public TimelineViewModel(AdScanner scanner) { _scanner = scanner; }

    partial void OnSelectedDaysChanged(int value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        _ = LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        BeginLoad();
        Changes = null;
        try
        {
            Changes = await _scanner.GetRecentChangesAsync(SelectedDays);
            OnPropertyChanged(nameof(CreatedCount));
            OnPropertyChanged(nameof(ModifiedCount));
            StatusMessage = Changes.Count == 0
                ? $"No changes in the last {SelectedDays} days."
                : $"{Changes.Count} change(s) in the last {SelectedDays} days";
            EndLoad();
        }
        catch (Exception ex) { SetError($"Error: {ex.Message}"); }
    }
}
