using System.Globalization;

namespace DirHealth.Desktop.Core.Export;

/// <summary>
/// One column of a shared table (S14-1): what the grid shows and what CSV/XLSX exports
/// write, defined once per view. Non-generic face for the WPF control and the exporters.
/// </summary>
public interface ITableColumn
{
    /// <summary>Grid header and export header.</summary>
    string  Header       { get; }
    /// <summary>Property path the grid binds/sorts on; null = export-only column.</summary>
    string? BindingPath  { get; }
    /// <summary>.NET format for the grid cell (e.g. "yyyy-MM-dd").</summary>
    string? StringFormat { get; }
    /// <summary>Grid width in px; 0 = size to content.</summary>
    double  Width        { get; }
    bool    Monospace    { get; }
    /// <summary>Grid shows a bool as Yes/No (export stays TRUE/FALSE).</summary>
    bool    YesNo        { get; }
    /// <summary>Resource key of an IValueConverter that colours the cell text (grid only).</summary>
    string? ForegroundConverterKey { get; }
    /// <summary>Cell value as text for export (not yet formula-escaped).</summary>
    string  ExportValue(object row);
}

public sealed class TableColumn<T> : ITableColumn where T : class
{
    private readonly Func<T, object?> _value;
    private readonly string?          _exportFormat;

    /// <param name="value">Value accessor used for export.</param>
    /// <param name="bindingPath">Grid binding path; null hides the column in the grid.</param>
    /// <param name="format">Display format in the grid; also used for export unless exportFormat is set.</param>
    public TableColumn(string header, Func<T, object?> value, string? bindingPath = null,
                       string? format = null, double width = 0, bool monospace = false,
                       string? exportFormat = null, bool yesNo = false,
                       string? foregroundConverterKey = null)
    {
        YesNo         = yesNo;
        ForegroundConverterKey = foregroundConverterKey;
        Header        = header;
        _value        = value;
        BindingPath   = bindingPath;
        StringFormat  = format;
        Width         = width;
        Monospace     = monospace;
        _exportFormat = exportFormat ?? format;
    }

    public string  Header       { get; }
    public string? BindingPath  { get; }
    public string? StringFormat { get; }
    public double  Width        { get; }
    public bool    Monospace    { get; }
    public bool    YesNo        { get; }
    public string? ForegroundConverterKey { get; }

    public string ExportValue(object row) => FormatValue(_value((T)row), _exportFormat);

    /// <summary>
    /// Export text for a value. Null → empty (never "Never" in a date column — that breaks
    /// imports, see research doc section E). Bool → TRUE/FALSE. Dates ISO, invariant culture.
    /// </summary>
    public static string FormatValue(object? value, string? format = null) => value switch
    {
        null                => "",
        string s            => s,
        bool b              => b ? "TRUE" : "FALSE",
        DateTime d          => d.ToString(format ?? "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable f      => f.ToString(format, CultureInfo.InvariantCulture),
        _                   => value.ToString() ?? "",
    };
}
