namespace CspPaletteCompanion.Companion;

public readonly record struct CanvasTile(
    int Index,
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int PixelCount => checked(Width * Height);
}

public static class CanvasTilePlanner
{
    public const int DefaultMaximumPixels = 2_400_000;
    public const int DefaultMaximumSide = 2_000;

    public static IReadOnlyList<CanvasTile> Plan(
        int width,
        int height,
        int maximumPixels = DefaultMaximumPixels,
        int maximumSide = DefaultMaximumSide)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (maximumPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        }

        if (maximumSide <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSide));
        }

        var tileWidth = Math.Min(width, Math.Min(maximumSide, maximumPixels));
        var tileHeight = Math.Min(height, Math.Min(maximumSide, maximumPixels / tileWidth));
        tileHeight = Math.Max(1, tileHeight);

        var tiles = new List<CanvasTile>();
        for (var top = 0; top < height; top += tileHeight)
        {
            var bottom = Math.Min(height, top + tileHeight);
            for (var left = 0; left < width; left += tileWidth)
            {
                var right = Math.Min(width, left + tileWidth);
                tiles.Add(new CanvasTile(tiles.Count, left, top, right, bottom));
            }
        }

        return tiles;
    }
}
