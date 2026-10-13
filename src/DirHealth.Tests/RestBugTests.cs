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

    [Fact]
    public void A9_StylesXaml_TakesSeverityColoursFromPalette()
    {
        var styles = File.ReadAllText(Path.Combine(DesktopDir(), "Resources", "Styles.xaml"));
        foreach (var level in new[] { "Critical", "High", "Medium", "Low" })
            Assert.Contains($"Member=\"core:SeverityPalette.{level}Color\"", styles);
        // the old hard-coded app values must not come back
        Assert.DoesNotContain("<Color x:Key=\"HighSeverityColor\">", styles);
    }

    [Fact]
    public void A9_PdfExporter_TakesSeverityColoursFromPalette()
    {
        var pdf = File.ReadAllText(Path.Combine(DesktopDir(), "Core", "Export", "PdfExporter.cs"));
        Assert.Contains("ColHigh        = FromRgb(SeverityPalette.HighRgb)", pdf);
        Assert.DoesNotContain("XColor.FromArgb(217, 119,   6)", pdf);
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
