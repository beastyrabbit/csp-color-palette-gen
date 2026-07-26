using CspPaletteCompanion.Companion;
using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.App;

internal sealed class CompanionCanvasService : IAsyncDisposable
{
    private readonly CompanionQrScanner scanner = new(uri =>
        CompanionPairingCodec.TryDecode(uri.AbsoluteUri, out _));
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly SemaphoreSlim readGate = new(1, 1);
    private CompanionModeClient? client;
    private bool disposed;

    internal bool IsConnected => client?.IsAuthenticated == true;

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsConnected)
        {
            return;
        }

        var pairingUri = await scanner.ScanUntilFoundAsync(cancellationToken);
        var pairing = CompanionPairingCodec.Decode(pairingUri.AbsoluteUri);
        var candidate = await CompanionModeClient.ConnectAndAuthenticateAsync(
            pairing,
            cancellationToken);

        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsConnected)
            {
                await candidate.DisposeAsync();
                return;
            }

            var previous = client;
            client = candidate;
            if (previous is not null)
            {
                await previous.DisposeAsync();
            }
        }
        catch
        {
            await candidate.DisposeAsync();
            throw;
        }
        finally
        {
            connectionGate.Release();
        }
    }

    internal async Task<ClipboardImage> ReadAsync(
        CspSession session,
        CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken);
        try
        {
            var current = client;
            if (current?.IsAuthenticated != true)
            {
                throw new InvalidOperationException(
                    "Companion Mode is disconnected. Select Connect and leave CSP’s QR code visible.");
            }

            try
            {
                var assembler = new WebtoonCanvasAssembler(current);
                var canvasSize = session.CanvasSize;
                var canvas = await assembler.ReadCanvasAsync(
                    canvasSize?.Width,
                    canvasSize?.Height,
                    maximumGalleryLength: 8192,
                    cancellationToken);
                return new ClipboardImage(canvas.Width, canvas.Height, canvas.Pixels);
            }
            catch
            {
                await ResetClientAsync(current);
                throw;
            }
        }
        finally
        {
            readGate.Release();
        }
    }

    internal async Task SetCurrentColorAsync(
        RgbColor color,
        CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken);
        try
        {
            var current = client;
            if (current?.IsAuthenticated != true)
            {
                throw new InvalidOperationException(
                    "Connect to CSP Companion Mode before choosing a swatch.");
            }

            try
            {
                await current.SetCurrentColorRgbAsync(
                    color.Red,
                    color.Green,
                    color.Blue,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                await ResetClientAsync(current);
                throw;
            }
        }
        finally
        {
            readGate.Release();
        }
    }

    internal Task<string> CopyMergedSelectionAsync(CancellationToken cancellationToken) =>
        CopyMergedSelectionAsync(null, cancellationToken);

    internal async Task<string> CopyMergedSelectionAsync(
        CompanionQuickAccessCommandIdentity? selectedCommand,
        CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken);
        try
        {
            var current = client;
            if (current?.IsAuthenticated != true)
            {
                throw new InvalidOperationException(
                    "Connect to CSP Companion Mode before using Selection · Canvas.");
            }

            var quickAccess = await current.GetQuickAccessDataAsync(cancellationToken);
            var command = ResolveMergedSelectionCommand(quickAccess, selectedCommand);
            if (command is null)
            {
                throw new InvalidOperationException(
                    "Add a “Copy Merged Selection” Auto Action to CSP Quick Access, then try again. " +
                    "Selection · Layer works without this one-time setup.");
            }

            await current.DoQuickAccessCommandAsync(command.Identity, cancellationToken);
            return command.DisplayName;
        }
        finally
        {
            readGate.Release();
        }
    }

    internal async Task<IReadOnlyList<CompanionQuickAccessCommandChoice>>
        GetQuickAccessCommandChoicesAsync(
            CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken);
        try
        {
            var current = client;
            if (current?.IsAuthenticated != true)
            {
                throw new InvalidOperationException(
                    "Connect to CSP Companion Mode before loading Quick Access commands.");
            }

            var quickAccess = await current.GetQuickAccessDataAsync(cancellationToken);
            return quickAccess.EnabledCommandChoices;
        }
        finally
        {
            readGate.Release();
        }
    }

    internal Task<CompanionActionInspection> InspectMergedSelectionActionAsync(
        CancellationToken cancellationToken) =>
        InspectMergedSelectionActionAsync(null, cancellationToken);

    internal async Task<CompanionActionInspection> InspectMergedSelectionActionAsync(
        CompanionQuickAccessCommandIdentity? selectedCommand,
        CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken);
        try
        {
            var current = client;
            if (current?.IsAuthenticated != true)
            {
                throw new InvalidOperationException(
                    "Connect to CSP Companion Mode before checking CSP actions.");
            }

            var quickAccess = await current.GetQuickAccessDataAsync(cancellationToken);
            var enabled = quickAccess.EnabledCommands;
            var match = ResolveMergedSelectionCommand(
                quickAccess,
                selectedCommand,
                throwForInvalidSelection: false);
            var names = enabled
                .Select(command => string.IsNullOrWhiteSpace(command.DisplayName)
                    ? command.CommandName
                    : command.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new CompanionActionInspection(
                match is not null,
                match?.DisplayName,
                enabled.Count,
                names,
                enabled.Select(command => command.ToChoice()).ToArray(),
                selectedCommand);
        }
        finally
        {
            readGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await connectionGate.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            await ResetClientWithoutLockAsync();
        }
        finally
        {
            connectionGate.Release();
            connectionGate.Dispose();
            readGate.Dispose();
        }
    }

    private async Task ResetClientAsync(CompanionModeClient expected)
    {
        await connectionGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(client, expected))
            {
                return;
            }

            await ResetClientWithoutLockAsync();
        }
        finally
        {
            connectionGate.Release();
        }
    }

    private async Task ResetClientWithoutLockAsync()
    {
        var current = client;
        client = null;
        if (current is not null)
        {
            await current.DisposeAsync();
        }
    }

    private static bool IsMergedSelectionCopyAction(CompanionQuickAccessCommand command)
    {
        var name = $"{command.DisplayName} {command.CommandName}";
        return
            name.Contains("CSP Palette Companion", StringComparison.OrdinalIgnoreCase) ||
            (name.Contains("copy", StringComparison.OrdinalIgnoreCase) &&
             name.Contains("merged", StringComparison.OrdinalIgnoreCase) &&
             (name.Contains("selection", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("visible layer", StringComparison.OrdinalIgnoreCase)));
    }

    private static CompanionQuickAccessCommand? ResolveMergedSelectionCommand(
        CompanionQuickAccessData quickAccess,
        CompanionQuickAccessCommandIdentity? selectedCommand,
        bool throwForInvalidSelection = true)
    {
        if (selectedCommand is null)
        {
            return quickAccess.EnabledCommands.FirstOrDefault(IsMergedSelectionCopyAction);
        }

        var enabledExact = quickAccess.FindEnabledCommand(selectedCommand);
        if (enabledExact is not null)
        {
            return enabledExact;
        }

        if (!throwForInvalidSelection)
        {
            return null;
        }

        var existsButDisabled = quickAccess.Commands.Any(command =>
            string.Equals(
                command.CommandType,
                selectedCommand.CommandType,
                StringComparison.Ordinal) &&
            string.Equals(
                command.CommandName,
                selectedCommand.CommandName,
                StringComparison.Ordinal));
        if (!existsButDisabled)
        {
            throw new InvalidOperationException(
                "The selected CSP Quick Access command is no longer available. " +
                "Choose an enabled command again in Settings.");
        }

        throw new InvalidOperationException(
            "The selected CSP Quick Access command is currently disabled. " +
            "Enable it in CSP or choose another command in Settings.");
    }
}

internal sealed record CompanionActionInspection(
    bool IsReady,
    string? ActionName,
    int EnabledCommandCount,
    IReadOnlyList<string> EnabledCommandNames,
    IReadOnlyList<CompanionQuickAccessCommandChoice> EnabledCommandChoices,
    CompanionQuickAccessCommandIdentity? SelectedCommand);
