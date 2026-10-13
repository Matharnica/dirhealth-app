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

    public static string Build(ExportContext ctx, string view, string filter, int rowCount,
                               string fileName, string sha256, DateTime utcNow, string version)
    {
        var sb = new StringBuilder();
        void Line(string key, string value) => sb.Append(key.PadRight(18)).Append(": ").Append(value).Append("\r\n");
        Line("File",              fileName);
        Line("SHA-256",           sha256);
        Line("Rows",              rowCount.ToString(CultureInfo.InvariantCulture));
        Line("View",              view);
        Line("Filter",            string.IsNullOrWhiteSpace(filter) ? "(none)" : filter);
        Line("Domain",            ctx.Domain);
        Line("Domain controller", ctx.DomainController);
        Line("Run by",            ctx.User);
        Line("Created (UTC)",     utcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        Line("Tool",              $"DirHealth {version}");
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
