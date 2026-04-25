using System.Windows;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop;

public partial class MainWindow : Window
{
    private readonly WindowStateStore _windowStateStore = new();

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        RestoreWindowState();

        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        Closing += (_, _) => SaveWindowState();

        Loaded += async (_, _) =>
        {
            if (viewModel.Dashboard.RunScanCommand.CanExecute(null))
                await viewModel.Dashboard.RunScanCommand.ExecuteAsync(null);
        };
    }

    private void RestoreWindowState()
    {
        var state = _windowStateStore.Load();

        Width  = state.Width;
        Height = state.Height;

        if (!double.IsNaN(state.Left) && !double.IsNaN(state.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = state.Left;
            Top  = state.Top;
        }

        if (state.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowState()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        _windowStateStore.Save(new WindowStateData
        {
            Left      = isMaximized ? RestoreBounds.Left   : Left,
            Top       = isMaximized ? RestoreBounds.Top    : Top,
            Width     = isMaximized ? RestoreBounds.Width  : Width,
            Height    = isMaximized ? RestoreBounds.Height : Height,
            Maximized = isMaximized
        });
    }

    private void TitleBar_Minimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void TitleBar_MaximizeRestore(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void TitleBar_Close(object sender, RoutedEventArgs e)
        => Close();
}
