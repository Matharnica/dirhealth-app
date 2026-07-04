using CommunityToolkit.Mvvm.ComponentModel;

namespace DirHealth.Desktop.ViewModels;

// Shared loading / empty / error state for every list-style view (S12-1).
// Derived VMs report IsEmpty and (optionally) a custom EmptyMessage; the ListStateOverlay
// control renders the resulting state uniformly across all browsers.
public abstract partial class ListBrowserViewModel : BaseViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private bool _hasError;

    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder), nameof(ShowEmptyState))]
    private bool _hasLoaded;

    // True once a load has populated no rows. Derived VMs read their own collection.
    protected abstract bool IsEmpty { get; }

    // Message shown when a successful load returned nothing. Override per view.
    public virtual string EmptyMessage => "No results found.";

    public bool ShowPlaceholder => !IsLoading && !HasError && !HasLoaded;
    public bool ShowEmptyState  => !IsLoading && !HasError && HasLoaded && IsEmpty;

    // Reset load state at the start of a (re)load.
    protected void BeginLoad()
    {
        HasError  = false;
        IsLoading = true;
    }

    // Mark a successful load; call after the collection has been populated.
    protected void EndLoad()
    {
        HasLoaded = true;
        IsLoading = false;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    // Surface a load failure uniformly.
    protected void SetError(string message)
    {
        HasError      = true;
        ErrorMessage  = message;
        StatusMessage = message;
        IsLoading     = false;
    }
}
