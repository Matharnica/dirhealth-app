using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class UserDetailViewModel : BaseViewModel
{
    private readonly AdScanner   _scanner;
    private readonly PdfExporter _pdfExporter = new();

    [ObservableProperty] private AdUser? _user;
    [ObservableProperty] private bool    _isLoading;

    public ObservableCollection<string> Groups { get; } = new();

    public UserDetailViewModel(AdScanner scanner)
    {
        _scanner = scanner;
    }

    public async Task LoadAsync(AdUser user)
    {
        User      = user;
        IsLoading = true;
        Groups.Clear();
        try
        {
            var groups = await _scanner.GetUserGroupsAsync(user.DistinguishedName);
            foreach (var g in groups) Groups.Add(g);
        }
        catch (Exception ex) { StatusMessage = $"Failed to load user details: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand(CanExecute = nameof(HasUser))]
    public void ExportPdf()
    {
        if (User is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"DirHealth-User-{User.SamAccountName}-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".pdf",
            Filter     = "PDF files|*.pdf"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _pdfExporter.ExportUserDetail(User, Groups, dlg.FileName);
                StatusMessage = $"Exported: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
        }
    }

    private bool HasUser() => User is not null;

    partial void OnUserChanged(AdUser? value) => ExportPdfCommand.NotifyCanExecuteChanged();
}
