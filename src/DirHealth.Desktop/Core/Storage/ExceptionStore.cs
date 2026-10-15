using System.IO;
using System.Text.Json;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Storage;

/// <summary>One documented exception for one object of one rule (ADR 0001). All times UTC.</summary>
public record ObjectException(
    string   RuleId,
    string   ObjectKey,
    string   ObjectName,
    string   Reason,
    DateTime ExpiresUtc,
    string   CreatedBy,
    DateTime CreatedUtc)
{
    public bool IsActive(DateTime nowUtc) => ExpiresUtc > nowUtc;
}

/// <summary>The stable key of an object for a rule (ADR 0001) — one type per rule, no fallback chain.</summary>
public static class ExceptionKey
{
    /// <summary>SID for object rules, name for policy rules; null when the object cannot be keyed
    /// (e.g. no SID because the scan predates v2.10).</summary>
    public static string? For(FindingRule rule, AffectedObject obj)
    {
        if (rule.KeyedBySid)
            return string.IsNullOrEmpty(obj.Sid) ? null : obj.Sid.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(obj.Name) ? null : "name:" + obj.Name.Trim();
    }
}

/// <summary>
/// Persists per-object exceptions in %APPDATA%\DirHealth\exceptions.json (ADR 0001). A separate
/// file on purpose: v2.9 still reads acknowledged.json and must not be able to overwrite this.
/// Corrupt file → copied aside and reported; write errors are reported, never swallowed.
/// </summary>
public class ExceptionStore
{
    public const int  FileVersion     = 1;
    public const int  MaxDays         = 365;
    public const int  DefaultDays     = 90;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private List<ObjectException> _items = [];

    /// <summary>Set when loading found a damaged file (it was copied aside); null otherwise.</summary>
    public string? LoadError { get; private set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DirHealth", "exceptions.json");

    public ExceptionStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        Load();
    }

    public IReadOnlyList<ObjectException> All => _items;

    private record FileShape(int Version, List<ObjectException> Exceptions);

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Json)
                        ?? throw new JsonException("empty file");
            if (shape.Version > FileVersion)
                throw new JsonException($"file version {shape.Version} is newer than this DirHealth understands ({FileVersion})");
            _items = shape.Exceptions ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            var aside = Path.Combine(Path.GetDirectoryName(_path) ?? "",
                $"exceptions.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            try { File.Copy(_path, aside, overwrite: false); } catch { /* best effort; error still reported */ }
            _items    = [];
            LoadError = $"exceptions.json could not be read ({ex.Message}). A copy was kept as {Path.GetFileName(aside)}; starting with no exceptions.";
        }
    }

    /// <summary>Throws on write failure — the caller shows the message.</summary>
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new FileShape(FileVersion, _items), Json));
        File.Move(tmp, _path, overwrite: true);   // no half-written file on crash
    }

    /// <summary>Validation per ADR 0001. Returns null when valid, else the reason.</summary>
    public static string? Validate(string? reason, DateTime expiresUtc, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))        return "A reason is required.";
        if (expiresUtc <= nowUtc)                     return "The expiry date must be in the future.";
        if (expiresUtc > nowUtc.AddDays(MaxDays))     return $"An exception can last at most {MaxDays} days.";
        return null;
    }

    public ObjectException? Find(string ruleId, string key) =>
        _items.FirstOrDefault(e => e.RuleId == ruleId && string.Equals(e.ObjectKey, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds or replaces the exception for (rule, key). Throws ArgumentException when invalid.</summary>
    public ObjectException Add(string ruleId, string key, string objectName, string reason,
                               DateTime expiresUtc, string createdBy, DateTime nowUtc)
    {
        if (Validate(reason, expiresUtc, nowUtc) is { } error) throw new ArgumentException(error);
        var ex = new ObjectException(ruleId, key, objectName, reason.Trim(), expiresUtc, createdBy, nowUtc);
        _items.RemoveAll(e => e.RuleId == ruleId && string.Equals(e.ObjectKey, key, StringComparison.OrdinalIgnoreCase));
        _items.Add(ex);
        Save();
        return ex;
    }

    public bool Remove(string ruleId, string key)
    {
        int n = _items.RemoveAll(e => e.RuleId == ruleId && string.Equals(e.ObjectKey, key, StringComparison.OrdinalIgnoreCase));
        if (n > 0) Save();
        return n > 0;
    }

    /// <summary>State of an object for a rule at a point in time.</summary>
    public ExceptionState StateOf(FindingRule rule, AffectedObject obj, DateTime nowUtc)
    {
        var key = ExceptionKey.For(rule, obj);
        if (key is null || Find(rule.RuleId, key) is not { } ex) return ExceptionState.None;
        return ex.IsActive(nowUtc) ? ExceptionState.Active : ExceptionState.Expired;
    }

    /// <summary>
    /// Stamps every object of the findings with its exception state and marks a finding as
    /// handled (IsAcknowledged) when all its objects are actively excepted.
    /// </summary>
    public void Apply(IEnumerable<AdFinding> findings, DateTime nowUtc)
    {
        foreach (var f in findings)
        {
            if (f.Rule is not { } rule) continue;
            foreach (var o in f.Objects)
            {
                var key = ExceptionKey.For(rule, o);
                var ex  = key is null ? null : Find(rule.RuleId, key);
                o.Exception      = ex is null ? ExceptionState.None : ex.IsActive(nowUtc) ? ExceptionState.Active : ExceptionState.Expired;
                o.ExceptionLabel = ex is null ? ""
                    : ex.IsActive(nowUtc) ? $"until {ex.ExpiresUtc:yyyy-MM-dd} — {ex.Reason}"
                    : $"expired {ex.ExpiresUtc:yyyy-MM-dd} — {ex.Reason}";
            }
            f.IsAcknowledged = f.Objects.Count > 0 && f.Objects.All(o => o.IsExcepted);
        }
    }
}
