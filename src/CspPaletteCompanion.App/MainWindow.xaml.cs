using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CspPaletteCompanion.Companion;
using CspPaletteCompanion.Core.Imaging;
using CspPaletteCompanion.Core.Palette;
using CspPaletteCompanion.Core.Settings;

namespace CspPaletteCompanion.App;

public partial class MainWindow : Window
{
    private const int MajorMinimum = 1;
    private const int MajorMaximum = 20;
    private const int MinorMinimum = 0;
    private const int MinorMaximum = 20;

    private enum StatusTone
    {
        Neutral,
        Busy,
        Good,
        Bad,
    }

    private readonly CspLocator _locator = new();
    private readonly CspAcquisitionService _acquisition;
    private readonly PaletteExtractor _extractor = new();
    private readonly PaletteHandoffService _handoff = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly MuxHandoffReader _handoffReader = new();
    private readonly TrayHost _tray = App.Tray;
    private readonly DispatcherTimer _connectionTimer;
    private readonly CancellationTokenSource _windowLifetime = new();
    private CancellationTokenSource? _connectCancellation;
    private Task? _connectTask;
    private bool _autoConnectRequested;
    private bool _manualConnectRequested;
    private bool _connectingThroughMux;
    // The poll would otherwise reclassify a failed Mux connect back to S0 within one
    // tick and the reason would never be on screen; the latch holds S7 until the user
    // acts on it.
    private string? _muxFailure;
    private MuxHandoffResult _muxHandoff;
    private bool _closing;
    private bool _closeInProgress;
    private bool _closingAfterCleanup;
    private bool _exitRequested;
    private bool _hiddenToTray;
    private bool _loadingSettings;
    private bool _pinned = true;
    private AppSettings _settings = AppSettings.Defaults;
    private IReadOnlyList<CompanionQuickAccessCommandChoice> _availableActionChoices =
        Array.Empty<CompanionQuickAccessCommandChoice>();
    private bool _showAllActionChoices;
    private string? _lastPalettePath;
    private Point _dragStart;

    public MainWindow()
    {
        InitializeComponent();
        _acquisition = new CspAcquisitionService();
        _connectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _connectionTimer.Tick += (_, _) => RefreshConnection();
        Loaded += MainWindow_Loaded;
        Closed += async (_, _) =>
        {
            if (_connectTask is not null)
            {
                await IgnoreCancellationAsync(_connectTask);
            }

            await _acquisition.DisposeAsync();
            _connectCancellation?.Dispose();
            _windowLifetime.Dispose();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // G6: the window carries no in-app border, radius or shadow, so rounding is the
        // desktop manager's job.
        NativeMethods.ApplyRoundedCorners(new WindowInteropHelper(this).Handle);

        // Before Loaded, so a second launch fired while this instance is still starting
        // up is received rather than dropped.
        _tray.ReattachActivationHook(this);

        // Settings are read here rather than from Loaded because the saved position has
        // to be applied before the first paint, and an awaited load completes after the
        // window is already on screen. Task.Run keeps the awaits inside LoadAsync off
        // the dispatcher that this call is blocking.
        _settings = Task.Run(() => _settingsService.LoadAsync(CancellationToken.None))
            .GetAwaiter()
            .GetResult();
        RestoreWindowPosition();
    }

    private void RestoreWindowPosition()
    {
        // The probe point sits 40px right and 20px down — inside the title bar — so a
        // window whose saved monitor has been unplugged, or which was left mostly
        // off-screen, falls back to centre.
        if (_settings.WindowLeft is { } left &&
            _settings.WindowTop is { } top &&
            System.Windows.Forms.Screen.AllScreens.Any(screen =>
                screen.WorkingArea.Contains((int)left + 40, (int)top + 20)))
        {
            Left = left;
            Top = top;
            return;
        }

        // CenterScreen cannot be requested from here: WindowStartupLocation has already
        // been consumed by the time this runs, so the centre is computed explicitly.
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + ((workArea.Height - Height) / 2);
    }

    /// <summary>
    /// Closing is synchronous and cancels the window lifetime immediately afterwards, so
    /// the position write has to complete here rather than be posted.
    /// </summary>
    private void SaveWindowPosition()
    {
        try
        {
            _settings = _settings with { WindowLeft = Left, WindowTop = Top };
            Task.Run(() => _settingsService.SaveAsync(_settings, CancellationToken.None))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            // A window that cannot record where it was must still close.
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _loadingSettings = true;
        try
        {
            ApplySettingsToUi();
        }
        finally
        {
            _loadingSettings = false;
        }

        _tray.ShowRequested += (_, _) => ShowFromTray();
        _tray.HideRequested += (_, _) => HideToTray();
        _tray.SettingsRequested += (_, _) => ShowSettingsFromTray();
        _tray.ExitRequested += (_, _) => RequestExit();
        _tray.Attach(this);
        ApplyHostMode();
        _tray.SetConnectionWord(ConnectionText.Text);

        ShowSettingsPath();
        ShowAboutText();
        UpdateStepperAvailability();
        ApplyStatusTone(StatusTone.Neutral);
        RefreshConnection();
        _connectionTimer.Start();
        UpdateSourceHelp();
    }

    /// <summary>
    /// Called before any close that must actually tear the app down, so the tray branch
    /// in <see cref="OnClosing"/> cannot swallow it.
    /// </summary>
    internal void MarkExitRequested() => _exitRequested = true;

    private void RequestExit()
    {
        MarkExitRequested();
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closingAfterCleanup)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        // All four conditions are load-bearing. RunInTray: the mode is on. !_exitRequested:
        // this close is not the tray menu's Exit, not App.SessionEnding and not the
        // second-instance shutdown — and a session-ending close ignores e.Cancel, so a
        // return here would abandon the connection on the one exit path where it matters
        // most. IsVisible: a close arriving at an already-hidden window is not a hide.
        // !_closeInProgress: a teardown is not already under way.
        if (_settings.RunInTray && !_exitRequested && IsVisible && !_closeInProgress)
        {
            HideToTray();
            return;
        }

        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        IsEnabled = false;
        _closing = true;
        _connectionTimer.Stop();
        _autoConnectRequested = false;
        SaveWindowPosition();
        _connectCancellation?.Cancel();
        _windowLifetime.Cancel();

        // Before the dispatcher hop, never after Close(): a ghost icon outlives the
        // process otherwise.
        _tray.Dispose();
        _closingAfterCleanup = true;
        await Dispatcher.Yield(DispatcherPriority.Background);
        Close();
    }

    private void HideToTray()
    {
        _hiddenToTray = true;
        // Hide, not Minimize: a minimised window animates out of a taskbar rectangle
        // that does not exist in tray mode, and animates back into it on restore.
        Hide();
        if (_settings.TrayHintShown)
        {
            return;
        }

        _tray.ShowHint();
        MarkHintShown();
    }

    private void ShowFromTray()
    {
        _hiddenToTray = false;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        ReassertTopmost();
    }

    private void ShowSettingsFromTray()
    {
        ShowFromTray();
        if (SettingsView.Visibility != Visibility.Visible)
        {
            SettingsButton_Click(SettingsButton, new RoutedEventArgs());
        }
    }

    /// <summary>
    /// A window that was hidden has no Z-order slot, so the pin it was carrying does not
    /// survive the restore and it lands under CSP's floating palettes. Re-asserting the
    /// property is what puts it back in the topmost band.
    /// </summary>
    private void ReassertTopmost()
    {
        if (!_pinned)
        {
            return;
        }

        Topmost = false;
        Topmost = true;
    }

    private async void MarkHintShown()
    {
        _settings = _settings with { TrayHintShown = true };
        await SaveSettingsAsync();
    }

    private void ApplyHostMode()
    {
        var wantTaskbar = !_settings.RunInTray;
        if (ShowInTaskbar != wantTaskbar)
        {
            ShowInTaskbar = wantTaskbar;

            // Belt and braces. Measured not to be needed on this Windows build — the
            // handle, the HwndSource, its hooks and the DWM corner preference all
            // survive the assignment — but both calls are idempotent and cost one
            // syscall each.
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != nint.Zero)
            {
                NativeMethods.ApplyRoundedCorners(handle);
                _tray.ReattachActivationHook(this);
            }
        }

        _tray.SetIconVisible(_settings.RunInTray);
        CloseButton.ToolTip = _settings.RunInTray ? "Hide to tray" : "Close";
        AutomationProperties.SetName(CloseButton, _settings.RunInTray ? "Hide to tray" : "Close");

        // Turning tray mode off while the window is hidden would leave it unreachable:
        // no taskbar button existed while it was hidden, and the icon is about to go.
        if (!_settings.RunInTray && _hiddenToTray)
        {
            ShowFromTray();
        }
    }

