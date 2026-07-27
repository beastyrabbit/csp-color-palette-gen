using System.Windows;
using System.Windows.Threading;

namespace CspPaletteCompanion.App;

public partial class App : Application
{
    private readonly TrayHost _tray = new();

    internal static TrayHost Tray => ((App)Current)._tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!_tray.IsFirstInstance)
        {
            _tray.ActivateExistingInstance();

            // The only sanctioned Application.Shutdown() call in the suite: the running
            // instance has already been asked to show itself and this process has
            // nothing left to do. MarkExitRequested first, so the tray hide branch
            // cannot swallow the teardown if a window ever exists this early.
            (MainWindow as MainWindow)?.MarkExitRequested();
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        SessionEnding += OnSessionEnding;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray.Dispose();
        base.OnExit(e);
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // The window's OnClosing cannot cancel a session-ending shutdown, so the tray
        // branch must be disarmed here or teardown is skipped entirely.
        if (MainWindow is MainWindow window)
        {
            window.MarkExitRequested();
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        // The icon is removed before the fault surfaces, because nothing else runs after
        // it. Handled stays false: a crash must still be a crash.
        _tray.Dispose();
        e.Handled = false;
    }

    private void OnProcessExit(object? sender, EventArgs e) => _tray.Dispose();
}
