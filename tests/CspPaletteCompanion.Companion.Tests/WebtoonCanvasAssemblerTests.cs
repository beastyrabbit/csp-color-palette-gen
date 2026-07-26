using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CspPaletteCompanion.Companion.Tests;

public sealed class WebtoonCanvasAssemblerTests
{
    [Fact]
    public async Task ReadCanvasAsync_FetchesIndependentBlocksConcurrentlyAndAssemblesByPosition()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        var host = RunConcurrentBlockHostAsync(listener, timeout.Token);
        var pairing = new CompanionPairingInfo(
            [IPAddress.Loopback],
            checked((ushort)endpoint.Port),
            "pairing-password",
            "G#1:2026");

        await using var client = await CompanionModeClient.ConnectAndAuthenticateAsync(
            pairing,
            timeout.Token);
        var assembler = new WebtoonCanvasAssembler(
            client,
            maximumPixelsPerTile: 1,
            maximumTileSide: 1,
            maximumConcurrency: 4);

        var canvas = await assembler.ReadCanvasAsync(cancellationToken: timeout.Token);

        Assert.Equal(4, canvas.Width);
        Assert.Equal(1, canvas.Height);
        Assert.Equal(
            new byte[]
            {
                10, 20, 30, 255,
                11, 21, 31, 255,
                12, 22, 32, 255,
                13, 23, 33, 255,
            },
            canvas.Pixels);
        await host.WaitAsync(timeout.Token);
        listener.Stop();
    }

    [Fact]
    public void SelectCanvasIndex_SingleEntry_DoesNotNeedExpectedDimensions()
    {
        var gallery = new[] { new CompanionCanvasSize(1200, 1800) };
        Assert.Equal(0, WebtoonCanvasAssembler.SelectCanvasIndex(gallery));
    }

    [Fact]
    public void SelectCanvasIndex_MultipleEntries_RequiresUniqueDimensionMatch()
    {
        var gallery = new[]
        {
            new CompanionCanvasSize(100, 200),
            new CompanionCanvasSize(300, 400),
            new CompanionCanvasSize(500, 600),
        };

        Assert.Equal(1, WebtoonCanvasAssembler.SelectCanvasIndex(gallery, 300, 400));
        Assert.Throws<InvalidOperationException>(() =>
            WebtoonCanvasAssembler.SelectCanvasIndex(gallery));
        Assert.Throws<InvalidOperationException>(() =>
            WebtoonCanvasAssembler.SelectCanvasIndex(gallery, 1, 2));
    }

    [Fact]
    public void SelectCanvasIndex_DuplicateDimensionMatch_RejectsAmbiguity()
    {
        var gallery = new[]
        {
            new CompanionCanvasSize(100, 200),
            new CompanionCanvasSize(100, 200),
        };

        Assert.Throws<InvalidOperationException>(() =>
            WebtoonCanvasAssembler.SelectCanvasIndex(gallery, 100, 200));
    }

    private static async Task RunConcurrentBlockHostAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = tcp.GetStream();

        var authentication = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        await WriteAsync(
            stream,
            authentication,
            """{"AuthErrorReason":"Unknown","RemoteCommandSpecVersionOfServer":"1.0"}"""u8.ToArray(),
            cancellationToken: cancellationToken);

        var sync = await CompanionFrameCodec.ReadAsync(stream, cancellationToken: cancellationToken);
        await WriteAsync(
            stream,
            sync,
            """{"Operation":"SyncPreview"}"""u8.ToArray(),
            cancellationToken: cancellationToken);

        var gallery = await CompanionFrameCodec.ReadAsync(stream, cancellationToken: cancellationToken);
        await WriteAsync(
            stream,
            gallery,
            """{"Operation":"UpdateGallery","CanvasCount":1,"CanvasSizeArray":[{"CanvasWidth":4,"CanvasHeight":1}],"GalleryIdentificationNumber":77}"""u8.ToArray(),
            cancellationToken: cancellationToken);

        // Deliberately withhold every response until all four requests arrive.
        // A sequential implementation deadlocks here and fails the timeout.
        var reads = new List<CompanionFrame>();
        for (var index = 0; index < 4; index++)
        {
            reads.Add(await CompanionFrameCodec.ReadAsync(
                stream,
                cancellationToken: cancellationToken));
        }

        foreach (var read in reads.OrderByDescending(
                     item => item.Detail!.Value.GetProperty("BlockIndex").GetInt32()))
        {
            var detail = read.Detail!.Value;
            var blockIndex = detail.GetProperty("BlockIndex").GetInt32();
            var rgb = new byte[]
            {
                checked((byte)(10 + blockIndex)),
                checked((byte)(20 + blockIndex)),
                checked((byte)(30 + blockIndex)),
            };
            var responseDetail = Encoding.UTF8.GetBytes($$"""
                {"Operation":"ReadPreviewBlock","BlockLeft":{{blockIndex}},"BlockTop":0,"BlockRight":{{blockIndex + 1}},"BlockBottom":1,"BlockIndex":{{blockIndex}}}
                """);
            await WriteAsync(
                stream,
                read,
                responseDetail,
                Encoding.ASCII.GetBytes(Convert.ToBase64String(rgb)),
                cancellationToken);
        }
    }

    private static async Task WriteAsync(
        Stream stream,
        CompanionFrame request,
        ReadOnlyMemory<byte> detail,
        ReadOnlyMemory<byte> tail = default,
        CancellationToken cancellationToken = default)
    {
        var response = CompanionFrameCodec.EncodeRaw(
            CompanionFrameType.Success,
            request.Command,
            request.Serial,
            detail.Span,
            tail.Span);
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
