using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public partial class DomainAdminsViewModel : BaseViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool          _isLoading;
    [ObservableProperty] private AdGroupDetail? _group;

    public TableViewModel<AdGroupMember> Table { get; } = new("Domain Admins", ViewColumns.GroupMembers)
    {
        Dn  = m => m.DistinguishedName,
        Sam = m => m.SamAccountName,
    };

    public DomainAdminsViewModel(AdScanner scanner)
    {
        _scanner = scanner;
        if (scanner is not null) Table.ContextProvider = scanner.GetExportContext;
    }

    partial void OnGroupChanged(AdGroupDetail? value) => Table.SetRows(value?.Members ?? []);

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Group     = null;
        try
        {
            Group         = await _scanner.GetDomainAdminsAsync();
            // S13-1: a group that could not be resolved says why instead of "0 members"
            StatusMessage = string.IsNullOrEmpty(Group.DistinguishedName)
                ? $"{Group.Name}: {Group.Description}"
                : $"{Group.Members.Count} member(s) in {Group.Name}";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
