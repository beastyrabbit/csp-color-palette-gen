namespace CspPaletteCompanion.Companion.Tests;

public sealed class CanvasTilePlannerTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2000, 1200)]
    [InlineData(5000, 4100)]
    [InlineData(25000, 300)]
    public void Plan_CoversCanvasExactlyWithinLimits(int width, int height)
    {
        var tiles = CanvasTilePlanner.Plan(width, height);
        var area = tiles.Sum(tile => (long)tile.PixelCount);

        Assert.Equal((long)width * height, area);
        Assert.All(tiles, tile =>
        {
            Assert.InRange(tile.Width, 1, CanvasTilePlanner.DefaultMaximumSide);
            Assert.InRange(tile.Height, 1, CanvasTilePlanner.DefaultMaximumSide);
            Assert.InRange(tile.PixelCount, 1, CanvasTilePlanner.DefaultMaximumPixels);
            Assert.InRange(tile.Left, 0, width - 1);
            Assert.InRange(tile.Top, 0, height - 1);
            Assert.InRange(tile.Right, 1, width);
            Assert.InRange(tile.Bottom, 1, height);
        });

        Assert.Equal(Enumerable.Range(0, tiles.Count), tiles.Select(tile => tile.Index));
        Assert.Equal(0, tiles.SelectMany(
            tile => tiles.Where(other => other.Index > tile.Index),
            (tile, other) => Intersects(tile, other)).Count(intersects => intersects));
    }

    [Fact]
    public void Plan_InvalidDimensions_Rejects()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasTilePlanner.Plan(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasTilePlanner.Plan(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasTilePlanner.Plan(1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasTilePlanner.Plan(1, 1, 1, 0));
    }

    private static bool Intersects(CanvasTile left, CanvasTile right) =>
        left.Left < right.Right &&
        left.Right > right.Left &&
        left.Top < right.Bottom &&
        left.Bottom > right.Top;
}
