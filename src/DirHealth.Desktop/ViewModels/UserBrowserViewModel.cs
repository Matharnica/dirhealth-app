using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public partial class UserBrowserViewModel : BaseViewModel
{
    private readonly AdScanner          _scanner;
    private readonly UserDetailViewModel _detail;

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _userFilter = "All";
    [ObservableProperty] private bool   _showDetail;

    public ObservableCollection<AdUser> Users { get; } = new();
    public UserDetailViewModel Detail => _detail;
    public List<string> UserFilterOptions { get; } = ["All", "Active", "Disabled", "Expiring 30d", "Never logged in"];

    private List<AdUser> _allUsers = new();

    public UserBrowserViewModel() : this(null!, null!) { }

    public UserBrowserViewModel(AdScanner scanner, UserDetailViewModel detail)
    {
        _scanner = scanner;
        _detail  = detail;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_scanner is null) return;
        IsLoading = true;
        Users.Clear();
        _allUsers.Clear();
        try
        {
            _allUsers = await _scanner.GetAllUsersAsync();
            ApplyFilter();
        }
        finally { IsLoading = false; }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnUserFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Users.Clear();
        var query = FilterText.Trim();
        foreach (var u in _allUsers)
        {
            if (!string.IsNullOrEmpty(query) &&
                !u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !u.SamAccountName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !u.Email.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            bool pass = UserFilter switch
            {
                "Active"          => u.IsEnabled,
                "Disabled"        => !u.IsEnabled,
                "Expiring 30d"    => u.DaysUntilPasswordExpiry is >= 0 and <= 30,
                "Never logged in" => u.LastLogon is null,
                _                 => true
            };
            if (pass) Users.Add(u);
        }
    }

    [RelayCommand]
    public async Task SelectUserAsync(AdUser user)
    {
        if (_detail is null) return;
        ShowDetail = true;
        await _detail.LoadAsync(user);
    }
}
