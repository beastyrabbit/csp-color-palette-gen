using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Threading;
using CspPaletteCompanion.Companion;
using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.App;

internal sealed class CspAcquisitionService : IAsyncDisposable
{
    private readonly ClipboardImageService _clipboard = new();
    private readonly CompanionCanvasService _companion = new();

    internal bool CompanionConnected => _companion.IsConnected;

    internal Task ConnectCompanionAsync(CancellationToken cancellationToken) =>
        _companion.ConnectAsync(cancellationToken);

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
        AcquireAsync(session, source, null, cancellationToken);

    internal async Task<AcquisitionResult> AcquireAsync(
        CspSession session,
        SourceIntent source,
        CompanionQuickAccessCommandIdentity? selectedMergedSelectionCommand,
        CancellationToken cancellationToken)
    {
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

            var existing = _clipboard.Read();
            if (existing is not null)
            {
                var fallbackResult = ValidateDimensions(existing, session, source);
                return fallbackResult with
                {
                    Route = AcquisitionRoute.Clipboard,
                    Notice = "Companion Mode was unavailable, so the merged clipboard image was used.",
                };
            }

            return AcquisitionResult.Fail(
                "Select Connect for direct Canvas access, or copy a merged canvas image first. " +
                $"Companion Mode said: {ReadableMessage(companionError)}");
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
                "Clip Studio Paint did not remain the active window, so no keyboard commands were sent.");
        }

        if (!SendCopyShortcut())
        {
            return AcquisitionResult.Fail("Windows could not send Copy to Clip Studio Paint.");
        }

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline &&
               NativeMethods.GetClipboardSequenceNumber() == sequence)
        {
            await Task.Delay(50, cancellationToken);
        }

        var copiedSequence = NativeMethods.GetClipboardSequenceNumber();
        if (copiedSequence == sequence)
        {
            return AcquisitionResult.Fail(isSelection
                ? "CSP did not copy any pixels. Select a layer with visible pixels inside the selection and try again."
                : "CSP did not copy the active layer. Check that the layer contains visible pixels and try again.");
        }

        ClipboardImage? image = null;
        var imageDeadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < imageDeadline && image is null)
        {
            try
            {
                image = _clipboard.Read();
            }
            catch (COMException)
            {
            }

            if (image is null)
            {
                await Task.Delay(50, cancellationToken);
            }
        }

        var restored = snapshot.TryRestore(copiedSequence);
        if (image is null)
        {
            return AcquisitionResult.Fail(isSelection
                ? "No selection pixels were copied. Create a selection in Clip Studio Paint, then try again."
                : "The active layer did not provide a clipboard image.");
        }

        var validated = ValidateDimensions(image, session, source);
        return validated with
        {
            ClipboardRestored = restored,
            Notice = validated.Notice,
        };
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
                $"CSP copied {image.Width} × {image.Height}px, but the canvas is {canvas.Width} × {canvas.Height}px. " +
                "An active selection may be cropping the source, so extraction stopped.");
        }

        if (source is SourceIntent.SelectionCanvas or SourceIntent.SelectionLayer &&
            session.CanvasSize is { } selectionCanvas &&
            image.Width == selectionCanvas.Width &&
            image.Height == selectionCanvas.Height)
        {
            return AcquisitionResult.Fail(
                "No bounded selection was detected. Create a selection smaller than the full canvas in Clip Studio Paint, then try again.");
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
                $"CSP ran “{actionName}”, but it did not copy pixels. " +
                "Confirm that a bounded selection overlaps visible artwork.");
        }

        ClipboardImage? image = null;
        var imageDeadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < imageDeadline && image is null)
        {
            try
            {
                image = _clipboard.Read();
            }
            catch (COMException)
            {
            }

            if (image is null)
            {
                await Task.Delay(50, cancellationToken);
            }
        }

        var restored = snapshot.TryRestore(copiedSequence);
        if (image is null)
        {
            return AcquisitionResult.Fail(
                $"CSP ran “{actionName}”, but the clipboard did not contain an image.");
        }

        var validated = ValidateDimensions(image, session, SourceIntent.SelectionCanvas);
        return validated with
        {
            ClipboardRestored = restored,
            Notice = $"Visible selection copied through CSP Quick Access (“{actionName}”).",
        };
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
