using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.Core.Tests;

public sealed class PaletteExtractionOptionsTests
{
    [Fact]
    public void Defaults_AreSixMajorAndSixMinorColors()
    {
        var options = new PaletteExtractionOptions();

        Assert.Equal(6, options.MajorColorCount);
        Assert.Equal(6, options.MinorColorCount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(21, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 21)]
    public void Constructor_RejectsCountsOutsideProductRange(int major, int minor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaletteExtractionOptions(major, minor));
    }
}
