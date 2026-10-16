using System.Windows.Controls;

namespace DirHealth.Desktop.Views.OuBrowser;

public partial class OuBrowserView : UserControl
{
    // Row selection loads the OU counts via TableViewModel.SelectionChanged (S14-3).
    public OuBrowserView() => InitializeComponent();
}
