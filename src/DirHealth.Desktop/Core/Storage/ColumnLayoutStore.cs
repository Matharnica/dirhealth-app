using System.IO;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Storage;

/// <summary>
/// Hidden grid columns per view (S20-1), in %APPDATA%\DirHealth\columns.json. A per-user convenience:
/// unreadable or unwritable files fall back to "all visible" — a lost layout costs a click, not data.
/// </summary>
public class ColumnLayoutStore
{
    private readonly string _path;
    private Dictionary<string, List<string>> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DirHealth", "columns.json");

    public ColumnLayoutStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        try
        {
            if (File.Exists(_path))
                _hidden = new(JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path)) ?? new(),
                              StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception) { _hidden = new(StringComparer.OrdinalIgnoreCase); }
    }

    public IReadOnlyCollection<string> HiddenFor(string view) =>
        _hidden.TryGetValue(view, out var l) ? l : [];

    public void SetHidden(string view, IEnumerable<string> headers)
    {
        var list = headers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) _hidden.Remove(view); else _hidden[view] = list;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_hidden, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* convenience only — the layout simply isn't remembered */ }
    }
}
