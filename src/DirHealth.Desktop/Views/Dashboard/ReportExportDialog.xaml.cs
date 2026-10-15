using System.Windows;
using DirHealth.Desktop.Core.Export.Reports;

namespace DirHealth.Desktop.Views.Dashboard;

public partial class ReportExportDialog : Window
{
    public ReportOptions? Result { get; private set; }

    public ReportExportDialog() => InitializeComponent();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        Result = new ReportOptions(SummaryRadio.IsChecked == true ? ReportKind.Summary : ReportKind.Full,
                                   RedactBox.IsChecked == true);
        DialogResult = true;
    }
}
