using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CspPaletteCompanion.Companion.Tests;

public sealed class CompanionModeClientIntegrationTests
{
    [Fact]
    public async Task LoopbackHost_AuthenticatesAssemblesCanvasAcksPushAndHeartbeats()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        var releasePush = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pushAcknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = RunFakeHostAsync(
            listener,
            releasePush.Task,
            pushAcknowledged,
            timeout.Token);
        var pairing = new CompanionPairingInfo(
            [IPAddress.Loopback],
            checked((ushort)endpoint.Port),
            "pairing-password",
            "G#1:2026");

        await using var client = await CompanionModeClient.ConnectAndAuthenticateAsync(pairing, timeout.Token);
        var pushReceived = new TaskCompletionSource<CompanionFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.ServerPushReceived += (_, _) =>
            throw new InvalidOperationException("A consumer callback must not stop the receive loop.");
        client.ServerPushReceived += (_, args) => pushReceived.TrySetResult(args.Frame);

        var assembler = new WebtoonCanvasAssembler(client);
        var canvas = await assembler.ReadCanvasAsync(cancellationToken: timeout.Token);

        Assert.Equal(2, canvas.Width);
        Assert.Equal(1, canvas.Height);
        Assert.Equal(
            new byte[] { 255, 0, 0, 255, 0, 128, 255, 255 },
            canvas.Pixels);

        await client.SetCurrentColorRgbAsync(0x33, 0x66, 0xff, cancellationToken: timeout.Token);

