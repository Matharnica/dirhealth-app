using System.IO;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Storage;

public record AcknowledgeEntry(string Note, DateTime AcknowledgedAt);

/// <summary>
/// Read-only view of the pre-v2.10 category acknowledgements (acknowledged.json). Since ADR 0001
/// they no longer hide findings; their notes are only shown as a hint on the finding card. The
/// file is never written again, so a downgrade to v2.9 finds it as it left it.
/// </summary>
public class AcknowledgeStore
{
    private readonly Dictionary<string, AcknowledgeEntry> _entries = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DirHealth", "acknowledged.json");

    public AcknowledgeStore(string? path = null)
    {
        var p = path ?? DefaultPath;
        try
        {
            if (File.Exists(p))
                _entries = JsonSerializer.Deserialize<Dictionary<string, AcknowledgeEntry>>(File.ReadAllText(p)) ?? new();
        }
        catch (Exception) { _entries = new(); } // a legacy hint is optional; nothing depends on it
    }

    public bool HasLegacy(string category) => _entries.ContainsKey(category);

    /// <summary>Note of the old category acknowledgement, or null when there was none.</summary>
    public AcknowledgeEntry? Legacy(string category) => _entries.TryGetValue(category, out var e) ? e : null;
}