    private void ShowAboutText()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName()
            .Version;
        var shortVersion = version is null
            ? string.Empty
            : $"Version {version.Major}.{version.Minor}.{version.Build} · ";
        AboutText.Text = $"{shortVersion}GPL-3.0";
    }

    private void ShowSettingsPath()
    {
        // The path is trimmed to one line in the card, so the full value has to
        // stay reachable somewhere.
        SettingsPathText.Text = _settingsService.SettingsFilePath;
        SettingsPathText.ToolTip = _settingsService.SettingsFilePath;
    }

    private void RefreshConnection()
    {
        var session = _locator.Find();
        if (_acquisition.CompanionConnected)
        {
            SetConnectionChrome((Brush)FindResource("AccentBrush"), "Connected");
            ConnectionPanel.Visibility = Visibility.Collapsed;
            ManualConnectButton.Visibility = Visibility.Collapsed;
            if (ConnectButton.IsKeyboardFocusWithin)
            {
                ExtractButton.Focus();
            }
            return;
        }

        ConnectionPanel.Visibility = Visibility.Visible;
        // Visible in S0 and nowhere else: in every other pre-connect state the Connect
        // button already runs the QR path.
        ManualConnectButton.Visibility = Visibility.Collapsed;
        if (_connectTask is { IsCompleted: false })
        {
            SetConnectionChrome(
                (Brush)FindResource("WarningBrush"),
                _connectingThroughMux ? "Connecting" : "Scanning");
            // The heading and the instruction belong to the connection loop while it is
            // running; writing them here too would let the 2-second poll overwrite the
            // step the user is currently being asked to perform.
            ConnectionPanel.BorderBrush = (Brush)FindResource("WarningBrush");
            ConnectButton.Content = "Stop";
            ConnectButton.ToolTip = _connectingThroughMux ? "Stop connecting" : "Stop scanning";
            AutomationProperties.SetName(
                ConnectButton,
                _connectingThroughMux
                    ? "Stop connecting through CSP Mux"
                    : "Stop connecting to CSP Companion Mode");
            return;
        }

        ConnectButton.Content = "Connect";
        if (_settings.UseMuxWhenAvailable)
        {
            ApplyMuxRouteState(session);
        }
        else
        {
            ApplyCspRouteState(session);
        }

        if (_autoConnectRequested && !_closing)
        {
            StartConnectionLoop();
        }
    }

    private void ApplyCspRouteState(CspSession? session)
    {
        ConnectButton.ToolTip = "Scan CSP’s Connect to smartphone QR code";
        AutomationProperties.SetName(ConnectButton, "Connect to CSP Companion Mode");
        ConnectionPanel.BorderBrush = (Brush)FindResource("ErrorBrush");
        if (session is null)
        {
            SetConnectionChrome((Brush)FindResource("ErrorBrush"), "Offline");
            SetConnectionHeading("Open Clip Studio Paint");
            SetConnectionInstructions("Start Clip Studio Paint, then Connect.");
            return;
        }

        SetConnectionChrome((Brush)FindResource("ErrorBrush"), "Disconnected");
        SetConnectionHeading("Connect to CSP");
        SetConnectionInstructions("In CSP, open Connect to smartphone, then Connect.");
    }

    private void ApplyMuxRouteState(CspSession? session)
    {
        // Route-neutral, deliberately: the scanner accepts any valid pairing URL and
        // decodes whichever it finds first, so it cannot be steered at one of the two
        // QR codes. The instruction says which code to put on screen; the button says
        // what pressing it does.
        ConnectButton.ToolTip = "Scan a Connect to smartphone QR code";
        AutomationProperties.SetName(ConnectButton, "Connect to CSP Companion Mode");

        if (_muxFailure is { } failure)
        {
            SetConnectionChrome((Brush)FindResource("ErrorBrush"), "Failed");
            ConnectionPanel.BorderBrush = (Brush)FindResource("ErrorBrush");
            SetConnectionHeading("CSP Mux is not answering");
            SetConnectionInstructions(failure);
            return;
        }

        _muxHandoff = _handoffReader.TryRead();
        switch (_muxHandoff.Status)
        {
            case MuxHandoffStatus.Live:
                SetConnectionChrome((Brush)FindResource("SubtleBrush"), "Offline");
                // Neither a warning nor a failure: this is a Mux that is available.
                ConnectionPanel.BorderBrush = (Brush)FindResource("BorderBrush");
                SetConnectionHeading("CSP Mux is sharing");
                SetConnectionInstructions(string.Empty);
                ConnectButton.ToolTip = "Connect through CSP Mux";
                AutomationProperties.SetName(ConnectButton, "Connect through CSP Mux");
                ManualConnectButton.Visibility = Visibility.Visible;
                return;

            case MuxHandoffStatus.Absent:
                SetConnectionChrome(
                    (Brush)FindResource(session is null ? "SubtleBrush" : "ErrorBrush"),
                    session is null ? "Offline" : "Disconnected");
                ConnectionPanel.BorderBrush = (Brush)FindResource("BorderBrush");
                SetConnectionHeading("CSP Mux is not sharing");
                SetConnectionInstructions("Start sharing in CSP Mux, or connect to CSP.");
                return;

            default:
                SetConnectionChrome((Brush)FindResource("SubtleBrush"), "Offline");
                // A refused file is genuinely anomalous: something exists and does not
                // verify.
                ConnectionPanel.BorderBrush = (Brush)FindResource("ErrorBrush");
                SetConnectionHeading("Cannot use CSP Mux");
                SetConnectionInstructions(RefusalInstruction(_muxHandoff.Status));
                return;
        }
    }

    private static string RefusalInstruction(MuxHandoffStatus status) =>
        status switch
        {
            MuxHandoffStatus.VersionTooNew => "CSP Mux is newer than this app. Update Companion.",
            // §13.5 names five causes and five fixes; Stale is the sixth the tree routes
            // here and the copy table has no row for it. It reuses S1's line rather than
            // claiming the file could not be read: it read fine, and the process it
            // names is gone, so starting sharing again is the fix.
            MuxHandoffStatus.Stale => "Start sharing in CSP Mux, or connect to CSP.",
            MuxHandoffStatus.NotLoopback => "CSP Mux is sharing on a network. Scan its QR.",
            MuxHandoffStatus.Unverifiable => "Could not verify CSP Mux. Scan its QR instead.",
            MuxHandoffStatus.PortNotOwned => "CSP Mux is not sharing on that port. Scan its QR.",
            _ => "Cannot read CSP Mux. Scan CSP’s QR instead.",
        };

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connectTask is { IsCompleted: false })
        {
            _autoConnectRequested = false;
            _connectCancellation?.Cancel();
            SetConnectionInstructions(string.Empty);
            await IgnoreCancellationAsync(_connectTask);
            RestoreCompanionWindow();
            RefreshConnection();
            return;
        }

        // S0 is the only state where this button offers the Mux; everywhere else it is
        // the QR action, which is what its own tooltip and automation name already
        // commit to. Deciding the route from the state on screen is also what keeps the
        // Mux route from ever falling through to a scan.
        var muxOffered = _settings.UseMuxWhenAvailable &&
                         _muxFailure is null &&
                         _muxHandoff.Status == MuxHandoffStatus.Live;
        _manualConnectRequested = !muxOffered || ReferenceEquals(sender, ManualConnectButton);
        _muxFailure = null;
        _autoConnectRequested = true;
        StartConnectionLoop();
        await Task.CompletedTask;
    }

    private void ManualConnectButton_Click(object sender, RoutedEventArgs e) =>
        ConnectButton_Click(sender, e);

    private void StartConnectionLoop()
    {
        if (_closing || !_autoConnectRequested || _connectTask is { IsCompleted: false })
        {
            return;
        }

        _connectCancellation?.Dispose();
        _connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
        _connectTask = RunConnectionLoopAsync(_connectCancellation.Token);
        RefreshConnection();
    }

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_autoConnectRequested && !cancellationToken.IsCancellationRequested)
            {
                if (_settings.UseMuxWhenAvailable && !_manualConnectRequested)
                {
                    await ConnectThroughMuxAsync(cancellationToken);
                    return;
                }

                var session = _locator.Find();
                if (session is null)
                {
                    SetConnectionHeading("Open Clip Studio Paint");
                    SetConnectionInstructions("Waiting for Clip Studio Paint.");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                SetConnectionHeading("Waiting for CSP’s QR code");
                SetConnectionInstructions(
                    "In CSP, open Connect to smartphone. Leave the QR visible.");
                ConnectionPanel.BorderBrush = (Brush)FindResource("WarningBrush");
                Topmost = false;
                NativeMethods.SetForegroundWindow(session.WindowHandle);
                await Task.Delay(300, cancellationToken);

                try
                {
                    await _acquisition.ConnectCompanionAsync(cancellationToken);
                    if (_acquisition.CompanionConnected)
                    {
                        // A drop returns the strip to its pre-connect state and waits for
                        // a press. Without this the poll restarts the loop unattended,
                        // which foregrounds CSP and scans every display with no user
                        // action — and on the other route reconnects through a proxy the
                        // user may have deliberately stopped.
                        _autoConnectRequested = false;
                        RestoreCompanionWindow();
                        Topmost = _pinned;
                        RefreshConnection();
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // The protocol error text is engineering prose and does not fit the
                    // 322px strip column; the loop retries regardless of the reason.
                    SetConnectionInstructions("CSP refused the connection. Retrying…");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
        finally
        {
            // The scan drops topmost so CSP can come forward; restore whatever the
            // user actually chose with the pin, not an unconditional "on top".
            Topmost = _pinned;
            _manualConnectRequested = false;
            if (!_closing)
            {
                RestoreCompanionWindow();
                // Posted, not called: this runs inside the loop task's own finally, so a
                // direct call would still see _connectTask incomplete and render the
                // in-progress state over the outcome.
                _ = Dispatcher.BeginInvoke(RefreshConnection, DispatcherPriority.Background);
            }
        }
    }

    /// <summary>
    /// The Mux route never loops and never falls through to the QR scan: every outcome
    /// either connects or leaves the strip in a state the user has to act on.
    /// </summary>
    private async Task ConnectThroughMuxAsync(CancellationToken cancellationToken)
    {
        // Full re-read, cache ignored. This is the moment of commitment; everything the
        // poll produced was a hint that drove wording.
        var handoff = _handoffReader.ReadAtConnect();
        _muxHandoff = handoff;
        _autoConnectRequested = false;
        if (handoff is not { Status: MuxHandoffStatus.Live, Pairing: { } pairing })
        {
            return;
        }

        // Written here rather than through RefreshConnection: the loop task has not been
        // assigned to _connectTask yet at this point, so the poll's in-progress branch
        // is not reachable and would render a pre-connect state instead.
        _connectingThroughMux = true;
        SetConnectionChrome((Brush)FindResource("WarningBrush"), "Connecting");
        ConnectionPanel.Visibility = Visibility.Visible;
        ConnectionPanel.BorderBrush = (Brush)FindResource("WarningBrush");
        ManualConnectButton.Visibility = Visibility.Collapsed;
        SetConnectionHeading("Connecting through CSP Mux");
        SetConnectionInstructions(string.Empty);
        ConnectButton.Content = "Stop";
        ConnectButton.ToolTip = "Stop connecting";
        AutomationProperties.SetName(ConnectButton, "Stop connecting through CSP Mux");

        // A loopback connect either completes in microseconds or is refused at once; the
        // only way to hang is a filtered port, and an unbounded hang leaves the strip on
        // Connecting with nothing but Stop to escape.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await _acquisition.ConnectThroughMuxAsync(pairing, attempt.Token);
            _connectingThroughMux = false;
            SetStatus("Ready · through CSP Mux", string.Empty);
            ApplyStatusTone(StatusTone.Neutral);
            RefreshConnection();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _connectingThroughMux = false;
            throw;
        }
        catch (Exception exception)
        {
            _connectingThroughMux = false;
            _muxFailure = exception switch
            {
                // A filtered port is not a refused one: do not tell the user to restart
                // something that is running.
                OperationCanceledException => "CSP Mux did not answer. Connect to CSP instead.",
                // The credential rotated, so rescan rather than restart.
                UnauthorizedAccessException => "CSP Mux refused the connection. Scan its QR.",
                _ => "Restart sharing in CSP Mux, or scan CSP’s QR.",
            };
        }
    }

    /// <summary>
    /// The ordinal guard is what stops the 2-second poll from spamming automation
    /// events. There is no tooltip: an element inside the <c>WindowChrome</c> caption
    /// region receives no mouse input, so one could never appear.
    /// </summary>
    private void SetConnectionChrome(Brush brush, string text)
    {
        ConnectionDot.Fill = brush;
        if (!string.Equals(ConnectionText.Text, text, StringComparison.Ordinal))
        {
            ConnectionText.Text = text;
            _tray.SetConnectionWord(text);
            Announce(ConnectionText);
        }
    }

    private void SetConnectionHeading(string text)
    {
        if (string.Equals(ConnectionHeading.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        ConnectionHeading.Text = text;
        Announce(ConnectionHeading);
    }

    private void SetConnectionInstructions(string text)
    {
        if (string.Equals(ConnectionInstructions.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        ConnectionInstructions.Text = text;
        Announce(ConnectionInstructions);
    }

    /// <summary>
    /// Marking a <see cref="TextBlock"/> as a polite live region in XAML only
    /// declares the intent; assistive technology is not told anything until the
    /// live-region-changed event is raised for the updated element.
    /// </summary>
    private static void Announce(UIElement element)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            return;
        }

        var peer = UIElementAutomationPeer.FromElement(element)
            ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private async void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        // Extract is the window's default button, so keep Enter from reaching it
        // while the settings page is up.
        if (MainView.Visibility != Visibility.Visible)
        {
            return;
        }

        // The boxes clamp themselves, so a stale in-progress edit is the only way
        // to get here with an out-of-range value.
        ClampCountBox(MajorCount, MajorMinimum, MajorMaximum);
        ClampCountBox(MinorCount, MinorMinimum, MinorMaximum);
        if (!TryReadCount(MajorCount, MajorMinimum, MajorMaximum, out var major) ||
            !TryReadCount(MinorCount, MinorMinimum, MinorMaximum, out var minor))
        {
            SetFailure(
                "Check the counts",
                $"Major {MajorMinimum}–{MajorMaximum}, minor {MinorMinimum}–{MinorMaximum}.");
            return;
        }

        var session = _locator.Find();
        if (session is null)
        {
            SetFailure("CSP is not running", "Open Clip Studio Paint and a document.");
            return;
        }

        var source = SelectedSource();
        ExtractButton.IsEnabled = false;
        ClearPaletteResult();

        try
        {
            SetProgress("Loading image", string.Empty);
            var acquisition = await _acquisition.AcquireAsync(
                session,
                source,
                SelectedActionIdentity(),
                _settings,
                _windowLifetime.Token);
            RestoreCompanionWindow();
            if (!acquisition.Success)
            {
                SetFailure("Could not read the requested source", acquisition.Error!);
                return;
            }

            SetProgress(
                "Analyzing pixels",
                $"{acquisition.Image!.Width:N0} × {acquisition.Image.Height:N0}");
            var image = acquisition.Image!;
            var result = await Task.Run(() =>
            {
                var rgba = new RgbaImage(image.Width, image.Height, image.Rgba);
                return _extractor.Extract(rgba, new PaletteExtractionOptions(major, minor));
            });

            SetProgress("Preparing palette", string.Empty);
            var bytes = AdobeColorSwatchWriter.Write(result);
            var outputPath = _handoff.CreateOutputPath(session.WindowTitle);
            await File.WriteAllBytesAsync(outputPath, bytes);
            _lastPalettePath = outputPath;

            ShowPalette(result);
            PaletteDragChip.Visibility = Visibility.Visible;
            OpenPaletteButton.Visibility = Visibility.Visible;

            var shortage = result.HasFewerColorsThanRequested
                ? $" Only {result.ColorCount} distinct colors available."
                : string.Empty;
            var clipboard = acquisition.ClipboardRestored
                ? string.Empty
                : " Clipboard changed during extraction; not restored.";
            var notice = acquisition.Notice is null ? string.Empty : $" {acquisition.Notice}";
            var detail =
                $"{result.MajorColors.Count} major + {result.MinorColors.Count} minor colors.{shortage}{clipboard}{notice}";
            SetSuccess($"{result.ColorCount} colors ready", detail);
        }
        catch (NoEligiblePixelsException)
        {
            SetFailure("No eligible colors", "No opaque mid-range pixels after filtering.");
        }
        catch (Exception exception)
        {
            SetFailure("Extraction failed", ReadableMessage(exception));
        }
        finally
        {
            ExtractButton.IsEnabled = true;
        }
    }

    private void ShowPalette(PaletteExtractionResult result)
    {
        PalettePreview.Children.Clear();
        PalettePlaceholder.Visibility = Visibility.Collapsed;

        foreach (var color in result.ToNamedColors())
        {
            var swatch = new Button
            {
                Width = 32,
                Height = 32,
                MinHeight = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 4),
                Background = new SolidColorBrush(Color.FromRgb(
                    color.Color.Red,
                    color.Color.Green,
                    color.Color.Blue)),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Style = (Style)FindResource("SwatchButtonStyle"),
                Tag = color.Color,
                // The tooltip is where the click affordance now lives: the placeholder's
                // explanatory sentence is deleted and the automation name carries it
                // only for screen readers.
                ToolTip = $"{color.Name} · {color.Color.ToHex()} — set as CSP color",
            };
            swatch.Click += PaletteSwatch_Click;
            AutomationProperties.SetName(
                swatch,
                $"Set CSP drawing color to {color.Name}, {color.Color.ToHex()}");
            PalettePreview.Children.Add(swatch);
        }
    }

    private async void PaletteSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RgbColor color } swatch)
        {
            return;
        }

        if (!_acquisition.CompanionConnected)
        {
            SetFailure("Not connected", "Connect, then choose the swatch again.");
            return;
        }

        swatch.IsEnabled = false;
        try
        {
            await _acquisition.SetCurrentColorAsync(color, _windowLifetime.Token);
            SetSuccess($"CSP color set to {color.ToHex()}", string.Empty);
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RefreshConnection();
            SetFailure("Could not set the CSP color", ReadableMessage(exception));
        }
        finally
        {
            swatch.IsEnabled = true;
        }
    }

    private void RestoreCompanionWindow()
    {
        // A window the user parked in the tray stays there: the scan loop dragging it
        // back is the same surprise as a self-restore.
        if (_hiddenToTray)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            NativeMethods.SetForegroundWindow(handle);
        }
    }

    private void SetProgress(string status, string detail)
    {
        SetStatus(status, detail);
        BusyIndicator.Visibility = Visibility.Visible;
        // Busy, not Good: work in progress must not render as a finished result.
        ApplyStatusTone(StatusTone.Busy);
    }

    private void SetFailure(string status, string detail)
    {
        SetStatus(status, detail);
        BusyIndicator.Visibility = Visibility.Collapsed;
        ApplyStatusTone(StatusTone.Bad);
    }

    private void SetSuccess(string status, string detail)
    {
        SetStatus(status, detail);
        BusyIndicator.Visibility = Visibility.Collapsed;
        ApplyStatusTone(StatusTone.Good);
    }

    private void SetStatus(string status, string detail)
    {
        StatusText.Text = status;
        DetailText.Text = detail;
        // The detail block is capped at three lines, so long success summaries are
        // ellipsised; the tooltip keeps the rest readable.
        DetailText.ToolTip = detail;
        Announce(StatusText);
        Announce(DetailText);
    }

    // A 1px coloured outline alone is too thin to read at this size; the low-saturation
    // fill is what makes the state legible at a glance.
    private void ApplyStatusTone(StatusTone tone)
    {
        var (toneKey, surfaceKey) = tone switch
        {
            StatusTone.Good => ("AccentBrush", "AccentStatusBrush"),
            StatusTone.Busy => ("WarningBrush", "WarningStatusBrush"),
            StatusTone.Bad => ("ErrorBrush", "ErrorStatusBrush"),
            _ => ("SubtleBrush", "PanelBrush"),
        };

        var tint = (Brush)FindResource(toneKey);
        StatusDot.Fill = tint;
        StatusPanel.BorderBrush = tone == StatusTone.Neutral
            ? (Brush)FindResource("BorderBrush")
            : tint;
        StatusPanel.Background = (Brush)FindResource(surfaceKey);
    }

    private void ClearPaletteResult()
    {
        PalettePreview.Children.Clear();
        PalettePlaceholder.Visibility = Visibility.Visible;
        PaletteDragChip.Visibility = Visibility.Collapsed;
        OpenPaletteButton.Visibility = Visibility.Collapsed;
        _lastPalettePath = null;
        ApplyStatusTone(StatusTone.Neutral);
    }

    private static bool TryReadCount(TextBox box, int minimum, int maximum, out int value)
    {
        return int.TryParse(box.Text, out value) && value >= minimum && value <= maximum;
    }

    private SourceIntent SelectedSource()
    {
        if (LayerSource.IsChecked == true)
        {
            return SourceIntent.Layer;
        }

        if (SelectionCanvasSource.IsChecked == true)
        {
            return SourceIntent.SelectionCanvas;
        }

        return SelectionLayerSource.IsChecked == true
            ? SourceIntent.SelectionLayer
            : SourceIntent.Canvas;
    }

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateSourceHelp();
        }
    }

    /// <summary>
    /// A greyed-out source with no explanation reads as a broken control, so each
    /// disabled option says which setting unlocks it. Disabled elements only show
    /// tooltips because <c>ToolTipService.ShowOnDisabled</c> is set on the style.
    /// </summary>
    private void UpdateSourceTooltips()
    {
        // The enabled branch is null: it duplicated SourceHelp verbatim one row above.
        CanvasSource.ToolTip = CanvasSource.IsEnabled
            ? null
            : "Turn on Companion canvas capture in Settings.";
        LayerSource.ToolTip = LayerSource.IsEnabled
            ? null
            : "Turn on Clipboard capture in Settings.";
        SelectionLayerSource.ToolTip = SelectionLayerSource.IsEnabled
            ? null
            : "Turn on Clipboard capture in Settings.";
        SelectionCanvasSource.ToolTip = SelectionCanvasSource.IsEnabled
            ? null
            : !_settings.AllowClipboardCapture
                ? "Turn on Clipboard capture in Settings."
                : !_settings.AllowAutoActionExecution
                    ? "Turn on Run selected CSP Auto Action in Settings."
                    : "Choose a CSP Quick Access action in Settings.";
    }

    private void UpdateSourceHelp()
    {
        SourceHelp.Text = SelectedSource() switch
        {
            SourceIntent.Canvas => "The whole visible canvas.",
            SourceIntent.Layer => "The active layer, via the clipboard.",
            SourceIntent.SelectionCanvas => "Runs your CSP action, then copies the selection.",
            SourceIntent.SelectionLayer => "Selected pixels on the active layer.",
            _ => string.Empty,
        };
    }

    private CompanionQuickAccessCommandIdentity? SelectedActionIdentity()
    {
        return string.IsNullOrWhiteSpace(_settings.SelectedAutoActionCommandType) ||
               string.IsNullOrWhiteSpace(_settings.SelectedAutoActionCommandName)
            ? null
            : new CompanionQuickAccessCommandIdentity(
                _settings.SelectedAutoActionCommandType,
                _settings.SelectedAutoActionCommandName);
    }

    private void ApplySettingsToUi()
    {
        var previousLoading = _loadingSettings;
        _loadingSettings = true;
        try
        {
            CompanionPermissionToggle.IsChecked = _settings.AllowCompanionCanvasCapture;
            ClipboardPermissionToggle.IsChecked = _settings.AllowClipboardCapture;
            AutoActionPermissionToggle.IsChecked = _settings.AllowAutoActionExecution;
            AutoActionPermissionToggle.IsEnabled = _settings.AllowClipboardCapture;
            AutoActionPermissionToggle.ToolTip = _settings.AllowClipboardCapture
                ? "Runs the CSP action selected below"
                : "Requires Clipboard capture";
            AutoActionOptionsPanel.Visibility = _settings.AllowAutoActionExecution
                ? Visibility.Visible
                : Visibility.Collapsed;
            MuxHandoffToggle.IsChecked = _settings.UseMuxWhenAvailable;
            TrayModeToggle.IsChecked = _settings.RunInTray;

            CanvasSource.IsEnabled = _settings.AllowCompanionCanvasCapture;
            LayerSource.IsEnabled = _settings.AllowClipboardCapture;
            SelectionLayerSource.IsEnabled = _settings.AllowClipboardCapture;
            SelectionCanvasSource.IsEnabled =
                _settings.AllowClipboardCapture &&
                _settings.AllowAutoActionExecution &&
                SelectedActionIdentity() is not null;
            UpdateSourceTooltips();

            if (!IsSelectedSourceAllowed())
            {
                if (CanvasSource.IsEnabled)
                {
                    CanvasSource.IsChecked = true;
                }
                else if (LayerSource.IsEnabled)
                {
                    LayerSource.IsChecked = true;
                }
            }

            // The ComboBox already displays the selected action, so the settled state
            // says nothing.
            SetActionStatus(SelectedActionIdentity() is null
                ? "No action selected."
                : string.Empty);
            ShowSettingsPath();
            UpdateSourceHelp();
        }
        finally
        {
            _loadingSettings = previousLoading;
        }
    }

    private bool IsSelectedSourceAllowed() =>
        SelectedSource() switch
        {
            SourceIntent.Canvas => _settings.AllowCompanionCanvasCapture,
            SourceIntent.Layer or SourceIntent.SelectionLayer => _settings.AllowClipboardCapture,
            SourceIntent.SelectionCanvas =>
                _settings.AllowClipboardCapture &&
                _settings.AllowAutoActionExecution &&
                SelectedActionIdentity() is not null,
            _ => false,
        };

    private async void PermissionToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        var allowClipboard = ClipboardPermissionToggle.IsChecked == true;
        var allowAutoAction =
            allowClipboard && AutoActionPermissionToggle.IsChecked == true;
        _settings = _settings with
        {
            AllowCompanionCanvasCapture = CompanionPermissionToggle.IsChecked == true,
            AllowClipboardCapture = allowClipboard,
            AllowAutoActionExecution = allowAutoAction,
        };
        await SaveSettingsAsync();
        ApplySettingsToUi();
    }

    private async void MuxHandoffToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        _settings = _settings with { UseMuxWhenAvailable = MuxHandoffToggle.IsChecked == true };
        _muxFailure = null;
        RefreshConnection();
        await SaveSettingsAsync();
    }

    private async void TrayModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        _settings = _settings with { RunInTray = TrayModeToggle.IsChecked == true };
        ApplyHostMode();
        await SaveSettingsAsync();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        MainView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        // Hidden rather than disabled: a ghosted gear next to a Back button reads
        // as a broken control instead of a redundant one.
        SettingsButton.Visibility = Visibility.Hidden;
        ApplySettingsToUi();
        BackButton.Focus();

        if (_settings.AllowAutoActionExecution && _acquisition.CompanionConnected)
        {
            await RefreshActionChoicesAsync();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
        SettingsButton.Visibility = Visibility.Visible;
        ApplySettingsToUi();
        SettingsButton.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsView.Visibility == Visibility.Visible)
        {
            BackButton_Click(BackButton, e);
            e.Handled = true;
        }
    }

    private void PinButton_Toggled(object sender, RoutedEventArgs e)
    {
        _pinned = PinButton.IsChecked == true;
        Topmost = _pinned;
        PinButton.ToolTip = _pinned
            ? "Always on top"
            : "Always on top (off)";
    }

    private void ShowSettingsFileButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _settingsService.SettingsFilePath;
        if (File.Exists(path))
        {
            _handoff.ShowInExplorer(path);
            return;
        }

        // Nothing has been saved yet: show the folder the file will land in.
        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private async void RefreshActionsButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshActionChoicesAsync();

    private async Task RefreshActionChoicesAsync()
    {
        if (!_acquisition.CompanionConnected)
        {
            SetActionStatus("Connect first, then refresh.");
            return;
        }

        RefreshActionsButton.IsEnabled = false;
        SetActionStatus("Reading CSP actions…");
        try
        {
            var choices = await _acquisition.GetQuickAccessCommandChoicesAsync(
                _windowLifetime.Token);
            _availableActionChoices = choices;
            _showAllActionChoices = false;
            PresentActionChoices();
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RefreshConnection();
            SetActionStatus($"Could not read CSP actions: {ReadableMessage(exception)}");
        }
        finally
        {
            RefreshActionsButton.IsEnabled = true;
        }
    }

    private void ShowAllActionsButton_Click(object sender, RoutedEventArgs e)
    {
        _showAllActionChoices = !_showAllActionChoices;
        PresentActionChoices();
    }

    private void PresentActionChoices()
    {
        var selectedIdentity = SelectedActionIdentity();
        var visibleChoices = QuickAccessActionMatcher.VisibleChoices(
            _availableActionChoices,
            selectedIdentity,
            _showAllActionChoices);
        var options = visibleChoices
            .Select(choice => new QuickAccessActionOption(choice))
            .ToArray();
        var selected = selectedIdentity is null
            ? null
            : options.FirstOrDefault(option =>
                string.Equals(
                    option.Choice.Identity.CommandType,
                    selectedIdentity.CommandType,
                    StringComparison.Ordinal) &&
                string.Equals(
                    option.Choice.Identity.CommandName,
                    selectedIdentity.CommandName,
                    StringComparison.Ordinal));
        var recommendedCount =
            QuickAccessActionMatcher.RecommendedCount(_availableActionChoices);
        var isFiltered =
            recommendedCount > 0 &&
            visibleChoices.Count < _availableActionChoices.Count;

        var previousLoading = _loadingSettings;
        _loadingSettings = true;
        try
        {
            AutoActionPicker.ItemsSource = options;
            AutoActionPicker.SelectedItem = selected;
            ShowAllActionsButton.Visibility =
                recommendedCount > 0 &&
                _availableActionChoices.Count > recommendedCount
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ShowAllActionsButton.Content = _showAllActionChoices
                ? "Show recommended"
                : $"Show all ({_availableActionChoices.Count})";
        }
        finally
        {
            _loadingSettings = previousLoading;
        }

        SetActionStatus(_availableActionChoices.Count == 0
            ? "CSP returned no enabled actions."
            : recommendedCount == 0
                ? $"{_availableActionChoices.Count} commands. No recommended match found."
                : selected is not null
                    ? isFiltered
                        ? $"{selected} — showing recommended actions."
                        : $"{selected} — enabled in CSP."
                    : isFiltered
                        ? $"Showing {visibleChoices.Count} recommended of {_availableActionChoices.Count} commands."
                        : $"{_availableActionChoices.Count} commands. Choose the action from the setup guide.");
    }

    private async void AutoActionPicker_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loadingSettings ||
            AutoActionPicker.SelectedItem is not QuickAccessActionOption option)
        {
            return;
        }

        _settings = _settings with
        {
            SelectedAutoActionCommandType = option.Choice.Identity.CommandType,
            SelectedAutoActionCommandName = option.Choice.Identity.CommandName,
            SelectedAutoActionDisplayName = option.DisplayName,
        };
        await SaveSettingsAsync();
        ApplySettingsToUi();
        // The caution now lives permanently in the options card's caption, so it is on
        // screen whether or not the selection just changed.
        SetActionStatus(option.DisplayName);
    }

    private void SetActionStatus(string text)
    {
        ActionStatusText.Text = text;
        Announce(ActionStatusText);
    }

    /// <summary>
    /// Shows a settings-page-wide message. Anything reported here has to be
    /// visible even when the Auto Action panel is collapsed.
    /// </summary>
    private void SetSettingsNotice(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            SettingsNotice.Visibility = Visibility.Collapsed;
            SettingsNoticeText.Text = string.Empty;
            return;
        }

        SettingsNoticeText.Text = text;
        SettingsNotice.Visibility = Visibility.Visible;
        Announce(SettingsNoticeText);
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsService.SaveAsync(_settings, _windowLifetime.Token);
            SetSettingsNotice(null);
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetSettingsNotice($"Settings could not be saved: {ReadableMessage(exception)}");
        }
    }

    private void SetupGuideButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "docs",
            "selection-canvas-setup.md");
        if (!File.Exists(path))
        {
            SetSettingsNotice("The setup guide is missing from this build.");
            return;
        }

        SetSettingsNotice(null);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void DecreaseMajor_Click(object sender, RoutedEventArgs e) => Adjust(MajorCount, -1);

    private void IncreaseMajor_Click(object sender, RoutedEventArgs e) => Adjust(MajorCount, 1);

    private void DecreaseMinor_Click(object sender, RoutedEventArgs e) => Adjust(MinorCount, -1);

    private void IncreaseMinor_Click(object sender, RoutedEventArgs e) => Adjust(MinorCount, 1);

    private void Adjust(TextBox box, int delta)
    {
        var (minimum, maximum) = RangeOf(box);
        var current = int.TryParse(box.Text, out var value) ? value : minimum;
        box.Text = Math.Clamp(current + delta, minimum, maximum).ToString();
        box.CaretIndex = box.Text.Length;
    }

    private (int Minimum, int Maximum) RangeOf(TextBox box) =>
        ReferenceEquals(box, MajorCount)
            ? (MajorMinimum, MajorMaximum)
            : (MinorMinimum, MinorMaximum);

    // Digits only: the old boxes accepted any text and only reported the problem
    // after a full extraction attempt failed.
    private void CountBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void CountBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                break;
            case Key.Up:
                Adjust(box, 1);
                e.Handled = true;
                break;
            case Key.Down:
                Adjust(box, -1);
                e.Handled = true;
                break;
        }
    }

    private void CountBox_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        Adjust(box, e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void CountBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateStepperAvailability();

    private void CountBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        var (minimum, maximum) = RangeOf(box);
        ClampCountBox(box, minimum, maximum);
    }

    private static void ClampCountBox(TextBox box, int minimum, int maximum)
    {
        var clamped = int.TryParse(box.Text, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : minimum;
        var text = clamped.ToString();
        if (!string.Equals(box.Text, text, StringComparison.Ordinal))
        {
            box.Text = text;
        }
    }

    /// <summary>
    /// Greys out a stepper once its bound is reached instead of letting it look
    /// live and silently do nothing.
    /// </summary>
    private void UpdateStepperAvailability()
    {
        // TextChanged fires while the XAML is still being loaded, when the fields
        // declared after the box being initialised are still null.
        if (!IsInitialized)
        {
            return;
        }

        var major = int.TryParse(MajorCount.Text, out var majorValue) ? majorValue : MajorMinimum;
        var minor = int.TryParse(MinorCount.Text, out var minorValue) ? minorValue : MinorMinimum;
        DecreaseMajorButton.IsEnabled = major > MajorMinimum;
        IncreaseMajorButton.IsEnabled = major < MajorMaximum;
        DecreaseMinorButton.IsEnabled = minor > MinorMinimum;
        IncreaseMinorButton.IsEnabled = minor < MinorMaximum;
    }

    private void OpenPaletteButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _lastPalettePath;
        if (path is not null && File.Exists(path))
        {
            _handoff.ShowInExplorer(path);
        }
    }

    // Clicking without dragging used to do nothing at all, even though the
    // keyboard path opened the file.
    private void PaletteDragChip_Click(object sender, RoutedEventArgs e) =>
        OpenPaletteButton_Click(sender, e);

    private void PaletteDragChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(PaletteDragChip);
    }

    private void PaletteDragChip_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _lastPalettePath is null ||
            !File.Exists(_lastPalettePath))
        {
            return;
        }

        var current = e.GetPosition(PaletteDragChip);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var files = new StringCollection { _lastPalettePath };
        var data = new DataObject();
        data.SetFileDropList(files);
        DragDrop.DoDragDrop(PaletteDragChip, data, DragDropEffects.Copy);
    }

    private static string ReadableMessage(Exception exception)
    {
        while (exception.InnerException is not null &&
               exception is IOException or AggregateException)
        {
            exception = exception.InnerException;
        }

        return exception.Message;
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal sealed record QuickAccessActionOption(
    CompanionQuickAccessCommandChoice Choice)
{
    internal string DisplayName =>
        string.IsNullOrWhiteSpace(Choice.DisplayName)
            ? Choice.Identity.CommandName
            : Choice.DisplayName;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Choice.SetName)
            ? DisplayName
            : $"{DisplayName} — {Choice.SetName}";
}
