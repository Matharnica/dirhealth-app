using DirHealth.Desktop.Core;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

// S14-6: rest bugs A6, A8, A9, A10.
public class RestBugTests
{
    // ── A6: sidebar badge counts open findings only ─────────────────────────
    [Fact]
    public void A6_CountOpen_IgnoresAcknowledged()
    {
        var findings = new List<AdFinding>
        {
            new() { Category = "A", IsAcknowledged = true },
            new() { Category = "B" },
            new() { Category = "C" },
        };
        Assert.Equal(2, FindingsViewModel.CountOpen(findings));
        Assert.Equal(0, FindingsViewModel.CountOpen([]));
    }

    // ── A8: selecting a finding loads its saved note ────────────────────────
    [Fact]
    public void A8_SelectingFindingDirectly_LoadsNote()
    {
        var vm = new FindingsViewModel();
        vm.SelectedFinding = new AdFinding { Category = "X", AcknowledgeNote = "accepted risk, ticket 42" };
        Assert.Equal("accepted risk, ticket 42", vm.AcknowledgeNote);

        vm.SelectedFinding = new AdFinding { Category = "Y" };
        Assert.Equal("", vm.AcknowledgeNote);   // no stale note from the previous selection
    }

    // ── A9: one colour source for app and PDF ────────────────────────────────
    [Theory]
    [InlineData(FindingSeverity.Critical, "#DC2626")]
    [InlineData(FindingSeverity.High,     "#D97706")]
    [InlineData(FindingSeverity.Medium,   "#CA8A04")]
    [InlineData(FindingSeverity.Low,      "#6B7280")]
    public void A9_Palette_HasOneValuePerSeverity(FindingSeverity s, string hex)
    {
        Assert.Equal(hex, SeverityPalette.Hex(s));
    }

    // A9 at runtime (follow-up 2026-10-16): the PDF's severity colours are the palette values.
    // The app side is checked with the real resources in ViewSmokeTests.SeverityBrushes_ComeFromPalette.
    [Theory]
    [InlineData(FindingSeverity.Critical)] [InlineData(FindingSeverity.High)]
    [InlineData(FindingSeverity.Medium)]   [InlineData(FindingSeverity.Low)]
    public void A9_PdfSeverityAccent_IsPaletteColour(FindingSeverity s)
    {
        var (_, border, _) = new PdfPageBuilder().SeverityStyle(s);
        Assert.Equal(SeverityPalette.Rgb(s), (border.R, border.G, border.B));
    }

    // Invariant "never hard-code a severity hex in a view": no palette value appears as a literal in
    // Views/ or Resources/. (Not checked: Core/ — e.g. the PDF label grey is #6B7280 by coincidence.)
    [Fact]
    public void A9_NoSeverityHexInViewsOrResources()
    {
        var hexes = Enum.GetValues<FindingSeverity>().Select(SeverityPalette.Hex).ToList();
        var hits = new[] { "Views", "Resources" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(DesktopDir(), d), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".xaml") || f.EndsWith(".cs"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.EndsWith("SeverityPalette.cs"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i, l)))
            .Where(x => hexes.Any(h => x.l.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
        Assert.True(hits.Count == 0, string.Join(", ", hits));
    }

    // ── A10: no "Never" in a CSV date column ────────────────────────────────
    [Fact]
    public void A10_InactiveUsersCsv_NeverLoggedOn_IsEmptyDate()
    {
        var path = Path.GetTempFileName();
        try
        {
            new CsvExporter(",").ExportInactiveUsers([new AdUser { SamAccountName = "x", DistinguishedName = "CN=x,DC=c" }], path);
            Assert.DoesNotContain("Never", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static string DesktopDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DirHealth.Desktop")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "DirHealth.Desktop");
    }
}
