using System.Windows.Media;
using DirHealth.Desktop.Core;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;
using DirHealth.Desktop.Views.Shared;
using Xunit;
using Xunit.Abstractions;

namespace DirHealth.Tests;

/// <summary>
/// Runtime XAML smoke test (S14). The compiler checks XAML syntax, not whether a view can
/// actually be built: a wrong Style TargetType, a missing StaticResource or a broken
/// ControlTemplate only throws at InitializeComponent / first layout. This instantiates every
/// view with the app's resources and lays it out once.
///
/// Item templates (2026-10-30): the views are built without a ViewModel, so every list is empty
/// and no ItemTemplate would ever be instantiated. After the first layout the test therefore walks
/// the visual tree, gives every ItemsControl with an ItemTemplate/ItemTemplateSelector and no items
/// one placeholder item (Selectors such as ComboBox also select it, so the selection box renders the
/// template) and lays out again — repeated, so templates nested in templates are reached too. Each
/// template that really produced visuals is counted; <see cref="ItemTemplates_AreInstantiated"/>
/// keeps that count from silently dropping to zero. Proof: a missing StaticResource in
/// DataQualityView's item template turned this test red (run RUN_RED), the reverted state is
/// green (same PR, following run). At the time of writing the views contain no ContentTemplate and no
/// DataGridTemplateColumn, so those need no extra handling.
///
/// What it does NOT check: binding paths (WPF only traces those; the placeholder has no
/// properties, so every binding inside a template fails silently), visual appearance, behaviour
/// after a real AD load, and anything inside a Collapsed element — WPF does not measure collapsed
/// elements, so their templates (e.g. panels shown only for a selection or a state) stay
/// unchecked; the test lists those as "not reached" in its output. ComboBox drop-down lists
/// (popup) and ItemContainerStyles of lists without an ItemTemplate are not instantiated either.
/// Needs the WindowsDesktop runtime → runs in CI only.
/// </summary>
public class ViewSmokeTests(ITestOutputHelper output)
{
    private static readonly object Gate = new();

    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    // Application resources as App.xaml defines them, without App's startup/crash dialogs.
    private static void EnsureResources()
    {
        lock (Gate)
        {
            if (Application.Current is not null) return;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var name in new[] { "Themes/Dark.xaml", "Styles.xaml", "Strings.xaml", "Icons.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/DirHealth;component/Resources/{name}"),
                });
            // Converters exactly as App.xaml declares them — read from the file, so a new
            // converter cannot be forgotten here.
            var asm = typeof(DirHealth.Desktop.App).Assembly;
            foreach (var (type, key) in AppXamlConverters())
                app.Resources[key] = Activator.CreateInstance(asm.GetType($"DirHealth.Desktop.{type}", throwOnError: true)!);
        }
    }

    private static IEnumerable<(string Type, string Key)> AppXamlConverters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DirHealth.Desktop")))
            dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "DirHealth.Desktop", "App.xaml"));
        return System.Text.RegularExpressions.Regex.Matches(xaml, @"<local:(\w+)\s+x:Key=""(\w+)""")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value));
    }

    [Fact]
    public void AppXamlConverters_AreFound()
    {
        Assert.True(AppXamlConverters().Count() >= 10);
    }

    private static void Layout(FrameworkElement e)
    {
        e.Measure(new Size(1400, 900));
        e.Arrange(new Rect(0, 0, 1400, 900));
        e.UpdateLayout();
    }

    public static IEnumerable<object[]> ViewTypes() =>
        typeof(DirHealth.Desktop.App).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("DirHealth.Desktop.Views", StringComparison.Ordinal) == true
                        && typeof(UserControl).IsAssignableFrom(t)
                        && !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName)
            .Select(t => new object[] { t.FullName! });

    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void EveryView_BuildsAndLaysOut(string typeName)
    {
        TemplateProbe probe = default!;
        OnSta(() => probe = BuildView(typeName));
        output.WriteLine($"{typeName}: {probe.Instantiated} item template(s) instantiated");
        foreach (var miss in probe.NotReached)
            output.WriteLine($"  not reached: {miss}");
    }

    [Fact]
    public void ItemTemplates_AreInstantiated()
    {
        // Denominator for the template part of the theory above: without it, a change that stops
        // the placeholder from reaching the templates would leave every view vacuously green.
        var perView = new List<string>();
        var total = 0;
        OnSta(() =>
        {
            foreach (var row in ViewTypes())
            {
                var name = (string)row[0];
                var probe = BuildView(name);
                total += probe.Instantiated;
                perView.Add($"{name.Split('.')[^1]}={probe.Instantiated}"
                            + (probe.NotReached.Count > 0 ? $" (not reached: {string.Join(", ", probe.NotReached)})" : ""));
            }
        });
        output.WriteLine($"item templates instantiated: {total}");
        foreach (var line in perView) output.WriteLine("  " + line);
        Assert.True(total >= 8, $"only {total} item templates instantiated: {string.Join("; ", perView)}");
    }

    private sealed record TemplateProbe(int Instantiated, IReadOnlyList<string> NotReached);

    private static TemplateProbe BuildView(string typeName)
    {
        EnsureResources();
        var type = typeof(DirHealth.Desktop.App).Assembly.GetType(typeName, throwOnError: true)!;
        var view = (FrameworkElement)Activator.CreateInstance(type)!;
        Layout(view);
        return FillItemTemplates(view);
    }

    // Gives every templated, empty ItemsControl one placeholder item and lays out again, so the
    // template is instantiated once. A missing StaticResource or broken Style inside it throws here.
    private static TemplateProbe FillItemTemplates(FrameworkElement view)
    {
        var seen = new Dictionary<ItemsControl, string>();
        for (var round = 0; round < 10; round++)
        {
            var fresh = Descendants(view).OfType<ItemsControl>()
                .Where(ic => !seen.ContainsKey(ic) && (ic.ItemTemplate is not null || ic.ItemTemplateSelector is not null))
                .ToList();
            if (fresh.Count == 0) break;
            foreach (var ic in fresh)
            {
                seen[ic] = Describe(ic);
                if (ic.Items.Count == 0) ic.ItemsSource = new[] { new object() };
                if (ic is System.Windows.Controls.Primitives.Selector { SelectedIndex: < 0 } sel && sel.Items.Count > 0)
                    sel.SelectedIndex = 0;
            }
            Layout(view);
        }
        var reached = seen.Keys.Where(TemplateRealized).ToHashSet();
        var notReached = seen.Where(kv => !reached.Contains(kv.Key))
            .Select(kv => kv.Value + (InCollapsedBranch(kv.Key) ? " [collapsed]" : "")).ToList();
        return new TemplateProbe(reached.Count, notReached);
    }

    // The template produced visuals: some ContentPresenter inside the control (item container or,
    // for a ComboBox, the selection box) uses it and has children.
    private static bool TemplateRealized(ItemsControl ic) =>
        Descendants(ic).OfType<ContentPresenter>().Any(cp =>
            ((ic.ItemTemplate is not null && ReferenceEquals(cp.ContentTemplate, ic.ItemTemplate))
             || (ic.ItemTemplateSelector is not null && ReferenceEquals(cp.ContentTemplateSelector, ic.ItemTemplateSelector)))
            && VisualTreeHelper.GetChildrenCount(cp) > 0);

    // IsVisible needs a PresentationSource (window), which the test has none of — read Visibility up the tree instead.
    private static bool InCollapsedBranch(DependencyObject node)
    {
        for (DependencyObject? n = node; n is not null; n = VisualTreeHelper.GetParent(n))
            if (n is UIElement { Visibility: not Visibility.Visible }) return true;
        return false;
    }

    private static string Describe(ItemsControl ic)
    {
        var path = BindingOperations.GetBindingExpression(ic, ItemsControl.ItemsSourceProperty)?.ParentBinding.Path?.Path;
        var label = !string.IsNullOrEmpty(ic.Name) ? ic.Name : path ?? "(unbound)";
        return $"{ic.GetType().Name}[{label}]";
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var n = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                yield return child;
                stack.Push(child);
            }
        }
    }

    // A9 at runtime (follow-up 2026-10-16): the brushes views use are the palette colours
    [Fact]
    public void SeverityBrushes_ComeFromPalette()
    {
        OnSta(() =>
        {
            EnsureResources();
            foreach (var (key, colour) in new[]
            {
                ("CriticalSeverityBrush", SeverityPalette.CriticalColor), ("HighSeverityBrush", SeverityPalette.HighColor),
                ("MediumSeverityBrush",   SeverityPalette.MediumColor),   ("LowSeverityBrush",  SeverityPalette.LowColor),
            })
                Assert.Equal(colour, ((SolidColorBrush)Application.Current.FindResource(key)).Color);
        });
    }

    [Fact]
    public void ViewTypes_FindsTheViews()
    {
        // Denominator: an empty MemberData would make the theory above pass vacuously
        Assert.True(ViewTypes().Count() >= 20, $"only {ViewTypes().Count()} views found");
    }

    [Fact]
    public void DataTable_BuildsColumns_AndExportFollowsHeaderSort()
    {
        OnSta(() =>
        {
            EnsureResources();
            var table = new TableViewModel<AdUser>("Users", UserColumns.Grid);
            table.SetRows(
            [
                new AdUser { DisplayName = "Bravo",   SamAccountName = "b" },
                new AdUser { DisplayName = "Alpha",   SamAccountName = "a" },
                new AdUser { DisplayName = "Charlie", SamAccountName = "c" },
            ]);
            var control = new DataTable { DataContext = table };
            Layout(control);

            var grid = (DataGrid)control.FindName("TableGrid");
            Assert.Equal(UserColumns.Grid.Count(c => c.BindingPath is not null), grid.Columns.Count);

            // What a header click does: sort the grid's view
            grid.Items.SortDescriptions.Add(new SortDescription(nameof(AdUser.DisplayName), ListSortDirection.Descending));
            var order = table.RowsForFilteredExport().Cast<AdUser>().Select(u => u.DisplayName).ToList();
            Assert.Equal(["Charlie", "Bravo", "Alpha"], order);
        });
    }

    [Fact]
    public void UserExportDialog_Builds()
    {
        OnSta(() =>
        {
            EnsureResources();
            var dlg = new DirHealth.Desktop.Views.UserBrowser.UserExportDialog(37, 1204);
            Layout((FrameworkElement)dlg.Content);
            dlg.Close();
        });
    }
}
