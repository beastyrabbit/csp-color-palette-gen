namespace CspPaletteCompanion.Core.Palette;

public sealed class NoEligiblePixelsException : InvalidOperationException
{
    public NoEligiblePixelsException()
        : base("The source contains no eligible opaque pixels after filtering transparent, near-black, and near-white pixels.")
    {
    }
}
