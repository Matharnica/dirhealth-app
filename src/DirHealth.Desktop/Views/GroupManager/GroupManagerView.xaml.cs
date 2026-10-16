using System.Windows.Controls;
using System.Windows.Input;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.GroupManager;

public partial class GroupManagerView : UserControl
{
    public GroupManagerView() => InitializeComponent();

    // Row selection loads the group detail via TableViewModel.SelectionChanged (S14-2).

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is GroupManagerViewModel vm && vm.SelectedGroup is not null)
        {
            vm.Table.SelectedItem = null;
            vm.SelectedGroup = null;
            e.Handled = true;
        }
    }
}
