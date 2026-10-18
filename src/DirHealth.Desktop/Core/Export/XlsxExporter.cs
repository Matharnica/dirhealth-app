using System.IO;
using System.Text;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;

namespace DirHealth.Desktop.Core.Export;

/// <summary>
/// XLSX export (S18-5, ADR 0003) via MiniExcel 1.46.0. Sheet "Data": header frozen, autofilter on.
/// Sheet "Info": the S14-5 metadata without the hash (the .meta.txt next to the file holds the SHA-256).
///
/// Formula safety comes from the cell type: strings are written as text cells (t="str"), never as
/// formulas — so values are NOT passed through CsvExporter.SafeField here (the apostrophe would show
/// up in the cell). Numbers and dates stay typed so Excel can sort and filter them.
/// </summary>
public class XlsxExporter
{
    /// <summary>Excel's limit for one cell.</summary>
    public const int MaxCellLength = 32_767;
    private const string Truncated = " …[truncated]";

    /// <summary>Removes characters XML 1.0 cannot carry (control chars from AD attributes) and caps the length.</summary>
    public static string CleanText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            if (ch is '\t' or '\n' or '\r' || ch >= 0x20 && ch != '￾' && ch != '￿')
                sb.Append(ch);
        var s = sb.ToString();
        return s.Length <= MaxCellLength ? s : s[..(MaxCellLength - Truncated.Length)] + Truncated;
    }

    /// <summary>Unique, non-empty header names (dictionary keys must differ).</summary>
    private static List<string> Headers(IReadOnlyList<ITableColumn> columns)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var c in columns)
        {
            var h = string.IsNullOrWhiteSpace(c.Header) ? "Column" : CleanText(c.Header);
            var name = h;
            for (int n = 2; !seen.Add(name); n++) name = $"{h} ({n})";
            list.Add(name);
        }
        return list;
    }

    /// <summary>Header row + typed, cleaned cells for one sheet. Returns the records and the data row count.</summary>
    private static (List<Dictionary<string, object?>> Records, int Count) Records(IEnumerable<object> rows, IReadOnlyList<ITableColumn> columns)
    {
        var headers = Headers(columns);
        var data = new List<Dictionary<string, object?>>();
        int count = 0;
        foreach (var row in rows)
        {
            count++;
            var d = new Dictionary<string, object?>(headers.Count);
            for (int i = 0; i < columns.Count; i++)
            {
                var v = columns[i].ExportTyped(row);
                d[headers[i]] = v is string s ? CleanText(s) : v;
            }
            data.Add(d);
        }
        // MiniExcel derives the header row from the first record; keep it for an empty table too
        if (data.Count == 0) data.Add(headers.ToDictionary(h => h, _ => (object?)null));
        return (data, count);
    }

    private static List<Dictionary<string, object?>> InfoRecords(IReadOnlyList<(string Key, string Value)> info) =>
        info.Select(kv => new Dictionary<string, object?> { ["Key"] = kv.Key, ["Value"] = CleanText(kv.Value) }).ToList();

    private static void Save(string filePath, Dictionary<string, object> sheets)
    {
        // Explicit, not defaults — defaults may change with 2.x (ADR 0003). Applies to every sheet.
        var config = new OpenXmlConfiguration { FreezeRowCount = 1, AutoFilter = true };
        MiniExcel.SaveAs(filePath, sheets, printHeader: true, excelType: ExcelType.XLSX, configuration: config, overwriteFile: true);
    }

    /// <summary>Writes the workbook. Returns the number of data rows.</summary>
    public int Export(IEnumerable<object> rows, IReadOnlyList<ITableColumn> columns, string filePath,
                      IReadOnlyList<(string Key, string Value)> info)
    {
        var (data, count) = Records(rows, columns);
        Save(filePath, new Dictionary<string, object> { ["Data"] = data, ["Info"] = InfoRecords(info) });
        return count;
    }

    // ── S22-6: several sheets in one workbook ────────────────────────────────

    /// <summary>Excel's limit for a sheet name.</summary>
    public const int MaxSheetName = 31;
    private static readonly char[] InvalidSheetChars = ['[', ']', ':', '*', '?', '/', '\\'];

    /// <summary>
    /// Excel-safe, unique sheet names in input order (S22-6): forbidden characters <c>[]:*?/\</c> replaced,
    /// control characters removed, no leading/trailing apostrophe, at most 31 characters, unique ignoring
    /// case (a clash gets " (2)", " (3)" … within the 31), "History" avoided (reserved by Excel).
    /// </summary>
    public static List<string> SheetNames(IEnumerable<string> wanted)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "History" };
        var result = new List<string>();
        foreach (var w in wanted)
        {
            var chars = (w ?? "").Where(ch => ch >= 0x20).Select(ch => InvalidSheetChars.Contains(ch) ? '_' : ch).ToArray();
            var baseName = new string(chars).Trim().Trim('\'').Trim();
            if (baseName.Length == 0) baseName = "Sheet";
            if (baseName.Length > MaxSheetName) baseName = baseName[..MaxSheetName].TrimEnd();
            var name = baseName;
            for (int n = 2; !used.Add(name); n++)
            {
                var suffix = $" ({n})";
                name = (baseName.Length + suffix.Length > MaxSheetName ? baseName[..(MaxSheetName - suffix.Length)].TrimEnd() : baseName) + suffix;
            }
            result.Add(name);
        }
        return result;
    }

    /// <summary>Writes one workbook with the given sheets in order. Names must come from <see cref="SheetNames"/>.</summary>
    public void ExportSheets(IReadOnlyList<XlsxSheet> sheets, string filePath)
    {
        var dict = new Dictionary<string, object>();
        foreach (var sh in sheets)
        {
            if (sh.Name.Length > MaxSheetName || sh.Name.IndexOfAny(InvalidSheetChars) >= 0)
                throw new ArgumentException($"Invalid sheet name: {sh.Name}");
            dict.Add(sh.Name, sh.InfoRows is { } info ? InfoRecords(info) : Records(sh.Rows, sh.Columns).Records);
        }
        Save(filePath, dict);
    }
}

/// <summary>One sheet of a multi-sheet workbook (S22-6): table rows + columns, or key/value info rows.</summary>
public sealed record XlsxSheet(string Name, IEnumerable<object> Rows, IReadOnlyList<ITableColumn> Columns)
{
    public IReadOnlyList<(string Key, string Value)>? InfoRows { get; init; }

    /// <summary>A "Key | Value" sheet like the S14-5 info sheet (no hash — ADR 0003).</summary>
    public static XlsxSheet Info(string name, IReadOnlyList<(string Key, string Value)> info) =>
        new(name, [], []) { InfoRows = info };
}
