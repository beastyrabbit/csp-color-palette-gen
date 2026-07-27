using CspPaletteCompanion.App;

namespace CspPaletteCompanion.App.Tests;

public sealed class CspLocatorTests
{
    [Theory]
    [InlineData("Painting — 1920 x 1080 px", 1920, 1080)]
    [InlineData("Painting — 800 × 600 px", 800, 600)]
    public void ParseCanvasSize_ReadsSupportedTitleFormats(
        string title,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(
            (expectedWidth, expectedHeight),
            CspLocator.ParseCanvasSize(title));
    }

    [Fact]
    public void ParseCanvasSize_RejectsValuesOutsideInt32()
    {
        Assert.Null(CspLocator.ParseCanvasSize(
            "Painting — 999999999999999999999 x 1080 px"));
    }
}
