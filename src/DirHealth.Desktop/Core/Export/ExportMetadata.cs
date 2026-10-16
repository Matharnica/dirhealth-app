using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DirHealth.Desktop.Core.Export;

/// <summary>Who/what/where of an export, filled by the caller (S14-5).</summary>
public record ExportContext(string Domain, string DomainController, string User);

/// <summary>
/// Audit side-car for every CSV (S14-5): "&lt;name&gt;.meta.txt" next to the export with domain,
/// queried DC, user, UTC time, view, filter, row count, DirHealth version and the SHA-256 of
/// the CSV. Kept out of the CSV itself — comment lines there break imports.
/// </summary>
public static class ExportMetadata
{
    public static string MetaPathFor(string exportPath) =>
        Path.Combine(Path.GetDirectoryName(exportPath) ?? "",
                     Path.GetFileNameWithoutExtension(exportPath) + ".meta.txt");

    public static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>The metadata fields without the hash — also the XLSX "Info" sheet (ADR 0003: a file
    /// cannot contain its own SHA-256, so that stays in the .meta.txt).</summary>
    public static List<(string Key, string Value)> Fields(ExportContext ctx, string view, string filter, int rowCount,
                                                          string fileName, DateTime utcNow, string version) =>
    [
        ("File",              fileName),
        ("Rows",              rowCount.ToString(CultureInfo.InvariantCulture)),
        ("View",              view),
        ("Filter",            string.IsNullOrWhiteSpace(filter) ? "(none)" : filter),
        ("Domain",            ctx.Domain),
        ("Domain controller", ctx.DomainController),
        ("Run by",            ctx.User),
        ("Created (UTC)",     utcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        ("Tool",              $"DirHealth {version}"),
    ];

    public static string Build(ExportContext ctx, string view, string filter, int rowCount,
                               string fileName, string sha256, DateTime utcNow, string version)
    {
        var sb = new StringBuilder();
        void Line(string key, string value) => sb.Append(key.PadRight(18)).Append(": ").Append(value).Append("\r\n");
        var fields = Fields(ctx, view, filter, rowCount, fileName, utcNow, version);
        Line(fields[0].Key, fields[0].Value);
        Line("SHA-256", sha256);
        foreach (var (k, v) in fields.Skip(1)) Line(k, v);
        return sb.ToString();
    }

    /// <summary>Hashes the finished export and writes the side-car. Returns its path.</summary>
    public static string WriteFor(string exportPath, ExportContext ctx, string view, string filter, int rowCount)
    {
        var text = Build(ctx, view, filter, rowCount, Path.GetFileName(exportPath),
                         Sha256OfFile(exportPath), DateTime.UtcNow, AppVersion);
        var metaPath = MetaPathFor(exportPath);
        File.WriteAllText(metaPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return metaPath;
    }
}
