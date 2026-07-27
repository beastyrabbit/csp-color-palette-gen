using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.Core.Tests;

public sealed class PaletteExtractorTests
{
    private readonly PaletteExtractor _extractor = new();

    [Fact]
    public void Extract_IsDeterministicForIdenticalPixelsAndSettings()
    {
        var image = TestImage.Blocks(
            (new RgbColor(210, 45, 55), 20),
            (new RgbColor(50, 170, 80), 12),
            (new RgbColor(55, 75, 210), 8),
            (new RgbColor(220, 170, 40), 4));
        var options = new PaletteExtractionOptions(3, 1);

        var first = _extractor.Extract(image, options);
        var second = _extractor.Extract(image, options);

        Assert.Equal(first.MajorColors, second.MajorColors);
        Assert.Equal(first.MinorColors, second.MinorColors);
    }

    [Fact]
    public void Extract_OrdersMajorColorsByClusterPopulation()
    {
        var dominant = new RgbColor(200, 30, 30);
        var supporting = new RgbColor(30, 180, 30);
        var accent = new RgbColor(30, 30, 200);
        var image = TestImage.Blocks(
            (dominant, 12),
            (supporting, 7),
            (accent, 2));

        var result = _extractor.Extract(
            image,
            new PaletteExtractionOptions(3, 0));

        Assert.Equal(new[] { dominant, supporting, accent }, result.MajorColors);
        Assert.Empty(result.MinorColors);
    }

    [Fact]
    public void Extract_SkipsMinorPassWhenZeroAreRequested()
    {
        var image = TestImage.Blocks(
            (new RgbColor(200, 50, 50), 2),
            (new RgbColor(50, 200, 50), 1));

        var result = _extractor.Extract(
            image,
            new PaletteExtractionOptions(1, 0));

        Assert.Single(result.MajorColors);
        Assert.Empty(result.MinorColors);
    }

    [Fact]
    public void Extract_FiltersTransparentNearBlackAndNearWhitePixels()
    {
        var eligible = new RgbColor(120, 80, 40);
        var pixels = new List<(byte, byte, byte, byte)>();

        for (var index = 0; index < 5; index++)
        {
            pixels.Add((1, 1, 1, 255));
            pixels.Add((254, 254, 254, 255));
            pixels.Add((200, 20, 20, 127));
            pixels.Add((eligible.Red, eligible.Green, eligible.Blue, 128));
        }

        var result = _extractor.Extract(
            TestImage.FromPixels(pixels.ToArray()),
            new PaletteExtractionOptions(1, 0));

        Assert.Equal(new[] { eligible }, result.MajorColors);
        Assert.Equal(5, result.EligiblePixelCount);
        Assert.Equal(1, result.SampledPixelCount);
    }

    [Fact]
    public void Extract_ThrowsWhenNoEligiblePixelsExist()
    {
        var image = TestImage.FromPixels(
            (0, 0, 0, 255),
            (255, 255, 255, 255),
            (100, 100, 100, 0));

        Assert.Throws<NoEligiblePixelsException>(
            () => _extractor.Extract(image, new PaletteExtractionOptions(1, 0)));
    }

    [Fact]
    public void BgraPreflight_RejectsLargeTransparentBlackPayloadWithoutAllocatingAnRgbaCopy()
    {
        var preflight = new BgraEligibilityPreflight();
        var chunk = new byte[4096 * 4];

        for (var index = 0; index < 1024; index++)
        {
            preflight.Observe(chunk);
        }

        Assert.True(preflight.IsDefinitelyIneligible);
        Assert.False(preflight.CanStopScanning);
    }

    [Fact]
    public void BgraPreflight_DoesNotRejectBlackWhiteMixtureThatCanInterpolateToGray()
    {
        var preflight = new BgraEligibilityPreflight();

        preflight.Observe(
        [
            0, 0, 0, 255,
            255, 255, 255, 255,
        ]);

        Assert.False(preflight.IsDefinitelyIneligible);
    }

    [Fact]
    public void BgraPreflight_StopsOnceNormalOpaqueArtworkIsConclusive()
    {
        var preflight = new BgraEligibilityPreflight();

        preflight.Observe([80, 120, 160, 255]);

        Assert.True(preflight.CanStopScanning);
        Assert.False(preflight.IsDefinitelyIneligible);
    }

    [Fact]
    public void Extract_ReturnsFewerColorsInsteadOfDuplicatingAFlatSource()
    {
        var image = TestImage.Blocks((new RgbColor(90, 140, 190), 10));

        var result = _extractor.Extract(
            image,
            new PaletteExtractionOptions(6, 6));

        Assert.Single(result.MajorColors);
        Assert.Empty(result.MinorColors);
        Assert.True(result.HasFewerColorsThanRequested);
        Assert.Equal(
            "The source produced 1 distinct palette colors; 12 were requested.",
            result.ShortfallMessage);
    }

    [Fact]
    public void Extract_DownscalesLongestDimensionToTwelveHundred()
    {
        var color = new RgbColor(70, 120, 180);
        var image = TestImage.FromColors(
            Enumerable.Repeat(color, 1201).ToArray());

        var result = _extractor.Extract(
            image,
            new PaletteExtractionOptions(1, 0));

        Assert.Equal(1200, result.EligiblePixelCount);
        Assert.Equal(240, result.SampledPixelCount);
        Assert.Null(result.ShortfallMessage);
    }

    [Fact]
    public void Extract_ReturnsMajorsBeforeDistinctBrightnessOrderedMinors()
    {
        var red = new RgbColor(210, 40, 40);
        var darkBlue = new RgbColor(30, 40, 140);
        var green = new RgbColor(30, 180, 60);
        var yellow = new RgbColor(220, 190, 30);
        var image = TestImage.Blocks(
            (red, 20),
            (darkBlue, 8),
            (green, 7),
            (yellow, 6));

        var result = _extractor.Extract(
            image,
            new PaletteExtractionOptions(1, 3));

        Assert.Single(result.MajorColors);
        Assert.Equal(3, result.MinorColors.Count);
        Assert.Equal(
            result.MinorColors.Count,
            result.MajorColors.Concat(result.MinorColors).Distinct().Count() - 1);
        Assert.Equal(
            result.MinorColors.OrderBy(Brightness),
            result.MinorColors);
        Assert.Equal(
            new[] { "Major 01", "Minor 01", "Minor 02", "Minor 03" },
            result.ToNamedColors().Select(color => color.Name));
    }

    private static double Brightness(RgbColor color) =>
        (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue);
}
