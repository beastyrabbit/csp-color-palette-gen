using CspPaletteCompanion.Core.Imaging;

namespace CspPaletteCompanion.Core.Tests;

public sealed class RgbaImageTests
{
    [Fact]
    public void Constructor_CopiesPixelBuffer()
    {
        var pixels = new byte[] { 1, 2, 3, 255 };
        var image = new RgbaImage(1, 1, pixels);

        pixels[0] = 99;

        Assert.Equal(1, image.Pixels.Span[0]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Constructor_RejectsNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RgbaImage(width, height, Array.Empty<byte>()));
    }

    [Fact]
    public void Constructor_RejectsIncorrectBufferLength()
    {
        Assert.Throws<ArgumentException>(
            () => new RgbaImage(2, 2, new byte[15]));
    }
}
