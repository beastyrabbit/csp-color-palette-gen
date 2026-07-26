using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CspPaletteCompanion.Core.Imaging;
using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.App;

public partial class MainWindow : Window
{
    private readonly CspLocator _locator = new();
    private readonly CspAcquisitionService _acquisition;
    private readonly PaletteExtractor _extractor = new();
    private readonly PaletteHandoffService _handoff = new();
    private readonly DispatcherTimer _connectionTimer;
    private readonly CancellationTokenSource _windowLifetime = new();
    private CancellationTokenSource? _connectCancellation;
    private Task? _connectTask;
    private bool _autoConnectRequested;
    private bool _closing;
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
        Loaded += (_, _) =>
        {
            RefreshConnection();
            _connectionTimer.Start();
            UpdateSourceHelp();
        };
        Closing += (_, _) =>
        {
            _closing = true;
            _connectionTimer.Stop();
            _autoConnectRequested = false;
            _connectCancellation?.Cancel();
            _windowLifetime.Cancel();
        };
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

    private void RefreshConnection()
    {
        var session = _locator.Find();
        if (_acquisition.CompanionConnected)
        {
            SetConnectionChrome(
                (Brush)FindResource("AccentBrush"),
                "Connected",
                "Clip Studio Paint is authenticated through local Companion Mode.");
            ConnectionPanel.Visibility = Visibility.Collapsed;
            if (ConnectButton.IsKeyboardFocusWithin)
            {
                ExtractButton.Focus();
            }
            return;
        }

        ConnectionPanel.Visibility = Visibility.Visible;
        if (_connectTask is { IsCompleted: false })
        {
            SetConnectionChrome(
                (Brush)FindResource("WarningBrush"),
                session is null ? "Waiting for CSP…" : "Connecting…",
                session is null
                    ? "Open Clip Studio Paint; connection will continue automatically."
                    : "Scanning all displays for CSP’s Companion Mode QR code.");
            ConnectionPanel.BorderBrush = (Brush)FindResource("WarningBrush");
            ConnectButton.Content = "Stop";
            AutomationProperties.SetName(ConnectButton, "Stop connecting to CSP Companion Mode");
            return;
        }

        ConnectButton.Content = "Connect";
        AutomationProperties.SetName(ConnectButton, "Connect to CSP Companion Mode");
        ConnectionPanel.BorderBrush = (Brush)FindResource("ErrorBrush");
        if (session is null)
        {
            SetConnectionChrome(
                (Brush)FindResource("ErrorBrush"),
                "CSP not found",
                "Open Clip Studio Paint, then select Connect.");
            ConnectionHeading.Text = "Open CSP to enable direct Canvas access";
            ConnectionInstructions.Text =
                "Start Clip Studio Paint. You can select Connect now and the app will keep waiting.";
        }
        else
        {
            SetConnectionChrome(
                (Brush)FindResource("ErrorBrush"),
                "Disconnected",
                $"CSP {session.Version} is open, but Companion Mode is not connected.");
            ConnectionHeading.Text = "Connect for direct Canvas access";
            ConnectionInstructions.Text =
                "In CSP, open “Connect to smartphone,” leave the QR visible, then select Connect.";
        }

        if (_autoConnectRequested && !_closing)
        {
            StartConnectionLoop();
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connectTask is { IsCompleted: false })
        {
            _autoConnectRequested = false;
            _connectCancellation?.Cancel();
            ConnectionInstructions.Text = "Stopping the connection scan…";
            await IgnoreCancellationAsync(_connectTask);
            RestoreCompanionWindow();
            RefreshConnection();
            return;
        }

        _autoConnectRequested = true;
        StartConnectionLoop();
        await Task.CompletedTask;
    }

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
                var session = _locator.Find();
                if (session is null)
                {
                    ConnectionInstructions.Text =
                        "Waiting for Clip Studio Paint. Open it and the scan will continue automatically.";
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                ConnectionHeading.Text = "Waiting for CSP’s QR code";
                ConnectionInstructions.Text =
                    "In CSP, open “Connect to smartphone” and leave the QR visible. Scanning continues until connected.";
                ConnectionPanel.BorderBrush = (Brush)FindResource("WarningBrush");
                Topmost = false;
                NativeMethods.SetForegroundWindow(session.WindowHandle);
                await Task.Delay(300, cancellationToken);

                try
                {
                    await _acquisition.ConnectCompanionAsync(cancellationToken);
                    if (_acquisition.CompanionConnected)
                    {
                        RestoreCompanionWindow();
                        Topmost = true;
                        RefreshConnection();
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ConnectionInstructions.Text =
                        $"The QR was found, but CSP did not accept the connection ({ReadableMessage(exception)}). Retrying…";
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
        finally
        {
            Topmost = true;
            if (!_closing)
            {
                RestoreCompanionWindow();
            }
        }
    }

    private void SetConnectionChrome(Brush brush, string text, string tooltip)
    {
        ConnectionDot.Fill = brush;
        ConnectionText.Text = text;
        ConnectionText.ToolTip = tooltip;
    }

    private async void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadCount(MajorCount, 1, 20, out var major) ||
            !TryReadCount(MinorCount, 0, 20, out var minor))
        {
            SetFailure("Check the color counts", "Major colors must be 1–20 and minor colors must be 0–20.");
            return;
        }

        var session = _locator.Find();
        if (session is null)
        {
            SetFailure("CSP is not running", "Open Clip Studio Paint and a document, then try again.");
            return;
        }

        var source = SelectedSource();
        ExtractButton.IsEnabled = false;

        try
        {
            SetProgress(source == SourceIntent.Canvas ? "Reading CSP canvas" : "Reading CSP pixels");
            var acquisition = await _acquisition.AcquireAsync(session, source, _windowLifetime.Token);
            RestoreCompanionWindow();
            if (!acquisition.Success)
            {
                SetFailure("Could not read the requested source", acquisition.Error!);
                return;
            }

            SetProgress("Extracting colors");
            var image = acquisition.Image!;
            var result = await Task.Run(() =>
            {
                var rgba = new RgbaImage(image.Width, image.Height, image.Rgba);
                return _extractor.Extract(rgba, new PaletteExtractionOptions(major, minor));
            });

            SetProgress("Creating Color Set");
            var bytes = AdobeColorSwatchWriter.Write(result);
            var outputPath = _handoff.CreateOutputPath(session.WindowTitle);
            await File.WriteAllBytesAsync(outputPath, bytes);
            _lastPalettePath = outputPath;

            ShowPalette(result);
            PaletteDragChip.Visibility = Visibility.Visible;
            OpenPaletteButton.Visibility = Visibility.Visible;

            var shortage = result.HasFewerColorsThanRequested
                ? $" Only {result.ColorCount} distinct eligible colors were available."
                : string.Empty;
            var clipboard = acquisition.ClipboardRestored
                ? string.Empty
                : " The clipboard changed during extraction and was not overwritten.";
            var route = acquisition.Route == AcquisitionRoute.Companion
                ? " Canvas pixels came directly from local CSP Companion Mode."
                : string.Empty;
            var notice = acquisition.Notice is null ? string.Empty : $" {acquisition.Notice}";
            var semantics = source switch
            {
                SourceIntent.SelectionLayer =>
                    " Selection came only from the active layer.",
                _ => string.Empty,
            };
            var detail =
                $"{result.MajorColors.Count} major + {result.MinorColors.Count} minor colors.{shortage}{clipboard}{route}{notice}{semantics} " +
                "Drop the palette file onto CSP’s Color Set palette.";
            SetSuccess($"{result.ColorCount} colors ready", detail);
        }
        catch (NoEligiblePixelsException)
        {
            SetFailure("No eligible colors", "The source contains no opaque mid-range pixels after filtering.");
        }
        catch (Exception exception)
        {
            SetFailure("Extraction failed", exception.Message);
        }
        finally
        {
            ExtractButton.IsEnabled = true;
        }
    }

