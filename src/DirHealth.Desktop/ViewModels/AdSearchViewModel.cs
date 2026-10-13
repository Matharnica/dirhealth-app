using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public record SearchModeItem(SearchMode Mode, string Label);

/// <summary>What the detail panel next to the search results shows (S15-2).</summary>
public enum SearchDetailKind { None, User, Computer, Group }

public partial class AdSearchViewModel : BaseViewModel
{
    private readonly AdSearcher? _searcher;
    private readonly AdScanner?  _scanner;

    [ObservableProperty] private string         _query = "";
    [ObservableProperty] private SearchModeItem _selectedMode;
    [ObservableProperty] private bool           _isLoading;
    [ObservableProperty] private string         _statusMessage = "";
    [ObservableProperty] private bool           _hasSearched;
    /// <summary>Search threw — shown instead of the "No results" empty state (S13-4).</summary>
    [ObservableProperty] private bool           _hasError;
    [ObservableProperty] private string         _errorMessage = "";
    /// <summary>"Showing 500 of more than 500 — narrow your search" (S15-1); empty otherwise.</summary>
    [ObservableProperty] private string         _limitHint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail), nameof(ShowUserDetail), nameof(ShowComputerDetail), nameof(ShowGroupDetail))]
    private SearchDetailKind _detailKind;

    [ObservableProperty] private AdGroupDetail? _groupDetail;
    [ObservableProperty] private bool           _isLoadingDetail;

    public bool IsLdapMode => SelectedMode.Mode == SearchMode.Ldap;

    public bool ShowDetail         => DetailKind != SearchDetailKind.None;
    public bool ShowUserDetail     => DetailKind == SearchDetailKind.User;
    public bool ShowComputerDetail => DetailKind == SearchDetailKind.Computer;
    public bool ShowGroupDetail    => DetailKind == SearchDetailKind.Group;

    partial void OnSelectedModeChanged(SearchModeItem value) => OnPropertyChanged(nameof(IsLdapMode));

    /// <summary>Results as the shared sortable/exportable table (S15-1).</summary>
    public TableViewModel<AdSearchResult> Table { get; } = new("Search", ViewColumns.SearchResults)
    {
        Dn  = r => r.DistinguishedName,
        Sid = r => r.Sid,
        Sam = r => r.SamAccountName,
    };

    public TableViewModel<AdGroupMember> GroupMembersTable { get; } = new("Group Members", ViewColumns.GroupMembers)
    {
        Dn  = m => m.DistinguishedName,
        Sam = m => m.SamAccountName,
    };

    public UserDetailViewModel?     UserDetail     { get; }
    public ComputerDetailViewModel? ComputerDetail { get; }

    public List<SearchModeItem> Modes { get; } = new()
    {
        new(SearchMode.Name,  "Name"),
        new(SearchMode.Sid,   "SID"),
        new(SearchMode.Email, "Email / UPN"),
        new(SearchMode.Ou,    "OU (Distinguished Name)"),
        new(SearchMode.Ldap,  "LDAP Filter"),
    };

    public AdSearchViewModel()
    {
        _selectedMode = Modes[0];
        Table.Activated += row => { if (row is AdSearchResult r) _ = OpenDetailAsync(r); };
    }

    public AdSearchViewModel(AdSearcher searcher, AdScanner? scanner = null,
                             UserDetailViewModel? userDetail = null,
                             ComputerDetailViewModel? computerDetail = null) : this()
    {
        _searcher      = searcher;
        _scanner       = scanner;
        UserDetail     = userDetail;
        ComputerDetail = computerDetail;
        Table.FilterDescription = () => $"{SelectedMode.Label}: {Query}";
        if (scanner is not null)
        {
            Table.ContextProvider             = scanner.GetExportContext;
            GroupMembersTable.ContextProvider = scanner.GetExportContext;
        }
    }

    /// <summary>
    /// Mode actually used: a "user:/group:/computer:/ou:" prefix switches any non-LDAP mode
    /// to the name search (S15-3). The raw LDAP mode is never reinterpreted.
    /// </summary>
    public static SearchMode EffectiveMode(SearchMode selected, string query) =>
        selected != SearchMode.Ldap && SearchQuery.Parse(query).Type != SearchScopeType.Any
            ? SearchMode.Name
            : selected;

    /// <summary>Caps the list at MaxResults and returns the hint text (S15-1).</summary>
    public static (List<AdSearchResult> Shown, string Hint) ApplyLimit(List<AdSearchResult> results) =>
        results.Count > AdSearcher.MaxResults
            ? (results.Take(AdSearcher.MaxResults).ToList(),
               $"Showing {AdSearcher.MaxResults} of more than {AdSearcher.MaxResults} — narrow your search")
            : (results, "");

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query) || _searcher is null) return;
        IsLoading     = true;
        StatusMessage = "";
        HasError      = false;
        ErrorMessage  = "";
        HasSearched   = false;
        LimitHint     = "";
        Table.Clear();
        try
        {
            var raw = await _searcher.SearchAsync(Query, EffectiveMode(SelectedMode.Mode, Query));
            var (shown, hint) = ApplyLimit(raw);
            Table.SetRows(shown);
            LimitHint     = hint;
            HasSearched   = true;
            StatusMessage = shown.Count == 0
                ? "No results found."
                : $"{shown.Count} result(s) found.";
        }
        catch (Exception ex)
        {
            HasError      = true;
            ErrorMessage  = ex.Message;
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Double-click / Enter on a result opens its detail next to the list (S15-2).</summary>
    public async Task OpenDetailAsync(AdSearchResult result)
    {
        if (_scanner is null) return;
        IsLoadingDetail = true;
        try
        {
            switch (result.ObjectType)
            {
                case AdObjectType.User when UserDetail is not null:
                    var user = await _scanner.GetUserByDnAsync(result.DistinguishedName);
                    if (user is null) { StatusMessage = $"{result.Name} no longer exists."; return; }
                    DetailKind = SearchDetailKind.User;
                    await UserDetail.LoadAsync(user);
                    break;
                case AdObjectType.Computer when ComputerDetail is not null:
                    var computer = await _scanner.GetComputerByDnAsync(result.DistinguishedName);
                    if (computer is null) { StatusMessage = $"{result.Name} no longer exists."; return; }
                    DetailKind = SearchDetailKind.Computer;
                    await ComputerDetail.LoadAsync(computer);
                    break;
                case AdObjectType.Group:
                    GroupDetail = await _scanner.GetGroupDetailAsync(result.DistinguishedName);
                    GroupMembersTable.SetRows(GroupDetail.Members);
                    DetailKind = SearchDetailKind.Group;
                    break;
                default:
                    StatusMessage = $"No detail view for {result.TypeLabel}.";
                    break;
            }
        }
        catch (Exception ex) { StatusMessage = $"Could not open {result.Name}: {ex.Message}"; }
        finally { IsLoadingDetail = false; }
    }

    [RelayCommand]
    public void CloseDetail() => DetailKind = SearchDetailKind.None;
}
