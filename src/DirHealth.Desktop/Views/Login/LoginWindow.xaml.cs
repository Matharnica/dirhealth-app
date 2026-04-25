using System.Windows;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.Login;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm        = vm;
        DataContext = vm;

        // Pre-fill password if remembered
        if (!string.IsNullOrEmpty(vm.Password))
            PasswordBox.Password = vm.Password;

        PasswordBox.PasswordChanged += (_, _) => _vm.Password = PasswordBox.Password;
    }
}
