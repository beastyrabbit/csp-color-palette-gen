using System.Buffers.Binary;
using System.Text;

namespace CspPaletteCompanion.Core.Palette;

/// <summary>
/// Writes Adobe Color Swatch files with a compatibility v1 section followed by
/// a named v2 section. All colors are encoded in the ACO RGB color space.
/// </summary>
public static class AdobeColorSwatchWriter
{
    private const ushort VersionOne = 1;
    private const ushort VersionTwo = 2;
    private const ushort RgbColorSpace = 0;

    public static byte[] Write(IReadOnlyList<NamedColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count == 0)
        {
            throw new ArgumentException("At least one color is required.", nameof(colors));
        }

        if (colors.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(colors),
                $"ACO supports at most {ushort.MaxValue} colors per section.");
        }

        using var stream = new MemoryStream();
        WriteSectionHeader(stream, VersionOne, colors.Count);
        foreach (var color in colors)
        {
            ArgumentNullException.ThrowIfNull(color);
            WriteRgbEntry(stream, color.Color);
        }

        WriteSectionHeader(stream, VersionTwo, colors.Count);
        foreach (var color in colors)
        {
            WriteRgbEntry(stream, color.Color);
            WriteName(stream, color.Name);
        }

        return stream.ToArray();
    }

    public static byte[] Write(PaletteExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Write(result.ToNamedColors());
    }

    private static void WriteSectionHeader(Stream stream, ushort version, int count)
    {
        WriteUInt16BigEndian(stream, version);
        WriteUInt16BigEndian(stream, checked((ushort)count));
    }

    private static void WriteRgbEntry(Stream stream, RgbColor color)
    {
        WriteUInt16BigEndian(stream, RgbColorSpace);
        WriteUInt16BigEndian(stream, ExpandByte(color.Red));
        WriteUInt16BigEndian(stream, ExpandByte(color.Green));
        WriteUInt16BigEndian(stream, ExpandByte(color.Blue));
        WriteUInt16BigEndian(stream, 0);
    }

    private static ushort ExpandByte(byte value) =>
        (ushort)((value << 8) | value);

    private static void WriteName(Stream stream, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var codeUnitCountIncludingTerminator = checked((uint)name.Length + 1);
        WriteUInt32BigEndian(stream, codeUnitCountIncludingTerminator);

        var encodedName = Encoding.BigEndianUnicode.GetBytes(name);
        stream.Write(encodedName);
        WriteUInt16BigEndian(stream, 0);
    }

    private static void WriteUInt16BigEndian(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
