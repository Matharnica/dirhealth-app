using System.Windows.Controls;
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
}
