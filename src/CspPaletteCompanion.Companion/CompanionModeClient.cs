using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CspPaletteCompanion.Companion;

public sealed class CompanionServerPushEventArgs(CompanionFrame frame) : EventArgs
{
    public CompanionFrame Frame { get; } = frame;
}

public sealed class CompanionModeClient : IAsyncDisposable
{
    private const string AuthenticateCommand = "Authenticate";
    private const string HeartbeatCommand = "TellHeartbeat";
    private const string PreviewCommand = "PreviewWebtoonFromClient";
    private const string SetCurrentColorCommand = "SetCurrentColor";
    private const string GetQuickAccessDataCommand = "GetQuickAccessData";
    private const string DoQuickAccessWireCommand = "DoQuickAccess";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly Stream stream;
    private readonly TcpClient? tcpClient;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<CompanionFrame>> pending = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly TimeSpan heartbeatInterval;
    private readonly int maximumFrameLength;
    private Task? receiveTask;
    private Task? heartbeatTask;
    private int nextSerial = -1;
    private int disposed;
    private bool isAuthenticated;

    private CompanionModeClient(
        Stream stream,
        TcpClient? tcpClient,
        TimeSpan heartbeatInterval,
        int maximumFrameLength)
    {
        this.stream = stream;
        this.tcpClient = tcpClient;
        this.heartbeatInterval = heartbeatInterval;
        this.maximumFrameLength = maximumFrameLength;
    }

    public event EventHandler<CompanionServerPushEventArgs>? ServerPushReceived;

    public bool IsAuthenticated => Volatile.Read(ref isAuthenticated);

    public EndPoint? RemoteEndPoint => tcpClient?.Client.RemoteEndPoint;

