using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Storage;

public class ScanCache
{
    public List<AdFinding> Findings       { get; set; } = [];
    public int  ComplianceScore           { get; set; }
    public string LastScanTime            { get; set; } = "";
    public int  FindingsCount             { get; set; }
    public int  InactiveUsersCount        { get; set; }
    public int  PasswordIssuesCount       { get; set; }
    public int  GroupIssuesCount          { get; set; }
}

public class ScanCacheStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public ScanCacheStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DirHealth");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "last-scan.json");
    }

    public ScanCache? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<ScanCache>(File.ReadAllText(_path), _jsonOptions);
        }
        catch { return null; }
    }

    public ScanCache? LoadPrevious()
    {
        try
        {
            var prev = _path.Replace("last-scan.json", "previous-scan.json");
            if (!File.Exists(prev)) return null;
            return JsonSerializer.Deserialize<ScanCache>(File.ReadAllText(prev), _jsonOptions);
        }
        catch { return null; }
    }

    public void Save(ScanCache cache)
    {
        try
        {
            var prev = _path.Replace("last-scan.json", "previous-scan.json");
            if (File.Exists(_path)) File.Copy(_path, prev, overwrite: true);
            File.WriteAllText(_path, JsonSerializer.Serialize(cache, _jsonOptions));
        }
        catch { }
    }
}
