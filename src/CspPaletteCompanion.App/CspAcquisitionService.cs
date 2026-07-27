using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Threading;
using CspPaletteCompanion.Companion;
using CspPaletteCompanion.Core.Palette;
using CspPaletteCompanion.Core.Settings;

namespace CspPaletteCompanion.App;

internal sealed class CspAcquisitionService : IAsyncDisposable
{
    private readonly ClipboardImageService _clipboard = new();
    private readonly CompanionCanvasService _companion = new();

    internal bool CompanionConnected => _companion.IsConnected;

    internal ConnectionRoute Route => _companion.Route;

    internal Task ConnectCompanionAsync(CancellationToken cancellationToken) =>
        _companion.ConnectAsync(cancellationToken);

    internal Task ConnectThroughMuxAsync(
        CompanionPairingInfo pairing,
        CancellationToken cancellationToken) =>
        _companion.ConnectThroughMuxAsync(pairing, cancellationToken);

    internal Task SetCurrentColorAsync(RgbColor color, CancellationToken cancellationToken) =>
        _companion.SetCurrentColorAsync(color, cancellationToken);

    internal Task<CompanionActionInspection> InspectMergedSelectionActionAsync(
        CancellationToken cancellationToken) =>
        _companion.InspectMergedSelectionActionAsync(cancellationToken);

    internal Task<CompanionActionInspection> InspectMergedSelectionActionAsync(
        CompanionQuickAccessCommandIdentity selectedCommand,
        CancellationToken cancellationToken) =>
        _companion.InspectMergedSelectionActionAsync(selectedCommand, cancellationToken);

    internal Task<IReadOnlyList<CompanionQuickAccessCommandChoice>>
        GetQuickAccessCommandChoicesAsync(CancellationToken cancellationToken) =>
        _companion.GetQuickAccessCommandChoicesAsync(cancellationToken);

    internal Task<AcquisitionResult> AcquireAsync(
        CspSession session,
        SourceIntent source,
        CancellationToken cancellationToken) =>
        AcquireCoreAsync(session, source, null, null, cancellationToken);

    internal Task<AcquisitionResult> AcquireAsync(
        CspSession session,
        SourceIntent source,
        CompanionQuickAccessCommandIdentity? selectedMergedSelectionCommand,
        CancellationToken cancellationToken) =>
        AcquireCoreAsync(
            session,
            source,
            selectedMergedSelectionCommand,
            null,
            cancellationToken);

    internal Task<AcquisitionResult> AcquireAsync(
        CspSession session,
        SourceIntent source,
        CompanionQuickAccessCommandIdentity? selectedMergedSelectionCommand,
        AppSettings settings,
        CancellationToken cancellationToken) =>
        AcquireCoreAsync(
            session,
            source,
            selectedMergedSelectionCommand,
            settings,
            cancellationToken);

