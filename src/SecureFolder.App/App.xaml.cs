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
    private const string MutexName = "SecureFolder_SingleInstance_v1";
    private const string ShowEventName = "SecureFolder_ShowWindow_v1";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _listenerCts;
    private bool _isFirstInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance: only the first process ever owns the mutex.
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        _isFirstInstance = createdNew;

        if (!createdNew)
        {
            // Another instance is already running: ask it to show its window,
            // then exit cleanly. This instance does NOT own the mutex.
            SignalExistingInstance();
            Shutdown();
            return;
        }

        StartShowListener();

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

    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var evt))
            {
                using (evt)
                    evt.Set();
            }
        }
        catch (Exception)
        {
            // Best effort — the other instance may be shutting down.
        }
    }

    private void StartShowListener()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;

        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_showEvent.WaitOne(250)) continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (token.IsCancellationRequested) break;

                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow is not { } window) return;
                        window.Show();
                        if (window.WindowState == WindowState.Minimized)
                            window.WindowState = WindowState.Normal;
                        window.Activate();
                        window.Topmost = true;
                        window.Topmost = false;
                        window.Focus();
                    });
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "SecureFolder-ShowListener",
        };
        thread.Start();
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
        // Cleanup and mutex release happen once in OnExit.
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
        _listenerCts?.Cancel();

        if (MainWindow?.DataContext is MainViewModel vm)
            vm.Dispose();

        if (TryFindResource("SecureFolderTray") is TaskbarIcon tray)
            tray.Dispose();

        _showEvent?.Dispose();

        // Only the instance that acquired the mutex may release it.
        if (_isFirstInstance)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex not owned by this thread — safe to ignore.
            }
        }
        _mutex?.Dispose();

        base.OnExit(e);
    }
}
