using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.Views.Shared;

/// <summary>Shared sortable table with counter, export menu and copy menu (S14-1).</summary>
public partial class DataTable : UserControl
{
    private static readonly FontFamily Mono = new("Consolas");
    private static readonly IValueConverter YesNo = new YesNoConverter();

    public DataTable()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TableViewModel old) old.DisplayOrder = null;
        if (e.NewValue is not TableViewModel vm) return;

        BuildColumns(vm.Columns);
        // Export "filtered" in the order the user sees after clicking a header
        vm.DisplayOrder = () => TableGrid.Items.Cast<object>().ToList();
    }

    private void BuildColumns(IReadOnlyList<ITableColumn> columns)
    {
        TableGrid.Columns.Clear();
        var visible = columns.Where(c => c.BindingPath is not null).ToList();
        for (int i = 0; i < visible.Count; i++)
        {
            var c = visible[i];
            var binding = new Binding(c.BindingPath) { Mode = BindingMode.OneWay };
            if (c.YesNo)                    binding.Converter    = YesNo;
            else if (c.StringFormat is { } f) binding.StringFormat = "{0:" + f + "}";

            var col = new DataGridTextColumn
            {
                Header         = c.Header,
                Binding        = binding,
                SortMemberPath = c.BindingPath,
                Width          = c.Width > 0 ? new DataGridLength(c.Width)
                               : i == visible.Count - 1 ? new DataGridLength(1, DataGridLengthUnitType.Star)
                               : DataGridLength.Auto,
                MinWidth       = 60,
            };
            if (c.Monospace) col.FontFamily = Mono;
            TableGrid.Columns.Add(col);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExportButton.ContextMenu is null) return;
        ExportButton.ContextMenu.PlacementTarget = ExportButton;
        ExportButton.ContextMenu.Placement       = PlacementMode.Bottom;
        ExportButton.ContextMenu.IsOpen          = true;
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only rows — a double-click on the header or the resize gripper is not an activation
        var row = ItemsControl.ContainerFromElement(TableGrid, (DependencyObject)e.OriginalSource) as DataGridRow;
        if (row?.Item is { } item && DataContext is TableViewModel vm)
        {
            vm.Activate(item);
            e.Handled = true;
        }
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TableGrid.SelectedItem is { } item && DataContext is TableViewModel vm)
        {
            vm.Activate(item);
            e.Handled = true; // DataGrid would otherwise move to the next row
        }
    }

    private sealed class YesNoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is bool b ? (b ? "Yes" : "No") : "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }
}
