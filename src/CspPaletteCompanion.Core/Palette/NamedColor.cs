namespace CspPaletteCompanion.Core.Palette;

public sealed record NamedColor
{
    public NamedColor(string name, RgbColor color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Color = color;
    }

    public string Name { get; }

    public RgbColor Color { get; }
}
