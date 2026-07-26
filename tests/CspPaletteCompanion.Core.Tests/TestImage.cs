using CspPaletteCompanion.Core.Imaging;
using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.Core.Tests;

internal static class TestImage
{
    public static RgbaImage FromColors(params RgbColor[] colors) =>
        FromPixels(colors.Select(color =>
            (color.Red, color.Green, color.Blue, (byte)255)).ToArray());

    public static RgbaImage FromPixels(params (byte Red, byte Green, byte Blue, byte Alpha)[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (var index = 0; index < pixels.Length; index++)
        {
            bytes[index * 4] = pixels[index].Red;
            bytes[(index * 4) + 1] = pixels[index].Green;
            bytes[(index * 4) + 2] = pixels[index].Blue;
            bytes[(index * 4) + 3] = pixels[index].Alpha;
        }

        return new RgbaImage(pixels.Length, 1, bytes);
    }

    public static RgbaImage Blocks(params (RgbColor Color, int EligiblePixelCount)[] blocks)
    {
        var colors = new List<RgbColor>();
        foreach (var (color, count) in blocks)
        {
            // Five copies ensure each requested sampled color occurs once per
            // count while honoring the extractor's every-fifth sampling rule.
            colors.AddRange(Enumerable.Repeat(color, count * 5));
        }

        return FromColors(colors.ToArray());
    }
}
