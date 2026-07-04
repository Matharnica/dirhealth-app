using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;

namespace DirHealth.Desktop.ViewModels;

public partial class DataQualityViewModel : BaseViewModel
{
    private readonly AdScanner   _scanner;
    private readonly CsvExporter _csvExporter = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private bool _hasError;

    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private List<AdAttributeCompleteness>? _attributes;

    [ObservableProperty] private int _totalUsers;

    public bool ShowPlaceholder => !IsLoading && !HasError && Attributes is null;
    public bool ShowEmptyState  => !IsLoading && !HasError && TotalUsers == 0 && Attributes is not null;

    public DataQualityViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading  = true;
        HasError   = false;
        Attributes = null;
        try
        {
            var result = await _scanner.GetDataQualityAsync();
            TotalUsers = result.TotalUsers;
            Attributes = result.Attributes;
            OnPropertyChanged(nameof(TotalUsers));
            ExportCsvCommand.NotifyCanExecuteChanged();
            StatusMessage = result.TotalUsers == 0
                ? "No user accounts found."
                : $"{result.TotalUsers} user account(s) analysed";
        }
        catch (Exception ex)
        {
            HasError      = true;
            ErrorMessage  = ex.Message;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand(CanExecute = nameof(HasData))]
    public void ExportCsv()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"DirHealth-DataQuality-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".csv",
            Filter     = "CSV files|*.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _csvExporter.ExportDataQuality(Attributes!, dlg.FileName);
            StatusMessage = $"Exported: {System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
    }

    private bool HasData() => Attributes is { Count: > 0 } && TotalUsers > 0;
}
