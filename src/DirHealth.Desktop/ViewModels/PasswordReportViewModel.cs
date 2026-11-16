using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;

using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

/// <summary>
/// Password expiry report. Loading/empty/error state comes from <see cref="ListBrowserViewModel"/>
/// (Q6): before, the green "no users expiring ✓" hung on UserCount = 0 alone and also showed after a
/// failed LDAP query and before the first load.
/// </summary>
public partial class PasswordReportViewModel : ListBrowserViewModel
{
    private readonly PdfExporter _pdfExporter = new();
    private readonly Func<int, Task<List<AdUser>>> _load;

    [ObservableProperty] private int _withinDays = 30;

    /// <summary>Shared sortable/exportable table (S14-2) — CSV export lives in its Export menu.</summary>
    public TableViewModel<AdUser> Table { get; } = new("Password Report", ViewColumns.PasswordReport)
    {
        Dn  = u => u.DistinguishedName,
        Sid = u => u.Sid,
        Sam = u => u.SamAccountName,
    };

    public int UserCount => Table.TotalCount;
    public List<int> DayOptions { get; } = [14, 30, 60, 90];

    protected override bool IsEmpty => Table.TotalCount == 0;
    public override string EmptyMessage => $"No users with a password expiring within {WithinDays} days. ✓";

    /// <param name="load">Loader for tests; default is the scanner's query.</param>
    public PasswordReportViewModel(AdScanner scanner, Func<int, Task<List<AdUser>>>? load = null)
    {
        _load = load ?? (days => scanner.GetExpiringPasswordUsersAsync(days));
        Table.FilterDescription = () => $"password expiring within {WithinDays} days";
        if (scanner is not null) Table.ContextProvider = scanner.GetExportContext;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        BeginLoad();
        Table.Clear();
        try
        {
            var users = await _load(WithinDays);
            Table.SetRows(users);
            StatusMessage = $"{users.Count} user(s) with password expiring within {WithinDays} days.";
            EndLoad();
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally
        {
            OnPropertyChanged(nameof(UserCount));
            OnPropertyChanged(nameof(EmptyMessage));
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
