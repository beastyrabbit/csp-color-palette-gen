using System.Text.Json.Serialization;

namespace CspPaletteCompanion.Companion;

public sealed record CompanionCanvasSize(
    [property: JsonPropertyName("CanvasWidth")] int Width,
    [property: JsonPropertyName("CanvasHeight")] int Height);

public sealed record CompanionPreviewResponse
{
    public string? Operation { get; init; }
    public int MaxLength { get; init; }
    public int CanvasCount { get; init; }
    public int CanvasIndex { get; init; }
    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }
    public IReadOnlyList<CompanionCanvasSize> CanvasSizeArray { get; init; } = [];
    public int GalleryIdentificationNumber { get; init; }
    public int BlockLeft { get; init; }
    public int BlockTop { get; init; }
    public int BlockRight { get; init; }
    public int BlockBottom { get; init; }
    public int BlockIndex { get; init; }
}

public sealed record CompanionPreviewBlock(
    CompanionPreviewResponse Response,
    CanvasTile RequestedTile,
    byte[] RgbaPixels);

public sealed record CompanionRgbaCanvas(int Width, int Height, byte[] Pixels)
{
    public int Stride => checked(Width * 4);
}