    private async Task<AcquisitionResult> AcquireCoreAsync(
        CspSession session,
        SourceIntent source,
        CompanionQuickAccessCommandIdentity? selectedMergedSelectionCommand,
        AppSettings? settings,
        CancellationToken cancellationToken)
    {
        if (settings is not null)
        {
            if (source == SourceIntent.Canvas &&
                !settings.AllowCompanionCanvasCapture)
            {
                return AcquisitionResult.Fail(
                    "Companion canvas capture is off. Turn it on in Settings.");
            }

            if (source is SourceIntent.Layer or SourceIntent.SelectionLayer &&
                !settings.AllowClipboardCapture)
            {
                return AcquisitionResult.Fail(
                    "Clipboard capture is off. Turn it on in Settings.");
            }

            if (source == SourceIntent.SelectionCanvas)
            {
                if (!settings.AllowClipboardCapture)
                {
                    return AcquisitionResult.Fail(
                        "Clipboard capture is off. Selection · Canvas needs it.");
                }

                if (!settings.AllowAutoActionExecution)
                {
                    return AcquisitionResult.Fail(
                        "Auto Action execution is off. Turn it on in Settings.");
                }

                if (selectedMergedSelectionCommand is null)
                {
                    return AcquisitionResult.Fail(
                        "Choose a CSP Quick Access action in Settings.");
                }
            }
        }

        if (source == SourceIntent.Canvas)
        {
            Exception? companionError = null;
            try
            {
                var companionImage = await _companion.ReadAsync(
                    session,
                    cancellationToken);
                return new AcquisitionResult(
                    companionImage,
                    null,
                    true,
                    AcquisitionRoute.Companion,
                    null);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                companionError = exception;
            }

            if (settings is not null && !settings.AllowClipboardCapture)
            {
                return AcquisitionResult.Fail(
                    $"Companion capture failed and clipboard fallback is off. {ReadableMessage(companionError)}");
            }

            var existingRead = _clipboard.Read();
            if (existingRead.IsDefinitelyIneligible)
            {
                return AcquisitionResult.Fail(
                    "No opaque mid-range pixels after filtering.");
            }

            if (existingRead.Image is { } existing)
            {
                var fallbackResult = ValidateDimensions(existing, session, source);
                return fallbackResult with
                {
                    Route = AcquisitionRoute.Clipboard,
                    Notice = "Used the clipboard image; Companion Mode was unavailable.",
                };
            }

            return AcquisitionResult.Fail(
                $"Connect, or copy a merged canvas image first. {ReadableMessage(companionError)}");
        }

        var isSelection = source is SourceIntent.SelectionCanvas or SourceIntent.SelectionLayer;
        var isCanvasSelection = source == SourceIntent.SelectionCanvas;
        if (isCanvasSelection)
        {
            return await AcquireCanvasSelectionAsync(
                session,
                selectedMergedSelectionCommand,
                cancellationToken);
        }

        var snapshot = ClipboardSnapshot.Capture();
        var sequence = NativeMethods.GetClipboardSequenceNumber();

        if (!NativeMethods.SetForegroundWindow(session.WindowHandle))
        {
            return AcquisitionResult.Fail("Clip Studio Paint could not be activated.");
        }

        await Task.Delay(120, cancellationToken);
        if (NativeMethods.GetForegroundWindow() != session.WindowHandle)
        {
            return AcquisitionResult.Fail(
                "CSP lost focus; no keyboard commands were sent.");
        }

        if (!SendCopyShortcut())
        {
            return AcquisitionResult.Fail("Could not send Copy to Clip Studio Paint.");
        }

        var copiedSequence = await WaitForClipboardChangeAsync(
            sequence,
            session.WindowHandle,
            source == SourceIntent.SelectionLayer,
            cancellationToken);
        if (copiedSequence == sequence)
        {
            return AcquisitionResult.Fail(isSelection
                ? "CSP copied nothing. Select a layer with visible pixels inside the selection."
                : "CSP copied nothing. Check the active layer has visible pixels.");
        }

        var (postCopyResult, restored) = await RunWithGuaranteedRestoreAsync(
            async () =>
            {
                ClipboardImage? image = null;
                var definitelyIneligible = false;
                var imageDeadline = DateTime.UtcNow.AddSeconds(1);
                while (DateTime.UtcNow < imageDeadline &&
                       image is null &&
                       !definitelyIneligible)
                {
                    try
                    {
                        var read = _clipboard.Read();
                        image = read.Image;
                        definitelyIneligible = read.IsDefinitelyIneligible;
                    }
                    catch (COMException)
                    {
                    }

                    if (image is null)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }

                if (definitelyIneligible)
                {
                    return AcquisitionResult.Fail(
                        "No opaque mid-range pixels after filtering.");
                }

                return image is null
                    ? AcquisitionResult.Fail(isSelection
                        ? "Nothing copied. Create a selection in Clip Studio Paint."
                        : "The active layer produced no clipboard image.")
                    : ValidateDimensions(image, session, source);
            },
            () => snapshot.TryRestore(copiedSequence));

        return postCopyResult with { ClipboardRestored = restored };
    }

    private static AcquisitionResult ValidateDimensions(
        ClipboardImage image,
        CspSession session,
        SourceIntent source)
    {
        if (source is SourceIntent.Canvas or SourceIntent.Layer &&
            session.CanvasSize is { } canvas &&
            (image.Width != canvas.Width || image.Height != canvas.Height))
        {
            return AcquisitionResult.Fail(
                $"CSP copied {image.Width} × {image.Height}, but the canvas is {canvas.Width} × {canvas.Height}. " +
                "A selection may be cropping the source.");
        }

        if (source is SourceIntent.SelectionCanvas or SourceIntent.SelectionLayer &&
            session.CanvasSize is { } selectionCanvas &&
            image.Width == selectionCanvas.Width &&
            image.Height == selectionCanvas.Height)
        {
            return AcquisitionResult.Fail(
                "No bounded selection. Create one smaller than the full canvas.");
        }

        return new AcquisitionResult(
            image,
            null,
            true,
            AcquisitionRoute.Clipboard,
            null);
    }

    public ValueTask DisposeAsync() => _companion.DisposeAsync();