        releasePush.SetResult();
        var push = await pushReceived.Task.WaitAsync(timeout.Token);
        Assert.Equal("SyncColorCircleUIState", push.Command);
        await pushAcknowledged.Task.WaitAsync(timeout.Token);
        await serverTask.WaitAsync(timeout.Token);
        listener.Stop();
    }

    [Fact]
    public async Task MalformedFrameMarksAnAuthenticatedClientDisconnected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        var releaseMalformedFrame = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = tcp.GetStream();
            var authentication = await CompanionFrameCodec.ReadAsync(
                stream,
                cancellationToken: timeout.Token);
            await WriteAsync(
                stream,
                CompanionFrameType.Success,
                authentication,
                """{"AuthErrorReason":"Unknown"}"""u8.ToArray(),
                cancellationToken: timeout.Token);

            await releaseMalformedFrame.Task.WaitAsync(timeout.Token);
            var malformed = CompanionFrameCodec.EncodeRaw(
                CompanionFrameType.Success,
                "Malformed",
                99,
                "not-json"u8);
            await stream.WriteAsync(malformed, timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }, timeout.Token);

        try
        {
            var pairing = new CompanionPairingInfo(
                [IPAddress.Loopback],
                checked((ushort)endpoint.Port),
                "pairing-password",
                "G#1:2026");
            await using var client = await CompanionModeClient.ConnectAndAuthenticateAsync(
                pairing,
                timeout.Token);
            Assert.True(client.IsAuthenticated);

            releaseMalformedFrame.SetResult();
            while (client.IsAuthenticated)
            {
                await Task.Delay(20, timeout.Token);
            }

            Assert.False(client.IsAuthenticated);
            await serverTask.WaitAsync(timeout.Token);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RunFakeHostAsync(
        TcpListener listener,
        Task releasePush,
        TaskCompletionSource pushAcknowledged,
        CancellationToken cancellationToken)
    {
        using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = tcp.GetStream();

        var authentication = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Equal(CompanionFrameType.Command, authentication.Type);
        Assert.Equal("Authenticate", authentication.Command);
        Assert.Equal((uint)0, authentication.Serial);
        var authenticationFields = authentication.Detail?.EnumerateArray().ToArray();
        Assert.NotNull(authenticationFields);
        Assert.Equal("G#1:2026", authenticationFields[0].GetString());
        Assert.Equal(
            "pairing-password",
            CompanionAuthCodec.Decrypt(Assert.IsType<string>(authenticationFields[1].GetString())));
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            authentication,
            """{"AuthErrorReason":"Unknown","RemoteCommandSpecVersionOfServer":"1.0","IsQuickAccessAvailable":true}"""u8.ToArray(),
            cancellationToken: cancellationToken);

        var sync = await CompanionFrameCodec.ReadAsync(stream, cancellationToken: cancellationToken);
        AssertPreviewOperation(sync, 1, "SyncPreview");
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            sync,
            """{"Operation":"SyncPreview"}"""u8.ToArray(),
            cancellationToken: cancellationToken);

        var gallery = await CompanionFrameCodec.ReadAsync(stream, cancellationToken: cancellationToken);
        AssertPreviewOperation(gallery, 2, "UpdateGallery");
        Assert.Equal(100, gallery.Detail?.GetProperty("MaxLength").GetInt32());
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            gallery,
            """{"Operation":"UpdateGallery","CanvasCount":1,"CanvasSizeArray":[{"CanvasWidth":2,"CanvasHeight":1}],"GalleryIdentificationNumber":77}"""u8.ToArray(),
            cancellationToken: cancellationToken);

        var read = await CompanionFrameCodec.ReadAsync(stream, cancellationToken: cancellationToken);
        AssertPreviewOperation(read, 3, "ReadPreviewBlock");
        Assert.Equal(77, read.Detail?.GetProperty("GalleryIdentificationNumber").GetInt32());
        Assert.Equal(2, read.Detail?.GetProperty("BlockRight").GetInt32());
        var rgb = new byte[] { 255, 0, 0, 0, 128, 255 };
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            read,
            """{"Operation":"ReadPreviewBlock","BlockLeft":0,"BlockTop":0,"BlockRight":2,"BlockBottom":1,"BlockIndex":0}"""u8.ToArray(),
            Encoding.ASCII.GetBytes(Convert.ToBase64String(rgb)),
            cancellationToken);

        var setColor = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Equal(CompanionFrameType.Command, setColor.Type);
        Assert.Equal("SetCurrentColor", setColor.Command);
        Assert.Equal((uint)4, setColor.Serial);
        var colorDetail = Assert.IsType<JsonElement>(setColor.Detail);
        Assert.False(colorDetail.GetProperty("IsColorTransparent").GetBoolean());
        Assert.Equal(0x33, colorDetail.GetProperty("RGBColorR").GetInt32());
        Assert.Equal(0x66, colorDetail.GetProperty("RGBColorG").GetInt32());
        Assert.Equal(0xff, colorDetail.GetProperty("RGBColorB").GetInt32());
        Assert.False(colorDetail.TryGetProperty("ColorSpaceKind", out _));
        Assert.False(colorDetail.TryGetProperty("ColorIndex", out _));
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            setColor,
            Array.Empty<byte>(),
            cancellationToken: cancellationToken);

        await releasePush.WaitAsync(cancellationToken);
        var pushBytes = CompanionFrameCodec.EncodeRaw(
            CompanionFrameType.Command,
            "SyncColorCircleUIState",
            91,
            "{}"u8);
        await stream.WriteAsync(pushBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var acknowledgement = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Equal(CompanionFrameType.Success, acknowledgement.Type);
        Assert.Equal("SyncColorCircleUIState", acknowledgement.Command);
        Assert.Equal((uint)91, acknowledgement.Serial);
        pushAcknowledged.SetResult();

        var heartbeat = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Equal(CompanionFrameType.Command, heartbeat.Type);
        Assert.Equal("TellHeartbeat", heartbeat.Command);
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            heartbeat,
            Array.Empty<byte>(),
            cancellationToken: cancellationToken);
    }

    private static void AssertPreviewOperation(
        CompanionFrame frame,
        uint expectedSerial,
        string expectedOperation)
    {
        Assert.Equal(CompanionFrameType.Command, frame.Type);
        Assert.Equal("PreviewWebtoonFromClient", frame.Command);
        Assert.Equal(expectedSerial, frame.Serial);
        Assert.Equal(expectedOperation, frame.Detail?.GetProperty("Operation").GetString());
    }

    private static async Task WriteAsync(
        Stream stream,
        CompanionFrameType type,
        CompanionFrame request,
        ReadOnlyMemory<byte> detail,
        ReadOnlyMemory<byte> tail = default,
        CancellationToken cancellationToken = default)
    {
        var response = CompanionFrameCodec.EncodeRaw(
            type,
            request.Command,
            request.Serial,
            detail.Span,
            tail.Span);
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
