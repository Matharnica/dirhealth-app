using System.IO;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Storage;

public record AcknowledgeEntry(string Note, DateTime AcknowledgedAt);

public class AcknowledgeStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private Dictionary<string, AcknowledgeEntry> _entries = new();

    public AcknowledgeStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DirHealth");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "acknowledged.json");
        Load();
    }

    public bool IsAcknowledged(string category) => _entries.ContainsKey(category);

    public string GetNote(string category) =>
        _entries.TryGetValue(category, out var e) ? e.Note : "";

    public void Acknowledge(string category, string note)
    {
        _entries[category] = new AcknowledgeEntry(note, DateTime.UtcNow);
        Save();
    }

    public void Unacknowledge(string category)
    {
        _entries.Remove(category);
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            _entries = JsonSerializer.Deserialize<Dictionary<string, AcknowledgeEntry>>(json) ?? new();
        }
        catch (Exception) { _entries = new(); }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_entries, _jsonOptions)); }
        catch (Exception) { }
    }
}