    private async Task<AcquisitionResult> AcquireCanvasSelectionAsync(
        CspSession session,
        CompanionQuickAccessCommandIdentity? selectedCommand,
        CancellationToken cancellationToken)
    {
        var snapshot = ClipboardSnapshot.Capture();
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        string actionName;
        try
        {
            actionName = await _companion.CopyMergedSelectionAsync(
                selectedCommand,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return AcquisitionResult.Fail(exception.Message);
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline &&
               NativeMethods.GetClipboardSequenceNumber() == sequence)
        {
            await Task.Delay(50, cancellationToken);
        }

        var copiedSequence = NativeMethods.GetClipboardSequenceNumber();
        if (copiedSequence == sequence)
        {
            return AcquisitionResult.Fail(
                $"CSP ran “{actionName}” and copied nothing. " +
                "Check the selection overlaps visible artwork.");
        }

        var (postCopyResult, restored) = await RunWithGuaranteedRestoreAsync(
            async () =>
            {
                ClipboardImage? image = null;
                var definitelyIneligible = false;
                var imageDeadline = DateTime.UtcNow.AddSeconds(1);
                while (DateTime.UtcNow < imageDeadline &&
                       image is null &&
                       !definitelyIneligible)
                {
                    try
                    {
                        var read = _clipboard.Read();
                        image = read.Image;
                        definitelyIneligible = read.IsDefinitelyIneligible;
                    }
                    catch (COMException)
                    {
                    }

                    if (image is null)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }

                if (definitelyIneligible)
                {
                    return AcquisitionResult.Fail(
                        "No opaque mid-range pixels after filtering.");
                }

                return image is null
                    ? AcquisitionResult.Fail(
                        $"CSP ran “{actionName}” and produced no image.")
                    : ValidateDimensions(image, session, SourceIntent.SelectionCanvas);
            },
            () => snapshot.TryRestore(copiedSequence));

        return postCopyResult with
        {
            ClipboardRestored = restored,
            Notice = null,
        };
    }

    /// <summary>
    /// Runs work that consumes an application-owned clipboard payload and guarantees
    /// that restoration is attempted even when the work fails or is cancelled.
    /// </summary>
    internal static async Task<(T Result, bool Restored)> RunWithGuaranteedRestoreAsync<T>(
        Func<Task<T>> operation,
        Func<bool> restore)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(restore);

        T result;
        bool restored;
        try
        {
            result = await operation();
        }
        finally
        {
            restored = restore();
        }

        return (result, restored);
    }

    private static bool SendCopyShortcut()
    {
        var inputs = new[]
        {
            Key(NativeMethods.VirtualKeyControl, false),
            Key(NativeMethods.VirtualKeyC, false),
            Key(NativeMethods.VirtualKeyC, true),
            Key(NativeMethods.VirtualKeyControl, true),
        };

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) ==
               inputs.Length;
    }

    private static async Task<uint> WaitForClipboardChangeAsync(
        uint initialSequence,
        nint cspWindow,
        bool allowResponsiveFastFailure,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var deadline = started.AddSeconds(3);
        var responsivenessChecked = false;

        while (DateTime.UtcNow < deadline)
        {
            var current = NativeMethods.GetClipboardSequenceNumber();
            if (current != initialSequence)
            {
                return current;
            }

            // An empty CSP selection completes synchronously and leaves the
            // clipboard untouched. Once CSP's UI thread has processed the Copy
            // input and is responsive again, waiting the full timeout cannot
            // produce pixels. A genuinely slow large copy keeps that thread
            // busy, so it retains the original three-second allowance.
            if (allowResponsiveFastFailure &&
                !responsivenessChecked &&
                DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(450))
            {
                responsivenessChecked = true;
                var responsive = NativeMethods.SendMessageTimeout(
                    cspWindow,
                    NativeMethods.WindowMessageNull,
                    0,
                    0,
                    NativeMethods.SendMessageAbortIfHung,
                    75,
                    out _) != 0;
                if (responsive)
                {
                    await Task.Delay(100, cancellationToken);
                    return NativeMethods.GetClipboardSequenceNumber();
                }
            }

            await Task.Delay(50, cancellationToken);
        }

        return NativeMethods.GetClipboardSequenceNumber();
    }

    private static NativeMethods.Input Key(
        ushort virtualKey,
        bool up)
    {
        return new NativeMethods.Input
        {
            Type = NativeMethods.InputKeyboard,
            Union = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = up ? NativeMethods.KeyEventKeyUp : 0,
                },
            },
        };
    }

    private static string ReadableMessage(Exception? exception)
    {
        while (exception?.InnerException is not null &&
               exception is IOException or AggregateException)
        {
            exception = exception.InnerException;
        }

        return exception?.Message ?? "the connection could not be established.";
    }
}

internal sealed record AcquisitionResult(
    ClipboardImage? Image,
    string? Error,
    bool ClipboardRestored,
    AcquisitionRoute Route,
    string? Notice)
{
    internal bool Success => Image is not null && Error is null;

    internal static AcquisitionResult Fail(string error) =>
        new(null, error, true, AcquisitionRoute.None, null);
}

internal enum AcquisitionRoute
{
    None,
    Companion,
    Clipboard,
}
