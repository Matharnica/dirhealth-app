using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public partial class DomainTrustViewModel : ListBrowserViewModel
{
    private readonly AdScanner _scanner;

    [ObservableProperty] private List<AdDomainTrust>? _trusts;

    public int TrustCount         => Trusts?.Count ?? 0;
    public int BidirectionalCount => Trusts?.Count(t => t.IsBidirectional) ?? 0;
    public int ForestTrustCount   => Trusts?.Count(t => t.IsForestTrust) ?? 0;

    protected override bool IsEmpty => Trusts is { Count: 0 };
    public override string EmptyMessage => "No domain trusts found.";

    /// <summary>Shared sortable/exportable table (S14-3); mirrors Trusts.</summary>
    public TableViewModel<AdDomainTrust> Table { get; } = new("Domain Trusts", ViewColumns.Trusts) { };

    public DomainTrustViewModel(AdScanner scanner)
    {
        _scanner = scanner;
        if (scanner is not null) Table.ContextProvider = scanner.GetExportContext;
    }

    partial void OnTrustsChanged(List<AdDomainTrust>? value) => Table.SetRows(value ?? []);

    [RelayCommand]
    public async Task LoadAsync()
    {
        BeginLoad();
        Trusts = null;
        try
        {
            Trusts = await _scanner.GetDomainTrustsAsync();
            OnPropertyChanged(nameof(TrustCount));
            OnPropertyChanged(nameof(BidirectionalCount));
            OnPropertyChanged(nameof(ForestTrustCount));
            StatusMessage = Trusts.Count == 0
                ? "No domain trusts found."
                : $"{Trusts.Count} trust(s) found";
            EndLoad();
        }
        catch (Exception ex) { SetError($"Error: {ex.Message}"); }
    }
}
