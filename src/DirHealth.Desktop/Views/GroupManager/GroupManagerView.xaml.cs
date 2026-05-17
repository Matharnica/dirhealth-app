using System.Windows.Controls;
using System.Windows.Input;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.GroupManager;

public partial class GroupManagerView : UserControl
{
    public GroupManagerView() => InitializeComponent();

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is GroupManagerViewModel vm && ((ListBox)sender).SelectedItem is AdGroup g)
            _ = vm.SelectGroupAsync(g);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is GroupManagerViewModel vm && vm.SelectedGroup is not null)
        {
            GroupList.SelectedItem = null;
            vm.SelectedGroup = null;
            e.Handled = true;
        }
    }
}
