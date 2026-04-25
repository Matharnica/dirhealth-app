using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class PasswordReportViewModel : BaseViewModel
{
    private readonly AdScanner   _scanner;
    private readonly CsvExporter _csvExporter = new();
    private readonly PdfExporter _pdfExporter = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int  _withinDays = 30;

    public ObservableCollection<AdUser> Users { get; } = new();
    public List<int> DayOptions { get; } = [14, 30, 60, 90];

    public PasswordReportViewModel(AdScanner scanner) { _scanner = scanner; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Users.Clear();
        try
        {
            var users = await _scanner.GetExpiringPasswordUsersAsync(WithinDays);
            foreach (var u in users) Users.Add(u);
            StatusMessage = $"{Users.Count} user(s) with password expiring within {WithinDays} days.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally
        {
            IsLoading = false;
            ExportCsvCommand.NotifyCanExecuteChanged();
            ExportPdfCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnWithinDaysChanged(int value) => _ = LoadAsync();

    [RelayCommand(CanExecute = nameof(HasUsers))]
    public void ExportCsv()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"DirHealth-PasswordReport-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".csv",
            Filter     = "CSV files|*.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _csvExporter.ExportPasswordReport(Users, dlg.FileName);
                StatusMessage = $"Exported: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
        }
    }

    [RelayCommand(CanExecute = nameof(HasUsers))]
    public void ExportPdf()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"DirHealth-PasswordReport-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".pdf",
            Filter     = "PDF files|*.pdf"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _pdfExporter.ExportPasswordReport(Users, dlg.FileName);
                StatusMessage = $"Exported: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
        }
    }

    private bool HasUsers() => Users.Count > 0;
}
