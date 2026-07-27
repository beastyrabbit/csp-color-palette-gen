namespace CspPaletteCompanion.Core.Palette;

/// <summary>
/// Incrementally identifies BGRA32 payloads that cannot produce an eligible
/// palette pixel. This is intentionally conservative: false means "inspect the
/// image normally", not that an eligible pixel is guaranteed.
/// </summary>
public sealed class BgraEligibilityPreflight
{
    private const byte AlphaThreshold = 128;
    private const byte EdgeColorThreshold = 15;

    private bool _allNearBlack = true;
    private bool _allNearWhite = true;
    private byte _maximumAlpha;
    private long _pixelCount;

    public bool CanStopScanning =>
        _maximumAlpha >= AlphaThreshold &&
        !_allNearBlack &&
        !_allNearWhite;

    public bool IsDefinitelyIneligible =>
        _pixelCount > 0 &&
        (_maximumAlpha is > 0 and < AlphaThreshold ||
         _allNearBlack ||
         _allNearWhite);

    public void Observe(ReadOnlySpan<byte> bgra32Pixels)
    {
        if (bgra32Pixels.Length % 4 != 0)
        {
            throw new ArgumentException(
                "BGRA32 data must contain a whole number of pixels.",
                nameof(bgra32Pixels));
        }

        for (var offset = 0; offset < bgra32Pixels.Length; offset += 4)
        {
            var blue = bgra32Pixels[offset];
            var green = bgra32Pixels[offset + 1];
            var red = bgra32Pixels[offset + 2];
            var alpha = bgra32Pixels[offset + 3];

            _maximumAlpha = Math.Max(_maximumAlpha, alpha);
            _allNearBlack &=
                red < EdgeColorThreshold &&
                green < EdgeColorThreshold &&
                blue < EdgeColorThreshold;
            _allNearWhite &=
                red > byte.MaxValue - EdgeColorThreshold &&
                green > byte.MaxValue - EdgeColorThreshold &&
                blue > byte.MaxValue - EdgeColorThreshold;
            _pixelCount++;
        }
    }
}
