namespace CspPaletteCompanion.Core.Palette;

public sealed class PaletteExtractionResult
{
    internal PaletteExtractionResult(
        IReadOnlyList<RgbColor> majorColors,
        IReadOnlyList<RgbColor> minorColors,
        int requestedMajorColorCount,
        int requestedMinorColorCount,
        int eligiblePixelCount,
        int sampledPixelCount)
    {
        MajorColors = majorColors;
        MinorColors = minorColors;
        RequestedMajorColorCount = requestedMajorColorCount;
        RequestedMinorColorCount = requestedMinorColorCount;
        EligiblePixelCount = eligiblePixelCount;
        SampledPixelCount = sampledPixelCount;
    }

    public IReadOnlyList<RgbColor> MajorColors { get; }

    public IReadOnlyList<RgbColor> MinorColors { get; }

    public int RequestedMajorColorCount { get; }

    public int RequestedMinorColorCount { get; }

    public int EligiblePixelCount { get; }

    public int SampledPixelCount { get; }

    public int ColorCount => MajorColors.Count + MinorColors.Count;

    public bool HasFewerColorsThanRequested =>
        MajorColors.Count < RequestedMajorColorCount ||
        MinorColors.Count < RequestedMinorColorCount;

    public string? ShortfallMessage =>
        HasFewerColorsThanRequested
            ? $"The source produced {ColorCount} distinct palette colors; " +
              $"{RequestedMajorColorCount + RequestedMinorColorCount} were requested."
            : null;

    public IReadOnlyList<NamedColor> ToNamedColors()
    {
        var colors = new List<NamedColor>(ColorCount);

        for (var index = 0; index < MajorColors.Count; index++)
        {
            colors.Add(new NamedColor($"Major {index + 1:00}", MajorColors[index]));
        }

        for (var index = 0; index < MinorColors.Count; index++)
        {
            colors.Add(new NamedColor($"Minor {index + 1:00}", MinorColors[index]));
        }

        return colors;
    }
}
