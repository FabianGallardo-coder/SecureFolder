using System.Threading;
using System.Windows;
using H.NotifyIcon;
using SecureFolder.App.ViewModels;

namespace SecureFolder.App;

/// <summary>
/// Application entry point for SecureFolder.
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance
        _mutex = new Mutex(true, "SecureFolder_SingleInstance_v1", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e); // Creates MainWindow via StartupUri

        // Wire tray icon
        if (TryFindResource("SecureFolderTray") is TaskbarIcon tray)
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
                tray.Icon = new System.Drawing.Icon(streamInfo.Stream);
        }
    }

    // ── Tray menu handlers ───────────────────────────────────────────────

    private void TrayMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow is { } w)
        {
            w.Show();
            w.Activate();
            w.WindowState = WindowState.Normal;
        }
    }

    private void TrayMenu_LockAll_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel vm)
            vm.LockAllVaultsCommand.Execute(null);
    }

    private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel vm)
            vm.Dispose();

        if (TryFindResource("SecureFolderTray") is TaskbarIcon tray)
            tray.Dispose();

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel vm)
            vm.Dispose();

        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel vm)
            vm.Dispose();

        if (TryFindResource("SecureFolderTray") is TaskbarIcon tray)
            tray.Dispose();

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
