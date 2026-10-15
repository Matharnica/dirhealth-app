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
        if (e.OldValue is TableViewModel old)
        {
            old.DisplayOrder   = null;
            old.ColumnsChanged -= ApplyColumnVisibility;
        }
        if (e.NewValue is not TableViewModel vm) return;

        BuildColumns(vm.Columns);
        vm.ColumnsChanged += ApplyColumnVisibility;
        ApplyColumnVisibility();
        AttachHeaderMenu();
        if (vm.MutedPath is { } muted && TryFindResource("DataTableRow") is Style rowBase)
        {
            // Greyed, not hidden (S17-2)
            var rowStyle = new Style(typeof(DataGridRow), rowBase);
            var trigger  = new DataTrigger { Binding = new Binding(muted), Value = true };
            trigger.Setters.Add(new Setter(OpacityProperty, 0.45));
            rowStyle.Triggers.Add(trigger);
            TableGrid.RowStyle = rowStyle;
        }
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
            Style? element = null;
            if (c.ForegroundConverterKey is { } key && TryFindResource(key) is IValueConverter conv)
            {
                element ??= new Style(typeof(TextBlock));
                element.Setters.Add(new Setter(TextBlock.ForegroundProperty,
                    new Binding(c.BindingPath) { Mode = BindingMode.OneWay, Converter = conv }));
            }
            if (c.ToolTipPath is { } tip)
            {
                element ??= new Style(typeof(TextBlock));
                element.Setters.Add(new Setter(ToolTipProperty, new Binding(tip) { Mode = BindingMode.OneWay }));
            }
            if (element is not null) col.ElementStyle = element;
            TableGrid.Columns.Add(col);
        }
    }

    // ── S20-1: right-click on a column header shows/hides columns ────────────
    private void ApplyColumnVisibility()
    {
        if (DataContext is not TableViewModel vm) return;
        foreach (var col in TableGrid.Columns)
            col.Visibility = vm.IsColumnHidden(col.Header?.ToString() ?? "") ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AttachHeaderMenu()
    {
        if (TryFindResource("DataTableHeader") is not Style baseStyle) return;
        var menu = new ContextMenu();
        menu.Opened += (_, _) => FillHeaderMenu(menu);
        var style = new Style(typeof(DataGridColumnHeader), baseStyle);
        style.Setters.Add(new Setter(ContextMenuProperty, menu));
        TableGrid.ColumnHeaderStyle = style;
    }

    private void FillHeaderMenu(ContextMenu menu)
    {
        if (DataContext is not TableViewModel vm) return;
        menu.Items.Clear();
        foreach (var col in TableGrid.Columns)
        {
            var header = col.Header?.ToString() ?? "";
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = !vm.IsColumnHidden(header), StaysOpenOnClick = true };
            item.Click += (_, _) => vm.SetColumnHidden(header, !item.IsChecked);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = vm.HiddenLabel, IsEnabled = false });
        var reset = new MenuItem { Header = "Reset to default" };
        reset.Click += (_, _) => vm.ResetColumns();
        menu.Items.Add(reset);
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
