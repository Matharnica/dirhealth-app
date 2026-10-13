using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.UserBrowser;

public partial class UserBrowserView : UserControl
{
    public UserBrowserView() => InitializeComponent();

    // Row selection opens the detail via TableViewModel.SelectionChanged (S14-1).

    private void ExportUsers_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UserBrowserViewModel vm) return;
        var dlg = new UserExportDialog(vm.Table.FilteredCount, vm.Table.TotalCount)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() == true && dlg.Result is { } options)
            vm.ExportFullUsers(options);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is UserBrowserViewModel vm && vm.ShowDetail)
        {
            vm.Table.SelectedItem = null;
            vm.ShowDetail = false;
            e.Handled = true;
        }
    }
}
