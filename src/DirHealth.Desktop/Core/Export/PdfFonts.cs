using System.Collections.Concurrent;
using System.IO;
using PdfSharp.Fonts;

namespace DirHealth.Desktop.Core.Export;

/// <summary>
/// Font resolver for the PDFs: reads the Windows system fonts the report design uses (Segoe UI for text,
/// Consolas for code) from %WINDIR%\Fonts. PDFsharp's built-in Windows resolver only knows a handful of
/// classic families (Arial, Courier New …), so without this resolver "Segoe UI" fails. A family or face
/// that is missing on the machine falls back to Arial / Courier New, so an export never fails over a font.
/// Fonts are embedded as subsets, as before with Arial.
/// </summary>
internal sealed class PdfFonts : IFontResolver
{
    private static readonly object Gate = new();

    /// <summary>Installs the resolver once per process (PDFsharp allows one global resolver).</summary>
    internal static bool Install()
    {
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new PdfFonts();
            return true;
        }
    }

    private static readonly string FontDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    // family → (regular file, bold file); first existing entry wins
    private static readonly Dictionary<string, (string Regular, string Bold)[]> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Segoe UI"]    = [("segoeui.ttf", "segoeuib.ttf"), ("arial.ttf", "arialbd.ttf")],
        ["Consolas"]    = [("consola.ttf", "consolab.ttf"), ("cour.ttf", "courbd.ttf")],
        ["Arial"]       = [("arial.ttf", "arialbd.ttf")],
        ["Courier New"] = [("cour.ttf", "courbd.ttf")],
    };

    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var candidates = Families.TryGetValue(familyName, out var c) ? c : Families["Arial"];
        foreach (var (regular, bold) in candidates)
        {
            if (isBold && File.Exists(Path.Combine(FontDir, bold))) return new FontResolverInfo(bold, false, isItalic);
            if (File.Exists(Path.Combine(FontDir, regular))) return new FontResolverInfo(regular, isBold, isItalic);
        }
        return null;
    }

    public byte[]? GetFont(string faceName) =>
        _cache.GetOrAdd(faceName, f => File.ReadAllBytes(Path.Combine(FontDir, f)));
}