    private void ShowPalette(PaletteExtractionResult result)
    {
        PalettePreview.Children.Clear();

        foreach (var color in result.ToNamedColors())
        {
            var swatch = new Button
            {
                Width = 32,
                Height = 32,
                MinHeight = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(Color.FromRgb(
                    color.Color.Red,
                    color.Color.Green,
                    color.Color.Blue)),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Style = (Style)FindResource("SwatchButtonStyle"),
                Tag = color.Color,
                ToolTip = $"{color.Name} — {color.Color.ToHex()}",
            };
            swatch.Click += PaletteSwatch_Click;
            AutomationProperties.SetName(
                swatch,
                $"Set CSP drawing color to {color.Name}, {color.Color.ToHex()}");
            AutomationProperties.SetHelpText(
                swatch,
                "Changes the current drawing color in connected Clip Studio Paint.");
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
            SetFailure(
                "Connect to use live colors",
                "Select Connect, show CSP’s smartphone QR code, then choose the swatch again.");
            return;
        }

        swatch.IsEnabled = false;
        try
        {
            await _acquisition.SetCurrentColorAsync(color, _windowLifetime.Token);
            SetSuccess(
                $"CSP color set to {color.ToHex()}",
                "The selected swatch is now Clip Studio Paint’s current drawing color.");
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

    private void SetProgress(string status)
    {
        StatusText.Text = status;
        DetailText.Text = "Working locally. This usually takes only a moment.";
        BusyIndicator.Visibility = Visibility.Visible;
        ApplyStatusTone((Brush)FindResource("AccentBrush"));
    }

    private void SetFailure(string status, string detail)
    {
        StatusText.Text = status;
        DetailText.Text = detail;
        BusyIndicator.Visibility = Visibility.Collapsed;
        ApplyStatusTone((Brush)FindResource("ErrorBrush"));
    }

    private void SetSuccess(string status, string detail)
    {
        StatusText.Text = status;
        DetailText.Text = detail;
        BusyIndicator.Visibility = Visibility.Collapsed;
        ApplyStatusTone((Brush)FindResource("AccentBrush"));

        var expandedHeight = Math.Min(660, SystemParameters.WorkArea.Height * 0.9);
        if (Height < expandedHeight)
        {
            Height = expandedHeight;
        }
    }

    private void ApplyStatusTone(Brush brush)
    {
        StatusDot.Fill = brush;
        StatusPanel.BorderBrush = brush;
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

    private void UpdateSourceHelp()
    {
        CheckActionButton.Visibility = SelectedSource() == SourceIntent.SelectionCanvas
            ? Visibility.Visible
            : Visibility.Collapsed;
        SourceHelp.Text = SelectedSource() switch
        {
            SourceIntent.Canvas =>
                "Uses Companion Mode; a merged clipboard image is the fallback.",
            SourceIntent.Layer =>
                "Layer asks CSP to copy the active layer. A cropped result is rejected when canvas dimensions are known.",
            SourceIntent.SelectionCanvas =>
                "Copies the visible composite through a CSP Auto Action. One-time Quick Access setup is required.",
            SourceIntent.SelectionLayer =>
                "Copies only the active layer inside the selection. Other visible layers are ignored.",
            _ => string.Empty,
        };
    }

    private async void CheckActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_acquisition.CompanionConnected)
        {
            SetFailure(
                "Connect to check CSP actions",
                "Connect Companion Mode, then select Check CSP Action again.");
            return;
        }

        CheckActionButton.IsEnabled = false;
        try
        {
            SetProgress("Checking CSP Quick Access");
            var inspection = await _acquisition.InspectMergedSelectionActionAsync(
                _windowLifetime.Token);
            if (inspection.IsReady)
            {
                var name = string.IsNullOrWhiteSpace(inspection.ActionName)
                    ? "compatible merged-selection action"
                    : $"“{inspection.ActionName}”";
                SetSuccess(
                    "Canvas selection action is ready",
                    $"Found {name} among {inspection.EnabledCommandCount} enabled Quick Access commands.");
                return;
            }

            var visibleNames = inspection.EnabledCommandNames.Count == 0
                ? "No enabled Quick Access commands were returned."
                : "Visible commands: " + string.Join(
                    ", ",
                    inspection.EnabledCommandNames.Take(6)) +
                  (inspection.EnabledCommandNames.Count > 6 ? ", …" : ".");
            SetFailure(
                "Canvas selection action not found",
                $"{visibleNames} CSP exposes Auto Actions to Companion Mode only after they are added to Quick Access.");
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RefreshConnection();
            SetFailure("Could not check CSP actions", ReadableMessage(exception));
        }
        finally
        {
            CheckActionButton.IsEnabled = true;
        }
    }

    private void DecreaseMajor_Click(object sender, RoutedEventArgs e) => Adjust(MajorCount, -1, 1, 20);

    private void IncreaseMajor_Click(object sender, RoutedEventArgs e) => Adjust(MajorCount, 1, 1, 20);

    private void DecreaseMinor_Click(object sender, RoutedEventArgs e) => Adjust(MinorCount, -1, 0, 20);

    private void IncreaseMinor_Click(object sender, RoutedEventArgs e) => Adjust(MinorCount, 1, 0, 20);

    private static void Adjust(TextBox box, int delta, int minimum, int maximum)
    {
        var current = int.TryParse(box.Text, out var value) ? value : minimum;
        box.Text = Math.Clamp(current + delta, minimum, maximum).ToString();
    }

    private void OpenPaletteButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _lastPalettePath;
        if (path is not null && File.Exists(path))
        {
            _handoff.ShowInExplorer(path);
        }
    }

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

    private void PaletteDragChip_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        OpenPaletteButton_Click(sender, e);
        e.Handled = true;
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
