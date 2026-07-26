using System.Text;

namespace CspPaletteCompanion.Companion.Tests;

public sealed class CompanionFrameCodecTests
{
    [Fact]
    public void EncodeAndDecode_RoundTripsJsonAndBinaryTail()
    {
        var raw = """{"Operation":"ReadPreviewBlock","BlockIndex":7}"""u8.ToArray();
        var tail = Encoding.ASCII.GetBytes("AQIDBA==");
        var encoded = CompanionFrameCodec.EncodeRaw(
            CompanionFrameType.Success,
            "PreviewWebtoonFromClient",
            42,
            raw,
            tail);

        Assert.True(CompanionFrameCodec.TryDecode(encoded, out var decoded, out var consumed));

        Assert.Equal(encoded.Length, consumed);
        Assert.NotNull(decoded);
        Assert.Equal(CompanionFrameType.Success, decoded.Type);
        Assert.Equal("PreviewWebtoonFromClient", decoded.Command);
        Assert.Equal((uint)42, decoded.Serial);
        Assert.Equal(7, decoded.Detail?.GetProperty("BlockIndex").GetInt32());
        Assert.Equal(raw, decoded.RawDetail);
        Assert.Equal(tail, decoded.BinaryTail);
    }

    [Fact]
    public void TryDecode_IncompleteAndConcatenatedBuffers_IsStreamingSafe()
    {
        var first = CompanionFrameCodec.Encode(CompanionFrameType.Command, "First", 1, new { A = 1 });
        var second = CompanionFrameCodec.Encode(CompanionFrameType.Success, "Second", 2);
        var combined = first.Concat(second).ToArray();

        Assert.False(CompanionFrameCodec.TryDecode(first.AsSpan(0, first.Length - 1), out _, out var none));
        Assert.Equal(0, none);
        Assert.True(CompanionFrameCodec.TryDecode(combined, out var decoded, out var consumed));
        Assert.Equal("First", decoded?.Command);
        Assert.Equal(first.Length, consumed);
        Assert.True(CompanionFrameCodec.TryDecode(combined.AsSpan(consumed), out decoded, out _));
        Assert.Equal("Second", decoded?.Command);
    }

    [Fact]
    public async Task ReadAsync_HandlesOneByteReads()
    {
        var encoded = CompanionFrameCodec.Encode(
            CompanionFrameType.Command,
            "Fragmented",
            uint.MaxValue,
            new[] { true, false });
        await using var stream = new OneByteAtATimeStream(encoded);

        var decoded = await CompanionFrameCodec.ReadAsync(stream);

        Assert.Equal("Fragmented", decoded.Command);
        Assert.Equal(uint.MaxValue, decoded.Serial);
    }

    [Fact]
    public async Task ReadAsync_PreservesCoalescedFrameAfterTerminator()
    {
        var first = CompanionFrameCodec.Encode(CompanionFrameType.Command, "First", 1);
        var second = CompanionFrameCodec.Encode(CompanionFrameType.Success, "Second", 2, new { Ok = true });
        await using var stream = new CountingMemoryStream(first.Concat(second).ToArray());

        var decodedFirst = await CompanionFrameCodec.ReadAsync(stream);
        var decodedSecond = await CompanionFrameCodec.ReadAsync(stream);

        Assert.Equal("First", decodedFirst.Command);
        Assert.Equal("Second", decodedSecond.Command);
        Assert.Equal(1, stream.ReadCount);
    }

    [Fact]
    public void Decode_RejectsInvalidVersionAndUnknownType()
    {
        var valid = CompanionFrameCodec.Encode(CompanionFrameType.Command, "X", 0);
        var badVersion = valid.ToArray();
        badVersion[10] = (byte)'X';
        var badType = valid.ToArray();
        badType[0] = 0x7F;

        Assert.Throws<InvalidDataException>(() =>
            CompanionFrameCodec.TryDecode(badVersion, out _, out _));
        Assert.Throws<InvalidDataException>(() =>
            CompanionFrameCodec.TryDecode(badType, out _, out _));
    }

    [Fact]
    public void Decode_EnforcesMaximumLength()
    {
        var frame = CompanionFrameCodec.Encode(
            CompanionFrameType.Command,
            "Large",
            0,
            new { Value = new string('x', 500) });

        Assert.Throws<InvalidDataException>(() =>
            CompanionFrameCodec.TryDecode(frame, out _, out _, 128));
        Assert.Throws<InvalidDataException>(() =>
            CompanionFrameCodec.TryDecode(new byte[129], out _, out _, 128));
    }

    private sealed class OneByteAtATimeStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..1], cancellationToken);
    }

    private sealed class CountingMemoryStream(byte[] data) : MemoryStream(data)
    {
        public int ReadCount { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
