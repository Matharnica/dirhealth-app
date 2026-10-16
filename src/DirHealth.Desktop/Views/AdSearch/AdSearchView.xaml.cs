using System.Windows.Controls;
using System.Windows.Input;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.AdSearch;

public partial class AdSearchView : UserControl
{
    public AdSearchView() => InitializeComponent();

    // Double-click / Enter on a result → TableViewModel.Activated → OpenDetailAsync (S15-2).

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is AdSearchViewModel vm && vm.ShowDetail)
        {
            vm.CloseDetail();
            e.Handled = true;
        }
    }
}
