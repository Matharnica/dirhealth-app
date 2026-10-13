using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DirHealth.Desktop.ViewModels.Tables;

/// <summary>
/// ObservableCollection that can swap its whole content with one Reset notification.
/// Re-filtering 10,000 rows item by item raises 10,000 CollectionChanged events, each
/// re-sorting the grid's view (S14-1).
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
