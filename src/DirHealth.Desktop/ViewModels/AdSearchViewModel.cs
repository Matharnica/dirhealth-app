using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using System.Collections.ObjectModel;

namespace DirHealth.Desktop.ViewModels;

public record SearchModeItem(SearchMode Mode, string Label);

public partial class AdSearchViewModel : BaseViewModel
{
    private readonly AdSearcher? _searcher;

    [ObservableProperty] private string         _query = "";
    [ObservableProperty] private SearchModeItem _selectedMode;
    [ObservableProperty] private bool           _isLoading;
    [ObservableProperty] private string         _statusMessage = "";

    public ObservableCollection<AdSearchResult> Results { get; } = new();

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
    }

    public AdSearchViewModel(AdSearcher searcher) : this()
    {
        _searcher = searcher;
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query) || _searcher is null) return;
        IsLoading     = true;
        StatusMessage = "";
        Results.Clear();
        try
        {
            var results = await _searcher.SearchAsync(Query, SelectedMode.Mode);
            foreach (var r in results) Results.Add(r);
            StatusMessage = results.Count == 0
                ? "No results found."
                : $"{results.Count} result(s) found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
