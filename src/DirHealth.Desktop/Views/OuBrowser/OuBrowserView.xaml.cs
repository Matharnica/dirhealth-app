using System.Windows.Controls;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.OuBrowser;

public partial class OuBrowserView : UserControl
{
    public OuBrowserView() => InitializeComponent();

    private void OUList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is OuBrowserViewModel vm && ((ListBox)sender).SelectedItem is AdOU ou)
            _ = vm.SelectOUCommand.ExecuteAsync(ou);
    }
}
