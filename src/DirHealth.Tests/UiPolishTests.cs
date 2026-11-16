using System.Globalization;
using System.Xml.Linq;
using DirHealth.Desktop;
using DirHealth.Desktop.Core;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

/// <summary>
/// UI polish for the first impression (review 2026-11-16, package 4): dashboard order and tiles,
/// severity badge contrast, Password Report error state, readable rule names, and two layout
/// guards read from the XAML (sidebar scrolls, title/subtitle not in one Grid cell).
/// What this does NOT check: how anything looks when rendered (no Windows rendering in these
/// tests — ViewSmokeTests only builds the views), and real AD loads.
/// </summary>
public class UiPolishTests
{
    // ── G3: dashboard lists the most severe findings first ───────────────────
    [Fact]
    public void Dashboard_SortsBySeverity_ThenByCount()
    {
        var findings = new List<AdFinding>
        {
            new() { Title = "Inactive users",   Severity = FindingSeverity.Low,      Count = 300 },
            new() { Title = "Weak policy",      Severity = FindingSeverity.Medium,   Count = 1 },
            new() { Title = "DCSync rights",    Severity = FindingSeverity.Critical, Count = 2 },
            new() { Title = "Kerberoastable",   Severity = FindingSeverity.High,     Count = 4 },
            new() { Title = "Unconstrained",    Severity = FindingSeverity.Critical, Count = 9 },
            new() { Title = "Pwd never expires",Severity = FindingSeverity.Medium,   Count = 40 },
        };

        var sorted = DashboardViewModel.SortBySeverity(findings).Select(f => f.Title).ToList();

        Assert.Equal(
            new[] { "Unconstrained", "DCSync rights", "Kerberoastable", "Pwd never expires", "Weak policy", "Inactive users" },
            sorted);
        Assert.Equal("Inactive users", findings[0].Title);   // the scan order itself is untouched (exports, cache)
    }

    [Fact]
    public void Dashboard_SeverityTiles_CountOpenFindingsOnly()
    {
        var findings = new List<AdFinding>
        {
            new() { Severity = FindingSeverity.Critical },
            new() { Severity = FindingSeverity.Critical, IsAcknowledged = true },   // fully excepted
            new() { Severity = FindingSeverity.High },
            new() { Severity = FindingSeverity.Low },
        };
        Assert.Equal(1, DashboardViewModel.CountOpen(findings, FindingSeverity.Critical));
        Assert.Equal(1, DashboardViewModel.CountOpen(findings, FindingSeverity.High));
        Assert.Equal(0, DashboardViewModel.CountOpen(findings, FindingSeverity.Medium));
    }

    // ── G2: the "points if fixed" list is capped ─────────────────────────────
    [Theory]
    [InlineData(0, "")]
    [InlineData(5, "")]
    [InlineData(6, "+ 1 more rule in Findings")]
    [InlineData(12, "+ 7 more rules in Findings")]
    public void Dashboard_BreakdownMoreText(int total, string expected)
    {
        Assert.Equal(5, DashboardViewModel.BreakdownRows);
        Assert.Equal(expected, DashboardViewModel.MoreText(total));
    }

    // ── Q6: Password Report never shows the green "nothing expiring" after a failure ──
    [Fact]
    public async Task PasswordReport_LoadFailure_ShowsErrorNotEmpty()
    {
        var vm = new PasswordReportViewModel(null!, _ => throw new InvalidOperationException("LDAP server unavailable"));

        await vm.LoadAsync();

        Assert.True(vm.HasError);
        Assert.Contains("LDAP server unavailable", vm.ErrorMessage);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task PasswordReport_BeforeLoad_NotEmpty_AfterEmptyLoad_Empty()
    {
        var vm = new PasswordReportViewModel(null!, _ => Task.FromResult(new List<AdUser>()));
        Assert.False(vm.ShowEmptyState);   // nothing loaded yet — no "✓"

        await vm.LoadAsync();

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.HasError);
        Assert.Contains("30 days", vm.EmptyMessage);
    }

