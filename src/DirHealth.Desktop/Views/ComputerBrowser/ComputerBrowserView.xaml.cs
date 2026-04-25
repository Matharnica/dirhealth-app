using System.Windows.Controls;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.ComputerBrowser;

public partial class ComputerBrowserView : UserControl
{
    public ComputerBrowserView() => InitializeComponent();

    private void ComputerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ComputerBrowserViewModel vm && ((ListBox)sender).SelectedItem is AdComputer computer)
            _ = vm.SelectComputerAsync(computer);
    }
}
