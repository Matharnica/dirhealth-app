using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.Core.Theme;
using DirHealth.Desktop.ViewModels;
using DirHealth.Desktop.Views.Login;

namespace DirHealth.Desktop;

public partial class App : Application
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DirHealth", "dirhealth.log");

    public App()
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            var full = ex.Exception.ToString();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.WriteAllText(_logPath, "DISPATCHER:\n" + full);
            } catch { }
            MessageBox.Show(
                $"{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n" +
                $"Full log saved to:\n{_logPath}",
                "DirHealth Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            File.WriteAllText(_logPath, "APPDOMAIN:\n" + ex.ExceptionObject);
            MessageBox.Show(ex.ExceptionObject.ToString(), "DirHealth Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    private static void Log(string msg)
    {
        try { File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Log("OnStartup begin");
        ThemeManager.LoadSavedTheme();
        Log("Theme loaded");

        var settings = LoadSettings();
        var http     = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var apiBase  = settings.GetValueOrDefault("LicenseApi", "https://license.dirhealth.app");

        var splash = new SplashWindow();
        splash.Show();
        splash.Close();
        Log("Splash closed");

        try
        {
            Log("Launching main window");
            LaunchMainWindow(http, apiBase);
            Log("Main window launched");
        }
        catch (Exception ex)
        {
            Log($"CRASH: {ex}");
            MessageBox.Show($"Startup failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private bool ShowLoginIfNeeded(AdConnector connector)
    {
        var saved = CredentialStore.Load();
        if (saved != null)
        {
            connector.Domain   = saved.Domain;
            connector.Username = saved.Username;
            connector.Password = saved.Password;
        }

        if (connector.IsDomainAdmin())
        {
            Log("Admin check passed (current context)");
            return true;
        }

        Log("Admin check failed — showing login window");
        var loginVm     = new LoginViewModel(connector);
        var loginWindow = new LoginWindow(loginVm);
        bool success    = false;

        loginVm.LoginSucceeded += () => { success = true; loginWindow.Close(); };
        loginVm.LoginCancelled += () => loginWindow.Close();
        loginWindow.ShowDialog();

        Log($"Login window closed, success={success}");
        return success;
    }

    private void LaunchMainWindow(HttpClient http, string apiBase)
    {
        var connector        = new AdConnector();

        if (!ShowLoginIfNeeded(connector))
        {
            Log("Login cancelled — shutting down");
            Shutdown(0);
            return;
        }
        var scanner          = new Core.AD.AdScanner(connector);
        var wmi              = new Core.AD.AdWmiClient();
        var searcher         = new Core.AD.AdSearcher(connector);
        var dashboard        = new DashboardViewModel(scanner);
        var findings         = new FindingsViewModel();
        var appSettings      = new SettingsViewModel(connector);
        var adSearch         = new AdSearchViewModel(searcher);
        var computerDetailVm = new ComputerDetailViewModel(wmi);
        var computerBrowser  = new ComputerBrowserViewModel(scanner, computerDetailVm);
        var userDetailVm     = new UserDetailViewModel(scanner);
        var userBrowser      = new UserBrowserViewModel(scanner, userDetailVm);
        var ouBrowser        = new OuBrowserViewModel(scanner);
        var groupManager     = new GroupManagerViewModel(scanner);
        var passwordReport   = new PasswordReportViewModel(scanner);
        var domainAdmins     = new DomainAdminsViewModel(scanner);
        var mainVm           = new MainViewModel(dashboard, findings, appSettings,
                                                 adSearch, computerBrowser, userBrowser,
                                                 ouBrowser, groupManager, passwordReport,
                                                 domainAdmins);

        Log("Creating MainWindow");
        var mainWindow = new MainWindow(mainVm);
        mainWindow.Loaded  += (_, _) => Log("MainWindow Loaded");
        mainWindow.Closing += (_, _) => Log("MainWindow Closing");
        mainWindow.Closed  += (_, _) => Log("MainWindow Closed");
        Application.Current.MainWindow = mainWindow;
        Log("Showing MainWindow");
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        Log("MainWindow shown");

        var scheduler = new DirHealth.Desktop.Core.Services.ScanScheduler();
        appSettings.OnScheduleChanged = (enabled, hours) =>
        {
            if (enabled) scheduler.Start(hours, () => dashboard.RunScanCommand.ExecuteAsync(null));
            else         scheduler.Stop();
        };

        appSettings.OnCheckForUpdates = () => CheckForUpdatesAsync(http, apiBase, mainVm);

        _ = CheckForUpdatesAsync(http, apiBase, mainVm);
    }

    private static async Task CheckForUpdatesAsync(HttpClient http, string apiBase, MainViewModel mainVm)
    {
        try
        {
            await Task.Delay(5000);
            Log("Update check starting");
            var checker = new DirHealth.Desktop.Core.Services.UpdateChecker(http, apiBase);
            var (update, diagnostic) = await checker.CheckAsync();
            Log($"Update check: {diagnostic}");
            if (update is not null)
            {
                Log($"Update available: {update.Version}");
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => mainVm.SetUpdateAvailable(update));
            }
        }
        catch (Exception ex) { Log($"Update check exception: {ex}"); }
    }

    private static Dictionary<string, string> LoadSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return new();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch { return new(); }
    }
}
