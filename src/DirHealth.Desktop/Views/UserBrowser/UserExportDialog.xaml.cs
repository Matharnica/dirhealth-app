using System.Globalization;
using System.Windows;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.UserBrowser;

public partial class UserExportDialog : Window
{
    public UserExportOptions? Result { get; private set; }

    public UserExportDialog(int filteredCount, int totalCount)
    {
        InitializeComponent();
        FilteredRadio.Content = $"Filtered rows ({filteredCount.ToString("N0", CultureInfo.InvariantCulture)})";
        AllRadio.Content      = $"All users ({totalCount.ToString("N0", CultureInfo.InvariantCulture)})";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        Result = new UserExportOptions(
            FilteredOnly:       FilteredRadio.IsChecked == true,
            IncludeMail:        MailBox.IsChecked == true,
            IncludeManager:     ManagerBox.IsChecked == true,
            IncludeDescription: DescriptionBox.IsChecked == true);
        DialogResult = true;
    }
}
