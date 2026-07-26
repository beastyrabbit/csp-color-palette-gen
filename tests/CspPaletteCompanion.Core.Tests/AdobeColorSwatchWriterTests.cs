using System.Buffers.Binary;
using System.Text;
using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.Core.Tests;

public sealed class AdobeColorSwatchWriterTests
{
    [Fact]
    public void Write_ProducesCompatibilityAndNamedSectionsInBigEndian()
    {
        var colors = new[]
        {
            new NamedColor("Major 01", new RgbColor(0x12, 0x34, 0x56)),
            new NamedColor("Mínor 01", new RgbColor(0xAB, 0xCD, 0xEF)),
        };

        var bytes = AdobeColorSwatchWriter.Write(colors);
        var reader = new AcoReader(bytes);

        Assert.Equal((ushort)1, reader.ReadUInt16());
        Assert.Equal((ushort)2, reader.ReadUInt16());
        AssertRgbEntry(reader, colors[0].Color);
        AssertRgbEntry(reader, colors[1].Color);

        Assert.Equal((ushort)2, reader.ReadUInt16());
        Assert.Equal((ushort)2, reader.ReadUInt16());
        AssertRgbEntry(reader, colors[0].Color);
        Assert.Equal("Major 01", reader.ReadName());
        AssertRgbEntry(reader, colors[1].Color);
        Assert.Equal("Mínor 01", reader.ReadName());
        Assert.True(reader.AtEnd);
    }

    [Fact]
    public void Write_UsesStableMajorThenMinorLabelsFromExtraction()
    {
        var image = TestImage.Blocks(
            (new RgbColor(200, 40, 50), 10),
            (new RgbColor(40, 80, 200), 5));
        var result = new PaletteExtractor().Extract(
            image,
            new PaletteExtractionOptions(1, 1));

        var bytes = AdobeColorSwatchWriter.Write(result);
        var names = ReadVersionTwoNames(bytes);

        Assert.Equal(new[] { "Major 01", "Minor 01" }, names);
    }

    [Fact]
    public void Write_RejectsAnEmptyPalette()
    {
        Assert.Throws<ArgumentException>(
            () => AdobeColorSwatchWriter.Write(Array.Empty<NamedColor>()));
    }

    private static string[] ReadVersionTwoNames(byte[] bytes)
    {
        var reader = new AcoReader(bytes);
        Assert.Equal((ushort)1, reader.ReadUInt16());
        var versionOneCount = reader.ReadUInt16();
        reader.Skip(versionOneCount * 10);
        Assert.Equal((ushort)2, reader.ReadUInt16());
        var versionTwoCount = reader.ReadUInt16();
        var names = new List<string>(versionTwoCount);

        for (var index = 0; index < versionTwoCount; index++)
        {
            reader.Skip(10);
            names.Add(reader.ReadName());
        }

        return names.ToArray();
    }

    private static void AssertRgbEntry(AcoReader reader, RgbColor expected)
    {
        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)((expected.Red << 8) | expected.Red), reader.ReadUInt16());
        Assert.Equal((ushort)((expected.Green << 8) | expected.Green), reader.ReadUInt16());
        Assert.Equal((ushort)((expected.Blue << 8) | expected.Blue), reader.ReadUInt16());
        Assert.Equal((ushort)0, reader.ReadUInt16());
    }

    private sealed class AcoReader
    {
        private readonly byte[] _bytes;
        private int _offset;

        public AcoReader(byte[] bytes)
        {
            _bytes = bytes;
        }

        public bool AtEnd => _offset == _bytes.Length;

        public ushort ReadUInt16()
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(_offset, 2));
            _offset += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(_offset, 4));
            _offset += 4;
            return value;
        }

        public string ReadName()
        {
            var codeUnitCount = checked((int)ReadUInt32());
            var byteCountWithoutTerminator = checked((codeUnitCount - 1) * 2);
            var value = Encoding.BigEndianUnicode.GetString(
                _bytes,
                _offset,
                byteCountWithoutTerminator);
            _offset += byteCountWithoutTerminator;
            Assert.Equal((ushort)0, ReadUInt16());
            return value;
        }

        public void Skip(int count)
        {
            _offset += count;
        }
    }
}
