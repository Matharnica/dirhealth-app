using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels.Tables;
using DirHealth.Desktop.Views.Shared;
using Xunit;

namespace DirHealth.Tests;

/// <summary>
/// Runtime XAML smoke test (S14). The compiler checks XAML syntax, not whether a view can
/// actually be built: a wrong Style TargetType, a missing StaticResource or a broken
/// ControlTemplate only throws at InitializeComponent / first layout. This instantiates every
/// view with the app's resources and lays it out once.
///
/// What it does NOT check: binding paths (WPF only traces those), visual appearance,
/// behaviour after a real AD load. Needs the WindowsDesktop runtime → runs in CI only.
/// </summary>
public class ViewSmokeTests
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
            var asm = typeof(DirHealth.Desktop.App).Assembly;
            foreach (var (key, type) in new[]
            {
                ("BoolToVisibility",    "BoolToVisibilityConverter"),
                ("InverseBool",         "InverseBoolConverter"),
                ("StringToVisibility",  "StringToVisibilityConverter"),
                ("NonZeroToVisibility", "NonZeroToVisibilityConverter"),
                ("DaysToExpiryColor",   "DaysToExpiryColorConverter"),
                ("IsNegativeConverter", "IsNegativeConverter"),
                ("EqualityConverter",   "EqualityConverter"),
                ("RelativeScanTime",    "RelativeScanTimeConverter"),
                ("ScoreColor",          "ScoreColorConverter"),
            })
                app.Resources[key] = Activator.CreateInstance(asm.GetType($"DirHealth.Desktop.{type}", throwOnError: true)!);
        }
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
        OnSta(() =>
        {
            EnsureResources();
            var type = typeof(DirHealth.Desktop.App).Assembly.GetType(typeName, throwOnError: true)!;
            var view = (FrameworkElement)Activator.CreateInstance(type)!;
            Layout(view);
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
