using System.Windows.Controls;
using System.Windows.Input;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.UserBrowser;

public partial class UserBrowserView : UserControl
{
    public UserBrowserView() => InitializeComponent();

    private void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is UserBrowserViewModel vm && ((ListBox)sender).SelectedItem is AdUser user)
            _ = vm.SelectUserAsync(user);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is UserBrowserViewModel vm && vm.ShowDetail)
        {
            UserList.SelectedItem = null;
            vm.ShowDetail = false;
            e.Handled = true;
        }
    }
}
