namespace CspPaletteCompanion.Companion;

public sealed class WebtoonCanvasAssembler(
    CompanionModeClient client,
    int maximumPixelsPerTile = CanvasTilePlanner.DefaultMaximumPixels,
    int maximumTileSide = CanvasTilePlanner.DefaultMaximumSide,
    int maximumConcurrency = 4)
{
    public async Task<CompanionRgbaCanvas> ReadCanvasAsync(
        int? expectedWidth = null,
        int? expectedHeight = null,
        int maximumGalleryLength = 100,
        CancellationToken cancellationToken = default)
    {
        if (expectedWidth.HasValue != expectedHeight.HasValue)
        {
            throw new ArgumentException("Expected width and height must either both be supplied or both be omitted.");
        }

        await client.SyncPreviewAsync(cancellationToken).ConfigureAwait(false);
        var gallery = await client.UpdateGalleryAsync(maximumGalleryLength, cancellationToken).ConfigureAwait(false);
        var canvasIndex = SelectCanvasIndex(gallery.CanvasSizeArray, expectedWidth, expectedHeight);
        var size = gallery.CanvasSizeArray[canvasIndex];
        var tiles = CanvasTilePlanner.Plan(size.Width, size.Height, maximumPixelsPerTile, maximumTileSide);
        var output = new byte[checked(size.Width * size.Height * 4)];
        if (maximumConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrency),
                "Canvas block concurrency must be positive.");
        }

        // CSP accepts several preview-block requests in flight and identifies
        // replies by serial number. Fetching blocks one at a time makes large
        // canvases pay the round-trip and CSP rendering latency for every tile.
        // Four workers matches the proven-safe clipremote implementation while
        // keeping temporary base64/RGB/RGBA allocations bounded.
        await Parallel.ForEachAsync(
            tiles,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(maximumConcurrency, tiles.Count),
            },
            async (tile, token) =>
            {
                var block = await client.ReadBlockAsync(
                    gallery.GalleryIdentificationNumber,
                    canvasIndex,
                    tile,
                    token).ConfigureAwait(false);

                // Planned tiles never overlap, so workers can copy directly into
                // their own output ranges without a lock.
                CopyBlock(output, size.Width, block.RgbaPixels, tile);
            }).ConfigureAwait(false);

        return new CompanionRgbaCanvas(size.Width, size.Height, output);
    }

    public static int SelectCanvasIndex(
        IReadOnlyList<CompanionCanvasSize> gallery,
        int? expectedWidth = null,
        int? expectedHeight = null)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        if (gallery.Count == 0)
        {
            throw new InvalidOperationException("The companion preview gallery is empty.");
        }

        if (gallery.Count == 1)
        {
            return 0;
        }

        if (expectedWidth is null || expectedHeight is null)
        {
            throw new InvalidOperationException(
                "The gallery contains multiple canvases; expected dimensions are required.");
        }

        var matches = gallery
            .Select((size, index) => (size, index))
            .Where(item => item.size.Width == expectedWidth && item.size.Height == expectedHeight)
            .Select(item => item.index)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No preview canvas matches {expectedWidth}x{expectedHeight}."),
            _ => throw new InvalidOperationException(
                $"Multiple preview canvases match {expectedWidth}x{expectedHeight}."),
        };
    }

    private static void CopyBlock(byte[] destination, int canvasWidth, byte[] source, CanvasTile tile)
    {
        var sourceStride = checked(tile.Width * 4);
        var destinationStride = checked(canvasWidth * 4);
        for (var row = 0; row < tile.Height; row++)
        {
            source.AsSpan(row * sourceStride, sourceStride).CopyTo(
                destination.AsSpan(
                    checked((tile.Top + row) * destinationStride + tile.Left * 4),
                    sourceStride));
        }
    }
}
