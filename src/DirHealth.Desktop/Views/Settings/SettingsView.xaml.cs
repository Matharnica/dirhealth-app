using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm && !string.IsNullOrEmpty(vm.Password))
                PasswordInput.Password = vm.Password;
        };
    }

    private void PasswordInput_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.Password = PasswordInput.Password;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
