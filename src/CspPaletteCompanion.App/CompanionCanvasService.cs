using System.Net;
using CspPaletteCompanion.Companion;
using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.App;

internal enum ConnectionRoute
{
    None,
    Csp,
    Mux,
}

internal sealed class CompanionCanvasService : IAsyncDisposable
{
    private readonly CompanionQrScanner scanner = new(uri =>
        CompanionPairingCodec.TryDecode(uri.AbsoluteUri, out _));
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly SemaphoreSlim readGate = new(1, 1);
    private CompanionModeClient? client;
    private ConnectionRoute route;
    private bool disposed;

    internal bool IsConnected => client?.IsAuthenticated == true;

    internal ConnectionRoute Route => route;

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
        await AdoptAsync(candidate, ConnectionRoute.Csp);
    }

    internal async Task ConnectThroughMuxAsync(
        CompanionPairingInfo pairing,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(pairing);

        // The rule lives where the credential leaves the process, not only in the one
        // caller that exists today: this method transmits pairing.Password to every
        // address in the list. The codec's own guard accepts 10/172.16-31/192.168/
        // 169.254 and IPv6 ULA, and the file route has no human confirmation step.
        if (pairing.Addresses.Count == 0 || !pairing.Addresses.All(IPAddress.IsLoopback))
        {
            throw new InvalidOperationException(
                "Refusing to authenticate to a non-loopback endpoint.");
        }

        if (IsConnected)
        {
            return;
        }

        var candidate = await CompanionModeClient.ConnectAndAuthenticateAsync(
            pairing,
            cancellationToken);
        await AdoptAsync(candidate, ConnectionRoute.Mux);
    }

    /// <summary>
    /// Publishes an already-connected, already-authenticated client, or disposes it.
    /// </summary>
    private async Task AdoptAsync(CompanionModeClient candidate, ConnectionRoute adopted)
    {
        // CancellationToken.None, not the caller's token. Adoption is a bounded,
        // non-cancellable handoff: cancelling while blocked on the gate would drop a
        // live authenticated downstream session, which permanently consumes one of the
        // proxy's client slots while the UI reports success.
        try
        {
            await connectionGate.WaitAsync(CancellationToken.None);
        }
        catch
        {
            await candidate.DisposeAsync();
            throw;
        }

        var swapped = false;
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
            route = adopted;
            swapped = true;
            if (previous is not null)
            {
                await previous.DisposeAsync();
            }
        }
        catch when (!swapped)
        {
            // Filtered: past the swap the candidate is the live connection, and
            // disposing it here would tear down what was just published.
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
                    "Connection lost. Connect again.");
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
                    "Not connected. Connect, then choose the swatch again.");
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
                    "Not connected. Connect, then try Selection · Canvas.");
            }

            var quickAccess = await current.GetQuickAccessDataAsync(cancellationToken);
            var command = ResolveMergedSelectionCommand(quickAccess, selectedCommand);
            if (command is null)
            {
                throw new InvalidOperationException(
                    "Add the setup guide’s action to CSP Quick Access. " +
                    "Selection · Layer needs no setup.");
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
                    "Not connected. Connect first, then refresh.");
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
                    "Not connected. Connect first, then refresh.");
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
        route = ConnectionRoute.None;
        if (current is not null)
        {
            await current.DisposeAsync();
        }
    }

    private static CompanionQuickAccessCommand? ResolveMergedSelectionCommand(
        CompanionQuickAccessData quickAccess,
        CompanionQuickAccessCommandIdentity? selectedCommand,
        bool throwForInvalidSelection = true)
    {
        if (selectedCommand is null)
        {
            return quickAccess.EnabledCommands.FirstOrDefault(
                QuickAccessActionMatcher.IsRecommended);
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
                "That CSP action is gone. Choose another in Settings.");
        }

        throw new InvalidOperationException(
            "That CSP action is disabled. Enable it in CSP, or choose another.");
    }
}

internal sealed record CompanionActionInspection(
    bool IsReady,
    string? ActionName,
    int EnabledCommandCount,
    IReadOnlyList<string> EnabledCommandNames,
    IReadOnlyList<CompanionQuickAccessCommandChoice> EnabledCommandChoices,
    CompanionQuickAccessCommandIdentity? SelectedCommand);
