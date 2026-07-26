using System.Buffers.Binary;
using System.Diagnostics;
using CspPaletteCompanion.Core.Imaging;
using CspPaletteCompanion.Core.Palette;

const int width = 256;
const int height = 256;
var rgba = new byte[width * height * 4];
var bands = new[]
{
    new RgbColor(205, 62, 74),
    new RgbColor(46, 118, 202),
    new RgbColor(57, 171, 103),
    new RgbColor(232, 171, 54),
};

for (var y = 0; y < height; y++)
{
    for (var x = 0; x < width; x++)
    {
        var color = bands[Math.Min(x / 64, bands.Length - 1)];
        if (x is >= 112 and < 144 && y is >= 104 and < 152)
        {
            color = new RgbColor(143, 79, 188);
        }

        var offset = ((y * width) + x) * 4;
        rgba[offset] = color.Red;
        rgba[offset + 1] = color.Green;
        rgba[offset + 2] = color.Blue;
        rgba[offset + 3] = 255;
    }
}

var outputArgument = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
var output = outputArgument is not null
    ? Path.GetFullPath(outputArgument)
    : Path.GetFullPath(Path.Combine("artifacts", "smoke"));
Directory.CreateDirectory(output);

var image = new RgbaImage(width, height, rgba);
var result = new PaletteExtractor().Extract(image, new PaletteExtractionOptions(4, 1));
File.WriteAllBytes(Path.Combine(output, "csp-smoke-source.bmp"), WriteBitmap(width, height, rgba));
File.WriteAllBytes(Path.Combine(output, "csp-smoke-palette.aco"), AdobeColorSwatchWriter.Write(result));
File.WriteAllLines(
    Path.Combine(output, "expected-palette.txt"),
    result.ToNamedColors().Select(color => $"{color.Name}\t{color.Color.ToHex()}"));

Console.WriteLine($"Wrote smoke fixtures to {output}");
foreach (var color in result.ToNamedColors())
{
    Console.WriteLine($"{color.Name}: {color.Color.ToHex()}");
}

if (args.Contains("--benchmark", StringComparer.Ordinal))
{
    RunBenchmark();
}

static byte[] WriteBitmap(int width, int height, byte[] rgba)
{
    var rowSize = checked(width * 3);
    var paddedRowSize = (rowSize + 3) & ~3;
    var pixelBytes = checked(paddedRowSize * height);
    var fileSize = checked(54 + pixelBytes);
    var bytes = new byte[fileSize];

    bytes[0] = (byte)'B';
    bytes[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), fileSize);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), pixelBytes);

    for (var y = 0; y < height; y++)
    {
        var sourceY = height - 1 - y;
        var targetRow = 54 + (y * paddedRowSize);
        for (var x = 0; x < width; x++)
        {
            var source = ((sourceY * width) + x) * 4;
            var target = targetRow + (x * 3);
            bytes[target] = rgba[source + 2];
            bytes[target + 1] = rgba[source + 1];
            bytes[target + 2] = rgba[source];
        }
    }

    return bytes;
}

static void RunBenchmark()
{
    const int benchmarkWidth = 3840;
    const int benchmarkHeight = 2160;
    var pixels = new byte[benchmarkWidth * benchmarkHeight * 4];
    for (var offset = 0; offset < pixels.Length; offset += 4)
    {
        var pixel = offset / 4;
        var x = pixel % benchmarkWidth;
        var y = pixel / benchmarkWidth;
        pixels[offset] = (byte)((x * 13 + y * 3) % 224 + 16);
        pixels[offset + 1] = (byte)((x * 5 + y * 11) % 224 + 16);
        pixels[offset + 2] = (byte)((x * 7 + y * 17) % 224 + 16);
        pixels[offset + 3] = 255;
    }

    var image = new RgbaImage(benchmarkWidth, benchmarkHeight, pixels);
    var extractor = new PaletteExtractor();
    extractor.Extract(image, new PaletteExtractionOptions());

    var samples = new List<double>();
    for (var iteration = 0; iteration < 10; iteration++)
    {
        var stopwatch = Stopwatch.StartNew();
        extractor.Extract(image, new PaletteExtractionOptions());
        samples.Add(stopwatch.Elapsed.TotalMilliseconds);
    }

    samples.Sort();
    var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
    Console.WriteLine(
        $"4K benchmark: median {samples[samples.Count / 2]:F1} ms, p95 {p95:F1} ms over {samples.Count} warm runs");
}
