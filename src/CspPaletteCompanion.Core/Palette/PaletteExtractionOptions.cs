namespace CspPaletteCompanion.Core.Palette;

public sealed record PaletteExtractionOptions
{
    public const int DefaultMajorColorCount = 6;
    public const int DefaultMinorColorCount = 6;

    public PaletteExtractionOptions(
        int majorColorCount = DefaultMajorColorCount,
        int minorColorCount = DefaultMinorColorCount)
    {
        if (majorColorCount is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(majorColorCount),
                "Major color count must be between 1 and 20.");
        }

        if (minorColorCount is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minorColorCount),
                "Minor color count must be between 0 and 20.");
        }

        MajorColorCount = majorColorCount;
        MinorColorCount = minorColorCount;
    }

    public int MajorColorCount { get; }

    public int MinorColorCount { get; }
}
