using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public partial class GroupManagerViewModel : ListBrowserViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private bool          _isLoadingDetail;
    [ObservableProperty] private string        _filterText = "";
    [ObservableProperty] private AdGroupDetail? _selectedGroup;

    /// <summary>Group list (S14-2).</summary>
    public TableViewModel<AdGroup> Table { get; } = new("Groups", ViewColumns.Groups) { Dn = g => g.DistinguishedName };

    /// <summary>Members of the selected group (S14-2).</summary>
    public TableViewModel<AdGroupMember> MembersTable { get; } = new("Group Members", ViewColumns.GroupMembers)
    {
        Dn  = m => m.DistinguishedName,
        Sam = m => m.SamAccountName,
    };
    private List<AdGroup> _allGroups = new();
    private DateTime _lastLoaded = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    protected override bool IsEmpty => _allGroups.Count == 0;
    public override string EmptyMessage => "No groups found.";

    public GroupManagerViewModel(AdScanner scanner)
    {
        _scanner = scanner;
        Table.FilterDescription = () => $"text='{FilterText}'";
        if (scanner is not null)
        {
            Table.ContextProvider        = scanner.GetExportContext;
            MembersTable.ContextProvider = scanner.GetExportContext;
        }
        MembersTable.FilterDescription = () => $"group={SelectedGroup?.Name}";
        Table.SelectionChanged += row => { if (row is AdGroup g) _ = SelectGroupAsync(g); };
    }

    partial void OnSelectedGroupChanged(AdGroupDetail? value) => MembersTable.SetRows(value?.Members ?? []);

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_allGroups.Count > 0 && DateTime.Now - _lastLoaded < CacheTtl)
        {
            ApplyFilter();
            return;
        }
        BeginLoad();
        Table.Clear();
        _allGroups.Clear();
        try
        {
            _allGroups  = await _scanner.GetAllGroupsWithCountAsync();
            _lastLoaded = DateTime.Now;
            Table.SetRows(_allGroups);
            ApplyFilter();
            EndLoad();
        }
        catch (Exception ex) { SetError($"Failed to load groups: {ex.Message}"); }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FilterText.Trim();
        Table.SetFilter(string.IsNullOrEmpty(q) ? null : g =>
            g.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            g.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    public async Task SelectGroupAsync(AdGroup group)
    {
        IsLoadingDetail = true;
        SelectedGroup   = null;
        try
        {
            SelectedGroup = await _scanner.GetGroupDetailAsync(group.DistinguishedName);
        }
        catch (Exception ex) { StatusMessage = $"Failed to load group details: {ex.Message}"; }
        finally { IsLoadingDetail = false; }
    }

    internal void InvalidateCache()
    {
        _lastLoaded = DateTime.MinValue;
        _allGroups.Clear();
    }
}
