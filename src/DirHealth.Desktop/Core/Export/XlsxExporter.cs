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

    /// <summary>Writes the workbook. Returns the number of data rows.</summary>
    public int Export(IEnumerable<object> rows, IReadOnlyList<ITableColumn> columns, string filePath,
                      IReadOnlyList<(string Key, string Value)> info)
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

        var infoRows = info.Select(kv => new Dictionary<string, object?> { ["Key"] = kv.Key, ["Value"] = CleanText(kv.Value) }).ToList();

        var sheets = new Dictionary<string, object> { ["Data"] = data, ["Info"] = infoRows };
        // Explicit, not defaults — defaults may change with 2.x (ADR 0003)
        var config = new OpenXmlConfiguration { FreezeRowCount = 1, AutoFilter = true };
        MiniExcel.SaveAs(filePath, sheets, printHeader: true, excelType: ExcelType.XLSX, configuration: config, overwriteFile: true);
        return count;
    }
}