    public static async Task<CompanionModeClient> ConnectAndAuthenticateAsync(
        CompanionPairingInfo pairing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        if (pairing.Addresses.Count == 0)
        {
            throw new ArgumentException("At least one pairing address is required.", nameof(pairing));
        }

        Exception? lastError = null;
        foreach (var address in pairing.Addresses)
        {
            var tcpClient = new TcpClient(address.AddressFamily);
            try
            {
                await tcpClient.ConnectAsync(address, pairing.Port, cancellationToken).ConfigureAwait(false);
                var client = new CompanionModeClient(
                    tcpClient.GetStream(),
                    tcpClient,
                    TimeSpan.FromSeconds(1),
                    CompanionFrameCodec.DefaultMaximumFrameLength);
                client.StartReceiveLoop();
                await client.AuthenticateAsync(
                    pairing.Generation,
                    pairing.Password,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                lastError = ex;
                tcpClient.Dispose();
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        throw new IOException("Could not connect to any companion endpoint.", lastError);
    }

    public static async Task<CompanionModeClient> ConnectAndAuthenticateAsync(
        string pairingUrl,
        bool allowPublicEndpoints = false,
        CancellationToken cancellationToken = default)
    {
        var pairing = CompanionPairingCodec.Decode(pairingUrl, allowPublicEndpoints);
        return await ConnectAndAuthenticateAsync(pairing, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a client over an already connected duplex stream. Intended for transports and tests.
    /// </summary>
    public static CompanionModeClient CreateForConnectedStream(
        Stream stream,
        TimeSpan? heartbeatInterval = null,
        int maximumFrameLength = CompanionFrameCodec.DefaultMaximumFrameLength)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var client = new CompanionModeClient(
            stream,
            null,
            heartbeatInterval ?? TimeSpan.FromSeconds(1),
            maximumFrameLength);
        client.StartReceiveLoop();
        return client;
    }

    public async Task<CompanionAuthResult> AuthenticateAsync(
        string generation,
        string password,
        string? newPassword = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        newPassword ??= CompanionAuthCodec.CreateRotatedPassword();
        var detail = CompanionAuthCodec.CreateAuthenticationDetail(generation, password, newPassword);
        var response = await SendRawAsync(
            AuthenticateCommand,
            detail,
            cancellationToken).ConfigureAwait(false);
        var result = CompanionAuthCodec.ParseResult(response);
        if (!result.IsAuthenticated)
        {
            throw new UnauthorizedAccessException(
                $"CLIP STUDIO rejected companion authentication: {result.ErrorReason ?? "unknown reason"}.");
        }

        Volatile.Write(ref isAuthenticated, true);
        heartbeatTask ??= Task.Run(() => HeartbeatLoopAsync(lifetimeCancellation.Token));
        return result;
    }

    public Task<CompanionFrame> SendAsync(
        string command,
        object? detail = null,
        CancellationToken cancellationToken = default)
    {
        var rawDetail = detail switch
        {
            null => [],
            byte[] value => value,
            _ => JsonSerializer.SerializeToUtf8Bytes(detail),
        };
        return SendRawAsync(command, rawDetail, cancellationToken);
    }

    public async Task<CompanionFrame> SendRawAsync(
        string command,
        ReadOnlyMemory<byte> rawDetail,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAuthenticated && command != AuthenticateCommand)
        {
            throw new InvalidOperationException("The companion client has not been authenticated.");
        }

        var serial = unchecked((uint)Interlocked.Increment(ref nextSerial));
        var completion = new TaskCompletionSource<CompanionFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(serial, completion))
        {
            throw new InvalidOperationException($"A request with serial {serial} is already pending.");
        }

        try
        {
            var frame = CompanionFrameCodec.EncodeRaw(
                CompanionFrameType.Command,
                command,
                serial,
                rawDetail.Span);
            await WriteFrameAsync(frame, cancellationToken).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(DefaultRequestTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token,
                timeout.Token);
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            pending.TryRemove(serial, out _);
        }
    }

    public Task<CompanionFrame> SendHeartbeatAsync(
        bool resetIdleTimer = false,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HeartbeatCommand,
            resetIdleTimer ? new { IdleTimerResetRequested = true } : null,
            cancellationToken);

    public async Task SetCurrentColorRgbAsync(
        byte red,
        byte green,
        byte blue,
        bool transparent = false,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            SetCurrentColorCommand,
            new
            {
                IsColorTransparent = transparent,
                RGBColorR = (int)red,
                RGBColorG = (int)green,
                RGBColorB = (int)blue,
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Type != CompanionFrameType.Success)
        {
            throw new InvalidDataException("CLIP STUDIO rejected the drawing-color change.");
        }
    }

    /// <summary>
    /// Reads the host's Quick Access sets and returns their command items in wire order.
    /// Tool and drawing-color items are not included in the returned command collection.
    /// </summary>
    public async Task<CompanionQuickAccessData> GetQuickAccessDataAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            GetQuickAccessDataCommand,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return CompanionQuickAccessData.Deserialize(response);
    }

    /// <summary>
    /// Invokes a Quick Access command using the exact command type and name reported by
    /// <see cref="GetQuickAccessDataAsync"/>.
    /// </summary>
    public Task DoQuickAccessCommandAsync(
        CompanionQuickAccessCommandIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return DoQuickAccessCommandAsync(
            identity.CommandType,
            identity.CommandName,
            cancellationToken);
    }

    /// <summary>
    /// Invokes a Quick Access command using the exact command type and name reported by
    /// <see cref="GetQuickAccessDataAsync"/>.
    /// </summary>
    public async Task DoQuickAccessCommandAsync(
        string commandType,
        string commandName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        var response = await SendAsync(
            DoQuickAccessWireCommand,
            new
            {
                ItemType = "Command",
                ItemCommandType = commandType,
                ItemCommandName = commandName,
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Type != CompanionFrameType.Success)
        {
            throw new InvalidDataException(
                $"CLIP STUDIO rejected Quick Access command '{commandType}/{commandName}'.");
        }
    }

    public async Task<CompanionPreviewResponse> SyncPreviewAsync(CancellationToken cancellationToken = default)
    {
        var frame = await SendAsync(
            PreviewCommand,
            new { Operation = "SyncPreview" },
            cancellationToken).ConfigureAwait(false);
        return DeserializePreview(frame);
    }

    public async Task<CompanionPreviewResponse> UpdateGalleryAsync(
        int maximumLength = 100,
        CancellationToken cancellationToken = default)
    {
        if (maximumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        var frame = await SendAsync(
            PreviewCommand,
            new { Operation = "UpdateGallery", MaxLength = maximumLength },
            cancellationToken).ConfigureAwait(false);
        return DeserializePreview(frame);
    }

    public async Task<CompanionPreviewBlock> ReadBlockAsync(
        int galleryIdentificationNumber,
        int canvasIndex,
        CanvasTile tile,
        CancellationToken cancellationToken = default)
    {
        if (tile.Width <= 0 || tile.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tile));
        }

        var frame = await SendAsync(
            PreviewCommand,
            new
            {
                Operation = "ReadPreviewBlock",
                GalleryIdentificationNumber = galleryIdentificationNumber,
                CanvasIndex = canvasIndex,
                BlockLeft = tile.Left,
                BlockTop = tile.Top,
                BlockRight = tile.Right,
                BlockBottom = tile.Bottom,
                BlockIndex = tile.Index,
            },
            cancellationToken).ConfigureAwait(false);

        var response = DeserializePreview(frame);
        byte[] encodedPixels;
        try
        {
            encodedPixels = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(frame.BinaryTail));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Preview pixel tail is not valid base64.", ex);
        }

        var expectedRgbLength = checked(tile.PixelCount * 3);
        if (encodedPixels.Length != expectedRgbLength)
        {
            throw new InvalidDataException(
                $"Preview block requires {expectedRgbLength} RGB bytes but received {encodedPixels.Length}.");
        }

        var rgba = new byte[checked(tile.PixelCount * 4)];
        for (int source = 0, destination = 0; source < encodedPixels.Length; source += 3, destination += 4)
        {
            rgba[destination] = encodedPixels[source];
            rgba[destination + 1] = encodedPixels[source + 1];
            rgba[destination + 2] = encodedPixels[source + 2];
            rgba[destination + 3] = 0xFF;
        }

        return new CompanionPreviewBlock(response, tile, rgba);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref isAuthenticated, false);
        await lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        tcpClient?.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);

        var closed = new ObjectDisposedException(nameof(CompanionModeClient));
        foreach (var completion in pending.Values)
        {
            completion.TrySetException(closed);
        }

        if (receiveTask is not null)
        {
            await IgnoreCancellationAsync(receiveTask).ConfigureAwait(false);
        }

        if (heartbeatTask is not null)
        {
            await IgnoreCancellationAsync(heartbeatTask).ConfigureAwait(false);
        }

        writeLock.Dispose();
        lifetimeCancellation.Dispose();
    }

    private static CompanionPreviewResponse DeserializePreview(CompanionFrame frame) =>
        frame.DeserializeDetail<CompanionPreviewResponse>() ??
        throw new InvalidDataException("Preview response has no detail body.");

    private void StartReceiveLoop() =>
        receiveTask = Task.Run(() => ReceiveLoopAsync(lifetimeCancellation.Token));

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await CompanionFrameCodec.ReadAsync(
                    stream,
                    maximumFrameLength,
                    cancellationToken).ConfigureAwait(false);
                if (frame.Type == CompanionFrameType.Command)
                {
                    await AcknowledgeServerPushAsync(frame, cancellationToken).ConfigureAwait(false);
                    ServerPushReceived?.Invoke(this, new CompanionServerPushEventArgs(frame));
                }
                else if (pending.TryGetValue(frame.Serial, out var completion))
                {
                    completion.TrySetResult(frame);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                MarkDisconnected(ex);
            }
        }
    }

    private async Task AcknowledgeServerPushAsync(CompanionFrame frame, CancellationToken cancellationToken)
    {
        var acknowledgement = CompanionFrameCodec.EncodeRaw(
            CompanionFrameType.Success,
            frame.Command,
            frame.Serial,
            []);
        await WriteFrameAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(heartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SendHeartbeatAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (
            ex is IOException or EndOfStreamException or SocketException or TimeoutException or TaskCanceledException)
        {
            MarkDisconnected(ex);
        }
    }

    private async Task WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private void MarkDisconnected(Exception exception)
    {
        Volatile.Write(ref isAuthenticated, false);
        foreach (var completion in pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }
}
