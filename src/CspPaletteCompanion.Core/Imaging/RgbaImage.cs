namespace CspPaletteCompanion.Core.Imaging;

/// <summary>
/// An immutable, row-major, non-premultiplied 8-bit RGBA image. Callers are
/// responsible for converting profiled source pixels to sRGB before construction.
/// </summary>
public sealed class RgbaImage
{
    private readonly byte[] _pixels;

    public RgbaImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        var expectedLength = checked(width * height * 4);
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected exactly {expectedLength} RGBA bytes for a {width}x{height} image.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Pixels => _pixels;
}
