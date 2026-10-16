using System.Collections;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.Export;

namespace DirHealth.Desktop.ViewModels.Tables;

/// <summary>
/// Shared table state for every list view (S14-1): rows after the local filter, the
/// "37 of 1,204" counter, CSV export of the filtered or the full set (with a .meta.txt
/// side-car, S14-5) and "Copy DN / SID / sAMAccountName" for the selected row.
/// Owned by a view's VM (composition); the <c>DataTable</c> control binds to it.
/// </summary>
public abstract partial class TableViewModel : ObservableObject
{
    /// <summary>Save dialog; replaced in tests. Args: suggested file name. Returns path or null.</summary>
    public static Func<string, string?> PickSavePath { get; set; } = DefaultPickSavePath;

    /// <summary>Clipboard writer; replaced in tests.</summary>
    public static Action<string> SetClipboardText { get; set; } = text => System.Windows.Clipboard.SetText(text);

    protected TableViewModel(string viewName, IReadOnlyList<ITableColumn> columns)
    {
        ViewName = viewName;
        Columns  = columns;
    }

    /// <summary>Name used in the export file name and the metadata ("Users", "Computers" …).</summary>
    public string ViewName { get; }

    public IReadOnlyList<ITableColumn> Columns { get; }

    /// <summary>Rows after the local filter — the grid's ItemsSource.</summary>
    public abstract IEnumerable Rows { get; }

    public abstract int TotalCount    { get; }
    public abstract int FilteredCount { get; }

    /// <summary>Human-readable description of the active filter for the metadata file.</summary>
    public Func<string>? FilterDescription { get; set; }

    /// <summary>Who/where for the metadata file; null → metadata says "unknown".</summary>
    public Func<ExportContext>? ContextProvider { get; set; }

    /// <summary>Set by the grid control: filtered rows in the order currently displayed (after
    /// header-click sorting). Null → collection order.</summary>
    public Func<IEnumerable<object>>? DisplayOrder { get; set; }

    /// <summary>Raised on double-click / Enter on a row (S15-2 opens the detail).</summary>
    public event Action<object>? Activated;

    /// <summary>Raised when the selected row changes.</summary>
    public event Action<object?>? SelectionChanged;

    [ObservableProperty] private object? _selectedItem;
    [ObservableProperty] private string  _statusMessage = "";

    partial void OnSelectedItemChanged(object? value)
    {
        CopyDnCommand.NotifyCanExecuteChanged();
        CopySidCommand.NotifyCanExecuteChanged();
        CopySamCommand.NotifyCanExecuteChanged();
        SelectionChanged?.Invoke(value);
    }

    public string CountLabel => FilteredCount == TotalCount
        ? $"{N(TotalCount)} {(TotalCount == 1 ? "item" : "items")}"
        : $"{N(FilteredCount)} of {N(TotalCount)}";

    public string ExportFilteredLabel => $"CSV ({N(FilteredCount)} filtered)";
    public string ExportAllLabel      => $"CSV (all {N(TotalCount)})";

    public bool HasRows    => FilteredCount > 0;
    public bool HasAnyRows => TotalCount > 0;

    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    public void Activate(object row) => Activated?.Invoke(row);

