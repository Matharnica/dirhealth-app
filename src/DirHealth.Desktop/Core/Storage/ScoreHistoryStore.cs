using System.IO;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Storage;

/// <summary>FormulaVersion: 1 = v2.9 and earlier (missing in old files), 2 = ADR 0002.</summary>
public record ScoreEntry(DateTime Timestamp, int Score, int FormulaVersion = 1);

public class ScoreHistoryStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private List<ScoreEntry> _entries = [];

    public IReadOnlyList<ScoreEntry> Entries => _entries;

    public ScoreHistoryStore(string? path = null)
    {
        if (path is null)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DirHealth");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "history.json");
        }
        _path = path;
        Load();
    }

    public void Add(int score, int formulaVersion = 1)
    {
        _entries.Add(new ScoreEntry(DateTime.UtcNow, score, formulaVersion));
        if (_entries.Count > 90)
            _entries = _entries.TakeLast(90).ToList();
        Save();
    }

    /// <summary>
    /// Score of the most recent entry, if it was computed with the same formula. Call BEFORE Add.
    /// (Before S17 this read the second-to-last entry before adding, i.e. the scan before the
    /// previous one.) Across a formula change there is nothing to compare — returns null.
    /// </summary>
    public int? PreviousComparableScore(int formulaVersion) =>
        _entries.Count >= 1 && _entries[^1].FormulaVersion == formulaVersion ? _entries[^1].Score : null;

    /// <summary>True when the last two entries can be compared (same formula).</summary>
    public static bool Comparable(IReadOnlyList<ScoreEntry> e) =>
        e.Count >= 2 && e[^1].FormulaVersion == e[^2].FormulaVersion;

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            _entries = JsonSerializer.Deserialize<List<ScoreEntry>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception) { _entries = []; }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_entries, _jsonOptions)); }
        catch (Exception) { }
    }
}
