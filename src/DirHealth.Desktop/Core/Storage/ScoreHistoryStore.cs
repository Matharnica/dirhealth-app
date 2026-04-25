using System.IO;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Storage;

public record ScoreEntry(DateTime Timestamp, int Score);

public class ScoreHistoryStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private List<ScoreEntry> _entries = [];

    public IReadOnlyList<ScoreEntry> Entries => _entries;

    public ScoreHistoryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DirHealth");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.json");
        Load();
    }

    public void Add(int score)
    {
        _entries.Add(new ScoreEntry(DateTime.UtcNow, score));
        if (_entries.Count > 90)
            _entries = _entries.TakeLast(90).ToList();
        Save();
    }

    public int? PreviousScore() =>
        _entries.Count >= 2 ? _entries[^2].Score : null;

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