    protected void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(ExportFilteredLabel));
        OnPropertyChanged(nameof(ExportAllLabel));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasAnyRows));
        ExportFilteredCommand.NotifyCanExecuteChanged();
        ExportAllCommand.NotifyCanExecuteChanged();
    }

    // ── Export ────────────────────────────────────────────────────────────────

    protected abstract IEnumerable<object> AllRowObjects();
    protected abstract IEnumerable<object> FilteredRowObjects();

    /// <summary>Filtered rows in display order — what "CSV (n filtered)" writes.</summary>
    public IReadOnlyList<object> RowsForFilteredExport() =>
        (DisplayOrder?.Invoke() ?? FilteredRowObjects()).ToList();

    public IReadOnlyList<object> RowsForFullExport() => AllRowObjects().ToList();

    [RelayCommand(CanExecute = nameof(HasRows))]
    private void ExportFiltered() =>
        Export(RowsForFilteredExport(), FilterDescription?.Invoke() ?? "", "filtered");

    [RelayCommand(CanExecute = nameof(HasAnyRows))]
    private void ExportAll() => Export(RowsForFullExport(), "(none — all rows)", "all");

    /// <summary>Writes CSV + metadata. Returns the CSV path, or null when cancelled/failed.</summary>
    public string? Export(IReadOnlyList<object> rows, string filter, string scope)
    {
        var suggested = $"DirHealth-{ViewName.Replace(' ', '-')}-{scope}-{DateTime.Now:yyyyMMdd-HHmm}";
        var path = PickSavePath(suggested);
        if (path is null) return null;
        try
        {
            int written = new CsvExporter().ExportTable(rows, Columns, path);
            var ctx = SafeContext();
            ExportMetadata.WriteFor(path, ctx, ViewName, filter, written);
            StatusMessage = $"Exported {N(written)} rows: {Path.GetFileName(path)} (+ .meta.txt)";
            return path;
        }
        catch (Exception ex)
        {
            // File locks / network shares must not reach the global crash handler
            StatusMessage = $"Export failed: {ex.Message}";
            return null;
        }
    }

    private ExportContext SafeContext()
    {
        try { return ContextProvider?.Invoke() ?? Unknown; }
        catch { return Unknown; }
    }

    private static readonly ExportContext Unknown = new("unknown", "unknown", Environment.UserName);

    private static string? DefaultPickSavePath(string suggestedName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = suggestedName,
            DefaultExt = ".csv",
            Filter     = "CSV files|*.csv",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    // ── Copy ──────────────────────────────────────────────────────────────────

    public bool CanCopyDn  => HasDnAccessor;
    public bool CanCopySid => HasSidAccessor;
    public bool CanCopySam => HasSamAccessor;

    protected abstract bool HasDnAccessor  { get; }
    protected abstract bool HasSidAccessor { get; }
    protected abstract bool HasSamAccessor { get; }

    protected abstract string? DnOf(object row);
    protected abstract string? SidOf(object row);
    protected abstract string? SamOf(object row);

    private bool CanCopyDnNow()  => SelectedItem is not null && HasDnAccessor;
    private bool CanCopySidNow() => SelectedItem is not null && HasSidAccessor;
    private bool CanCopySamNow() => SelectedItem is not null && HasSamAccessor;

    [RelayCommand(CanExecute = nameof(CanCopyDnNow))]
    private void CopyDn()  => Copy(SelectedItem is { } r ? DnOf(r)  : null, "DN");

    [RelayCommand(CanExecute = nameof(CanCopySidNow))]
    private void CopySid() => Copy(SelectedItem is { } r ? SidOf(r) : null, "SID");

    [RelayCommand(CanExecute = nameof(CanCopySamNow))]
    private void CopySam() => Copy(SelectedItem is { } r ? SamOf(r) : null, "sAMAccountName");

    private void Copy(string? value, string what)
    {
        if (string.IsNullOrEmpty(value)) { StatusMessage = $"No {what} for this row."; return; }
        try
        {
            SetClipboardText(value);
            StatusMessage = $"Copied {what}.";
        }
        catch (Exception ex) { StatusMessage = $"Copy failed: {ex.Message}"; } // clipboard can be locked by another app
    }
}

/// <summary>Typed table: holds the full row set and the local filter.</summary>
public sealed class TableViewModel<T> : TableViewModel where T : class
{
    private readonly BulkObservableCollection<T> _rows = new();
    private List<T>        _all = [];
    private Func<T, bool>? _filter;

    public TableViewModel(string viewName, IReadOnlyList<TableColumn<T>> columns)
        : base(viewName, columns.Cast<ITableColumn>().ToList()) { }

    public Func<T, string?>? Dn  { get; init; }
    public Func<T, string?>? Sid { get; init; }
    public Func<T, string?>? Sam { get; init; }

    public override IEnumerable Rows => _rows;
    public IReadOnlyList<T> FilteredRows => _rows;
    public IReadOnlyList<T> AllRows      => _all;

    public override int TotalCount    => _all.Count;
    public override int FilteredCount => _rows.Count;

    public T? Selected => SelectedItem as T;

    /// <summary>Replaces the full row set and re-applies the current filter.</summary>
    public void SetRows(IEnumerable<T> rows)
    {
        _all = rows.ToList();
        Refresh();
    }

    /// <summary>Sets the local filter (null = all rows) and re-applies it.</summary>
    public void SetFilter(Func<T, bool>? filter)
    {
        _filter = filter;
        Refresh();
    }

    /// <summary>Re-applies the filter, e.g. after a filter input changed.</summary>
    public void Refresh()
    {
        var keep = SelectedItem;
        _rows.ReplaceAll(_filter is null ? _all : _all.Where(_filter));
        if (keep is T k && !_rows.Contains(k)) SelectedItem = null;
        NotifyCountsChanged();
    }

    public void Clear() => SetRows([]);

    protected override IEnumerable<object> AllRowObjects()      => _all;
    protected override IEnumerable<object> FilteredRowObjects() => _rows;

    protected override bool HasDnAccessor  => Dn  is not null;
    protected override bool HasSidAccessor => Sid is not null;
    protected override bool HasSamAccessor => Sam is not null;

    protected override string? DnOf(object row)  => row is T t ? Dn?.Invoke(t)  : null;
    protected override string? SidOf(object row) => row is T t ? Sid?.Invoke(t) : null;
    protected override string? SamOf(object row) => row is T t ? Sam?.Invoke(t) : null;
}