    [Fact]
    public async Task PasswordReport_LoadWithUsers_NeitherEmptyNorError()
    {
        var vm = new PasswordReportViewModel(null!, _ => Task.FromResult(new List<AdUser> { new() { SamAccountName = "a" } }));
        await vm.LoadAsync();
        Assert.Equal(1, vm.UserCount);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task PasswordReport_RetryAfterError_ClearsError()
    {
        bool fail = true;
        var vm = new PasswordReportViewModel(null!, _ => fail
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(new List<AdUser>()));
        await vm.LoadAsync();
        fail = false;
        await vm.LoadAsync();
        Assert.False(vm.HasError);
        Assert.True(vm.ShowEmptyState);
    }

    // ── Q1/Q2: severity badge colours from the palette, readable and distinct ──
    public static IEnumerable<object[]> Severities() =>
        Enum.GetValues<FindingSeverity>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Severities))]
    public void SeverityBadge_TextContrast_AtLeastWcagAA(FindingSeverity s)
    {
        var ratio = Contrast(SeverityPalette.BadgeTextRgb(s), SeverityPalette.BadgeBackgroundRgb(s));
        Assert.True(ratio >= 4.5, $"{s}: {ratio:F2}:1 < 4.5:1");
    }

    [Fact]
    public void SeverityBadge_AllFourDistinct()
    {
        var all = Enum.GetValues<FindingSeverity>();
        Assert.Equal(4, all.Length);
        Assert.Equal(4, all.Select(SeverityPalette.BadgeBackgroundRgb).Distinct().Count());
        Assert.Equal(4, all.Select(SeverityPalette.BadgeTextRgb).Distinct().Count());
        Assert.Equal(4, all.Select(SeverityPalette.Rgb).Distinct().Count());
    }

    // Accents are stripes/markers (non-text): WCAG 1.4.11 asks 3:1 against the surface they sit on.
    // Surface #1A1D27 = SurfaceColor in Resources/Themes/Dark.xaml.
    [Theory]
    [MemberData(nameof(Severities))]
    public void SeverityAccent_NonTextContrast_OnSurface(FindingSeverity s)
    {
        var ratio = Contrast(SeverityPalette.Rgb(s), (0x1A, 0x1D, 0x27));
        Assert.True(ratio >= 3.0, $"{s}: {ratio:F2}:1 < 3:1");
    }

    [Fact]
    public void Contrast_Formula_MatchesKnownValues()
    {
        // Self-test of the helper: black/white = 21:1, equal colours = 1:1
        Assert.Equal(21.0, Contrast((0, 0, 0), (255, 255, 255)), 2);
        Assert.Equal(1.0, Contrast((10, 20, 30), (10, 20, 30)), 6);
    }

    // A9: severity colour is chosen only by the shared styles in Styles.xaml, never per view
    [Fact]
    public void Views_HaveNoOwnSeverityColourTriggers()
    {
        var hits = Directory.EnumerateFiles(Path.Combine(DesktopDir(), "Views"), "*.xaml", SearchOption.AllDirectories)
            .Append(Path.Combine(DesktopDir(), "MainWindow.xaml"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i, l)))
            .Where(x => x.l.Contains("DataTrigger Binding=\"{Binding Severity}\""))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
        Assert.True(hits.Count == 0, string.Join(", ", hits));
    }

    // ── Item 9: readable rule names in the UI, one splitting rule (ScanDiffCalculator) ──
    [Theory]
    [InlineData("PasswordNeverExpires", "Password Never Expires")]
    [InlineData("PreWindows2000Access", "Pre Windows 2000 Access")]
    [InlineData("", "")]
    public void ReadableCategoryConverter_SplitsKey(string key, string expected)
    {
        var c = new ReadableCategoryConverter();
        Assert.Equal(expected, c.Convert(key, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ReadableCategoryConverter_NonString_IsEmpty()
    {
        Assert.Equal("", new ReadableCategoryConverter().Convert(null!, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    // ── G1: the sidebar scrolls, Settings stays outside the scroll area ──────
    [Fact]
    public void Sidebar_NavigationScrolls_SettingsPinned()
    {
        var doc = XDocument.Load(Path.Combine(DesktopDir(), "MainWindow.xaml"));
        XElement NavButton(string command) => doc.Descendants()
            .Single(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Command") == $"{{Binding {command}}}");
        bool InScrollViewer(XElement e) => e.Ancestors().Any(a => a.Name.LocalName == "ScrollViewer");

        Assert.True(InScrollViewer(NavButton("ShowDashboardCommand")));
        Assert.True(InScrollViewer(NavButton("ShowDataQualityCommand")));
        Assert.False(InScrollViewer(NavButton("ShowSettingsCommand")));
    }

    // ── Q4: no title/subtitle pair stacked into the same Grid cell ───────────
    // Catches the pattern that overlapped in five views: two TextBlocks, same Grid row/column,
    // one pushed down with VerticalAlignment="Bottom".
    [Fact]
    public void Views_NoTextBlocksOverlappingInOneGridCell()
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(DesktopDir(), "Views"), "*.xaml", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(file);
            foreach (var grid in doc.Descendants().Where(e => e.Name.LocalName == "Grid"))
            {
                var cells = grid.Elements()
                    .Where(e => e.Name.LocalName == "TextBlock")
                    .GroupBy(e => ((string?)e.Attribute("Grid.Row") ?? "0") + "/" + ((string?)e.Attribute("Grid.Column") ?? "0"));
                foreach (var cell in cells.Where(c => c.Count() > 1))
                    if (cell.Any(t => (string?)t.Attribute("VerticalAlignment") == "Bottom"))
                        hits.Add($"{Path.GetFileName(file)} cell {cell.Key}");
            }
        }
        Assert.True(hits.Count == 0, string.Join(", ", hits));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>WCAG 2.x contrast ratio of two sRGB colours (1 … 21).</summary>
    private static double Contrast((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((byte R, byte G, byte B) c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static string DesktopDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DirHealth.Desktop")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "DirHealth.Desktop");
    }
}
