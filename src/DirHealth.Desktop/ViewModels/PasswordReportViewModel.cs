using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;

using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public partial class PasswordReportViewModel : BaseViewModel
{
    private readonly AdScanner   _scanner;
    private readonly PdfExporter _pdfExporter = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int  _withinDays = 30;

    /// <summary>Shared sortable/exportable table (S14-2) — CSV export lives in its Export menu.</summary>
    public TableViewModel<AdUser> Table { get; } = new("Password Report", ViewColumns.PasswordReport)
    {
        Dn  = u => u.DistinguishedName,
        Sid = u => u.Sid,
        Sam = u => u.SamAccountName,
    };

    public int UserCount => Table.TotalCount;
    public List<int> DayOptions { get; } = [14, 30, 60, 90];

    public PasswordReportViewModel(AdScanner scanner)
    {
        _scanner = scanner;
        Table.FilterDescription = () => $"password expiring within {WithinDays} days";
        if (scanner is not null) Table.ContextProvider = scanner.GetExportContext;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Table.Clear();
        try
        {
            var users = await _scanner.GetExpiringPasswordUsersAsync(WithinDays);
            Table.SetRows(users);
            StatusMessage = $"{users.Count} user(s) with password expiring within {WithinDays} days.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(UserCount));
            ExportPdfCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnWithinDaysChanged(int value) => _ = LoadAsync();

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
                _pdfExporter.ExportPasswordReport(Table.FilteredRows, dlg.FileName);
                StatusMessage = $"Exported: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
        }
    }

    private bool HasUsers() => Table.TotalCount > 0;
}
