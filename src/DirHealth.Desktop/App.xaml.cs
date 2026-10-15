using System.IO;
using System.Net.Http;
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
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.WriteAllText(_logPath, "APPDOMAIN:\n" + ex.ExceptionObject);
            } catch { }
            var e   = ex.ExceptionObject as Exception;
            var msg = e is not null
                ? $"{e.GetType().Name}: {e.Message}"
                : ex.ExceptionObject?.GetType().Name ?? "Unknown error";
            MessageBox.Show(
                $"Fatal error: {msg}\n\nDetails written to:\n{_logPath}",
                "DirHealth Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var splash = new SplashWindow();
        splash.Show();
        splash.Close();
        Log("Splash closed");

        try
        {
            Log("Launching main window");
            LaunchMainWindow(http);
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

        var check = connector.CheckDomainAdmin();
        if (check.IsAdmin)
        {
            Log("Admin check passed (current context)");
            return true;
        }

        Log(check.Status == AdminCheckStatus.Failed
            ? $"Admin check could not run ({check.Error}) — showing login window"
            : "Admin check: not a member — showing login window");
        var loginVm     = new LoginViewModel(connector);
        var loginWindow = new LoginWindow(loginVm);
        bool success    = false;

        loginVm.LoginSucceeded += () => { success = true; loginWindow.Close(); };
        loginVm.LoginCancelled += () => loginWindow.Close();
        loginWindow.ShowDialog();

        Log($"Login window closed, success={success}");
        return success;
    }

    private void LaunchMainWindow(HttpClient http)
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
        // One exception store for dashboard (score) and findings (card) — ADR 0001/0002
        var exceptions       = new Core.Storage.ExceptionStore();
        var dashboard        = new DashboardViewModel(scanner, exceptions);
        var findings         = new FindingsViewModel(exceptions);
        var appSettings      = new SettingsViewModel(connector);
        var computerDetailVm = new ComputerDetailViewModel(wmi);
        // Search has its own detail VMs so opening a result does not disturb the browsers (S15-2)
        var searchUserDetail = new UserDetailViewModel(scanner);
        var adSearch         = new AdSearchViewModel(searcher, scanner,
                                                     searchUserDetail,
                                                     new ComputerDetailViewModel(wmi));
        var computerBrowser  = new ComputerBrowserViewModel(scanner, computerDetailVm);
        var userDetailVm     = new UserDetailViewModel(scanner);
        // S15-4/S15-5: detail and dossier show the user's findings from the last scan
        foreach (var detailVm in new[] { userDetailVm, searchUserDetail })
        {
            detailVm.FindingsProvider = () => dashboard.Findings;
            detailVm.ScanTimeProvider = () => dashboard.LastScanTime;
        }
        var userBrowser      = new UserBrowserViewModel(scanner, userDetailVm);
        var ouBrowser        = new OuBrowserViewModel(scanner);
        var groupManager     = new GroupManagerViewModel(scanner);
        var passwordReport   = new PasswordReportViewModel(scanner);
        var domainAdmins     = new DomainAdminsViewModel(scanner);
        var dcInventory      = new DcInventoryViewModel(scanner);
        var privilegedGroups = new PrivilegedGroupsViewModel(scanner);
        var domainTrust      = new DomainTrustViewModel(scanner);
        var timeline         = new TimelineViewModel(scanner);
        var gpoBrowser       = new GpoBrowserViewModel(scanner);
        var dataQuality      = new DataQualityViewModel(scanner);
        var mainVm           = new MainViewModel(dashboard, findings, appSettings,
                                                 adSearch, computerBrowser, userBrowser,
                                                 ouBrowser, groupManager, passwordReport,
                                                 domainAdmins, dcInventory, privilegedGroups,
                                                 domainTrust, timeline, gpoBrowser, dataQuality);

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

        appSettings.OnCheckForUpdates = () => ManualUpdateCheckAsync(http, mainVm);

        _ = StartupUpdateCheckAsync(http, mainVm);
    }

    private static async Task StartupUpdateCheckAsync(HttpClient http, MainViewModel mainVm)
    {
        try
        {
            await Task.Delay(5000);
            Log("Startup update check starting");
            var checker = new DirHealth.Desktop.Core.Services.UpdateChecker(http);
            var (update, diagnostic) = await checker.CheckAsync();
            Log($"Startup update check: {diagnostic}");
            if (update is not null)
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => mainVm.SetUpdateAvailable(update));
        }
        catch (Exception ex) { Log($"Startup update check exception: {ex}"); }
    }

    private static async Task<string> ManualUpdateCheckAsync(HttpClient http, MainViewModel mainVm)
    {
        try
        {
            Log("Manual update check starting");
            var checker = new DirHealth.Desktop.Core.Services.UpdateChecker(http);
            var (update, diagnostic) = await checker.CheckAsync();
            Log($"Manual update check: {diagnostic}");
            if (update is not null)
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => mainVm.SetUpdateAvailable(update));
            return diagnostic;
        }
        catch (Exception ex)
        {
            Log($"Manual update check exception: {ex}");
            return $"Check failed: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
