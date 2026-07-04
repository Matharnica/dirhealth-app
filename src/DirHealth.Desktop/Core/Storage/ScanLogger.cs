using System.IO;

namespace DirHealth.Desktop.Core.Storage;

public record ScanLogEntry(string Query, long ElapsedMs, int Count, string? Error);

// Structured per-scan diagnostics log: %APPDATA%\DirHealth\scan.log.
// Purely additive observability — never throws into the scan path.
public class ScanLogger
{
    private const int MaxLines = 1500;   // rolling cap (~20–30 recent scan runs)

    private readonly string _path;

    public ScanLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DirHealth");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "scan.log");
    }

    // For tests / custom locations.
    public ScanLogger(string path) => _path = path;

    public void WriteRun(IEnumerable<ScanLogEntry> entries, DateTime timestamp)
    {
        try
        {
            var lines = new List<string> { $"=== scan {timestamp:yyyy-MM-dd HH:mm:ss} ===" };
            foreach (var e in entries.OrderBy(e => e.Query))
            {
                var result = e.Error is null ? $"{e.Count} result(s)" : $"ERROR: {e.Error}";
                lines.Add($"  {e.Query,-32} {e.ElapsedMs,6} ms  {result}");
            }

            File.AppendAllLines(_path, lines);
            Truncate();
        }
        catch { /* observability must never break the scan */ }
    }

    private void Truncate()
    {
        try
        {
            var all = File.ReadAllLines(_path);
            if (all.Length <= MaxLines) return;
            File.WriteAllLines(_path, all[^MaxLines..]);
        }
        catch { }
    }
}
