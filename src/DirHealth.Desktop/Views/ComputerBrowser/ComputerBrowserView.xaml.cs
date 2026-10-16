using System.Windows.Controls;
using System.Windows.Input;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.ComputerBrowser;

public partial class ComputerBrowserView : UserControl
{
    public ComputerBrowserView() => InitializeComponent();

    // Row selection opens the detail via TableViewModel.SelectionChanged (S14-2).

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is ComputerBrowserViewModel vm && vm.ShowDetail)
        {
            vm.Table.SelectedItem = null;
            vm.ShowDetail = false;
            e.Handled = true;
        }
    }
}
