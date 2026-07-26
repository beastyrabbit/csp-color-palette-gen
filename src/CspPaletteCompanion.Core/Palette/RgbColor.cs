namespace CspPaletteCompanion.Core.Palette;

public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public string ToHex() => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public override string ToString() => ToHex();
}
