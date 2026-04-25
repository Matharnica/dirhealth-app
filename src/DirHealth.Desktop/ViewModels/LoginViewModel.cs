using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly AdConnector _connector;

    [ObservableProperty] private string _domain   = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private bool   _rememberCredentials;
    [ObservableProperty] private bool   _isConnecting;

    partial void OnErrorMessageChanged(string value) => HasError = !string.IsNullOrEmpty(value);

    public string Password { get; set; } = "";

    public event Action? LoginSucceeded;
    public event Action? LoginCancelled;

    public LoginViewModel(AdConnector connector)
    {
        _connector = connector;

        // Auto-detect domain
        try { Domain = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName; }
        catch { }

        // Load remembered credentials
        var saved = CredentialStore.Load();
        if (saved != null)
        {
            Domain               = saved.Domain;
            Username             = saved.Username;
            Password             = saved.Password;
            RememberCredentials  = true;
        }
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Please enter a username.";
            return;
        }

        IsConnecting = true;
        ErrorMessage = "";
        try
        {
            _connector.Domain   = string.IsNullOrWhiteSpace(Domain) ? null : Domain;
            _connector.Username = Username;
            _connector.Password = Password;

            var isAdmin = await Task.Run(() => _connector.IsDomainAdmin());
            if (!isAdmin)
            {
                ErrorMessage = $"{Domain}\\{Username} is not a member of Domain Admins.";
                _connector.Username = null;
                _connector.Password = null;
                return;
            }

            if (RememberCredentials)
                CredentialStore.Save(Domain, Username, Password);
            else
                CredentialStore.Clear();

            LoginSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            _connector.Username = null;
            _connector.Password = null;
        }
        finally { IsConnecting = false; }
    }

    [RelayCommand]
    public void Cancel() => LoginCancelled?.Invoke();
}
