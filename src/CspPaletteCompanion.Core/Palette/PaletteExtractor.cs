using CspPaletteCompanion.Core.Imaging;

namespace CspPaletteCompanion.Core.Palette;

/// <summary>
/// Deterministic BeastyPage-style major/minor color extraction.
/// </summary>
public sealed class PaletteExtractor
{
    private const int MaximumDimension = 1200;
    private const int AlphaThreshold = 128;
    private const int EdgeColorThreshold = 15;
    private const int SamplingStride = 5;
    private const int MaximumKMeansIterations = 20;
    private const int InitialMinorDistance = 50;
    private const int MinorDistanceStep = 5;

    public PaletteExtractionResult Extract(
        RgbaImage source,
        PaletteExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new PaletteExtractionOptions();

        var scaled = ScaleDown(source);
        var sample = SampleEligiblePixels(scaled);
        if (sample.Pixels.Length == 0)
        {
            throw new NoEligiblePixelsException();
        }

        var majorClusters = Cluster(
            sample.Pixels,
            options.MajorColorCount,
            StableSeed(scaled, 0x4D414A4F52UL));

        var majorColors = DistinctColors(
            majorClusters
                .OrderByDescending(cluster => cluster.Population)
                .ThenBy(cluster => cluster.Center.Red)
                .ThenBy(cluster => cluster.Center.Green)
                .ThenBy(cluster => cluster.Center.Blue)
                .Select(cluster => cluster.Center),
            options.MajorColorCount);

        IReadOnlyList<RgbColor> minorColors = Array.Empty<RgbColor>();
        if (options.MinorColorCount > 0)
        {
            var minorClusters = Cluster(
                sample.Pixels,
                checked(options.MinorColorCount * 2),
                StableSeed(scaled, 0x4D494E4F52UL));

            var candidates = DistinctColors(
                minorClusters
                    .OrderBy(cluster => Brightness(cluster.Center))
                    .ThenBy(cluster => cluster.Center.Red)
                    .ThenBy(cluster => cluster.Center.Green)
                    .ThenBy(cluster => cluster.Center.Blue)
                    .Select(cluster => cluster.Center),
                int.MaxValue);

            minorColors = SelectMinorColors(
                candidates,
                majorColors,
                options.MinorColorCount);
        }

        return new PaletteExtractionResult(
            majorColors,
            minorColors,
            options.MajorColorCount,
            options.MinorColorCount,
            sample.EligibleCount,
            sample.Pixels.Length);
    }

    private static RgbaImage ScaleDown(RgbaImage source)
    {
        var longestDimension = Math.Max(source.Width, source.Height);
        if (longestDimension <= MaximumDimension)
        {
            return source;
        }

        var scale = (double)MaximumDimension / longestDimension;
        var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var sourcePixels = source.Pixels.Span;
        var targetPixels = new byte[checked(targetWidth * targetHeight * 4)];

        // Bilinear interpolation matches the smooth canvas downscaling used by the
        // browser reference more closely than nearest-neighbor resampling.
        var xRatio = (double)source.Width / targetWidth;
        var yRatio = (double)source.Height / targetHeight;

        for (var targetY = 0; targetY < targetHeight; targetY++)
        {
            var sourceY = ((targetY + 0.5) * yRatio) - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.Height - 1);
            var y1 = Math.Min(y0 + 1, source.Height - 1);
            var yWeight = Math.Clamp(sourceY - y0, 0, 1);

            for (var targetX = 0; targetX < targetWidth; targetX++)
            {
                var sourceX = ((targetX + 0.5) * xRatio) - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.Width - 1);
                var x1 = Math.Min(x0 + 1, source.Width - 1);
                var xWeight = Math.Clamp(sourceX - x0, 0, 1);
                var targetOffset = ((targetY * targetWidth) + targetX) * 4;

                var topLeftOffset = ((y0 * source.Width) + x0) * 4;
                var topRightOffset = ((y0 * source.Width) + x1) * 4;
                var bottomLeftOffset = ((y1 * source.Width) + x0) * 4;
                var bottomRightOffset = ((y1 * source.Width) + x1) * 4;
                var topLeftWeight = (1 - xWeight) * (1 - yWeight);
                var topRightWeight = xWeight * (1 - yWeight);
                var bottomLeftWeight = (1 - xWeight) * yWeight;
                var bottomRightWeight = xWeight * yWeight;

                var alpha =
                    (sourcePixels[topLeftOffset + 3] * topLeftWeight) +
                    (sourcePixels[topRightOffset + 3] * topRightWeight) +
                    (sourcePixels[bottomLeftOffset + 3] * bottomLeftWeight) +
                    (sourcePixels[bottomRightOffset + 3] * bottomRightWeight);

                targetPixels[targetOffset + 3] =
                    (byte)Math.Clamp((int)Math.Round(alpha), 0, 255);

                for (var channel = 0; channel < 3; channel++)
                {
                    if (alpha <= 0)
                    {
                        targetPixels[targetOffset + channel] = 0;
                        continue;
                    }

                    var premultiplied =
                        (sourcePixels[topLeftOffset + channel] *
                         sourcePixels[topLeftOffset + 3] *
                         topLeftWeight) +
                        (sourcePixels[topRightOffset + channel] *
                         sourcePixels[topRightOffset + 3] *
                         topRightWeight) +
                        (sourcePixels[bottomLeftOffset + channel] *
                         sourcePixels[bottomLeftOffset + 3] *
                         bottomLeftWeight) +
                        (sourcePixels[bottomRightOffset + channel] *
                         sourcePixels[bottomRightOffset + 3] *
                         bottomRightWeight);

                    targetPixels[targetOffset + channel] =
                        (byte)Math.Clamp((int)Math.Round(premultiplied / alpha), 0, 255);
                }
            }
        }

        return new RgbaImage(targetWidth, targetHeight, targetPixels);
    }

    private static SampleResult SampleEligiblePixels(RgbaImage image)
    {
        var pixels = image.Pixels.Span;
        var result = new List<Pixel>(Math.Max(1, (image.Width * image.Height) / SamplingStride));
        var eligibleCount = 0;

        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var red = pixels[offset];
            var green = pixels[offset + 1];
            var blue = pixels[offset + 2];
            var alpha = pixels[offset + 3];

            if (alpha < AlphaThreshold ||
                IsNearBlack(red, green, blue) ||
                IsNearWhite(red, green, blue))
            {
                continue;
            }

            if (eligibleCount % SamplingStride == 0)
            {
                result.Add(new Pixel(red, green, blue));
            }

            eligibleCount++;
        }

        return new SampleResult(result.ToArray(), eligibleCount);
    }

    private static bool IsNearBlack(byte red, byte green, byte blue) =>
        red < EdgeColorThreshold &&
        green < EdgeColorThreshold &&
        blue < EdgeColorThreshold;

    private static bool IsNearWhite(byte red, byte green, byte blue) =>
        red > byte.MaxValue - EdgeColorThreshold &&
        green > byte.MaxValue - EdgeColorThreshold &&
        blue > byte.MaxValue - EdgeColorThreshold;

    private static IReadOnlyList<RgbColor> SelectMinorColors(
        IReadOnlyList<RgbColor> candidates,
        IReadOnlyList<RgbColor> majorColors,
        int requestedCount)
    {
        var accepted = new List<RgbColor>(requestedCount);

        for (var threshold = InitialMinorDistance;
             threshold >= 0 && accepted.Count < requestedCount;
             threshold -= MinorDistanceStep)
        {
            foreach (var candidate in candidates)
            {
                if (accepted.Count == requestedCount)
                {
                    break;
                }

                if (majorColors.Contains(candidate) || accepted.Contains(candidate))
                {
                    continue;
                }

                var isDistinct = majorColors
                    .Concat(accepted)
                    .All(color => ColorDistance(color, candidate) >= threshold);

                if (isDistinct)
                {
                    accepted.Add(candidate);
                }
            }
        }

        return accepted;
    }

    private static double Brightness(RgbColor color) =>
        (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue);

    private static double ColorDistance(RgbColor left, RgbColor right)
    {
        var red = left.Red - right.Red;
        var green = left.Green - right.Green;
        var blue = left.Blue - right.Blue;
        return Math.Sqrt((red * red) + (green * green) + (blue * blue));
    }

    private static IReadOnlyList<RgbColor> DistinctColors(
        IEnumerable<RgbColor> colors,
        int maximumCount)
    {
        var result = new List<RgbColor>();
        var seen = new HashSet<RgbColor>();

        foreach (var color in colors)
        {
            if (seen.Add(color))
            {
                result.Add(color);
                if (result.Count == maximumCount)
                {
                    break;
                }
            }
        }

        return result;
    }

    private static ClusterResult[] Cluster(Pixel[] pixels, int requestedK, ulong seed)
    {
        var distinctCount = pixels.Distinct().Count();
        var k = Math.Min(requestedK, distinctCount);
        if (k == 0)
        {
            return Array.Empty<ClusterResult>();
        }

        var random = new StableRandom(seed);
        var centers = InitializeCenters(pixels, k, random);
        var assignments = new int[pixels.Length];
        Array.Fill(assignments, -1);

        var sumsRed = new long[k];
        var sumsGreen = new long[k];
        var sumsBlue = new long[k];
        var populations = new int[k];

        for (var iteration = 0; iteration < MaximumKMeansIterations; iteration++)
        {
            Array.Clear(sumsRed);
            Array.Clear(sumsGreen);
            Array.Clear(sumsBlue);
            Array.Clear(populations);
            var changed = false;

            for (var pointIndex = 0; pointIndex < pixels.Length; pointIndex++)
            {
                var clusterIndex = FindNearestCenter(pixels[pointIndex], centers);
                if (assignments[pointIndex] != clusterIndex)
                {
                    assignments[pointIndex] = clusterIndex;
                    changed = true;
                }

                var point = pixels[pointIndex];
                sumsRed[clusterIndex] += point.Red;
                sumsGreen[clusterIndex] += point.Green;
                sumsBlue[clusterIndex] += point.Blue;
                populations[clusterIndex]++;
            }

            for (var clusterIndex = 0; clusterIndex < k; clusterIndex++)
            {
                if (populations[clusterIndex] == 0)
                {
                    centers[clusterIndex] = FindFarthestPoint(pixels, centers);
                    continue;
                }

                centers[clusterIndex] = new Center(
                    (double)sumsRed[clusterIndex] / populations[clusterIndex],
                    (double)sumsGreen[clusterIndex] / populations[clusterIndex],
                    (double)sumsBlue[clusterIndex] / populations[clusterIndex]);
            }

            if (!changed)
            {
                break;
            }
        }

        // Reassign after the final center update so populations describe the
        // returned centers, including when the iteration limit is reached.
        Array.Clear(populations);
        for (var pointIndex = 0; pointIndex < pixels.Length; pointIndex++)
        {
            populations[FindNearestCenter(pixels[pointIndex], centers)]++;
        }

        return centers
            .Select((center, index) => new ClusterResult(center.ToRgbColor(), populations[index]))
            .Where(cluster => cluster.Population > 0)
            .ToArray();
    }

    private static Center[] InitializeCenters(Pixel[] pixels, int k, StableRandom random)
    {
        var centers = new Center[k];
        centers[0] = Center.FromPixel(pixels[random.NextIndex(pixels.Length)]);
        var minimumSquaredDistances = new double[pixels.Length];

        for (var centerIndex = 1; centerIndex < k; centerIndex++)
        {
            var totalWeight = 0d;
            for (var pointIndex = 0; pointIndex < pixels.Length; pointIndex++)
            {
                var distance = SquaredDistance(pixels[pointIndex], centers[0]);
                for (var existingIndex = 1; existingIndex < centerIndex; existingIndex++)
                {
                    distance = Math.Min(
                        distance,
                        SquaredDistance(pixels[pointIndex], centers[existingIndex]));
                }

                minimumSquaredDistances[pointIndex] = distance;
                totalWeight += distance;
            }

            if (totalWeight <= 0)
            {
                centers[centerIndex] = Center.FromPixel(
                    pixels.First(pixel => centers
                        .Take(centerIndex)
                        .All(center => center.ToRgbColor() != pixel.ToRgbColor())));
                continue;
            }

            var target = random.NextDouble() * totalWeight;
            var cumulative = 0d;
            var selectedIndex = pixels.Length - 1;
            for (var pointIndex = 0; pointIndex < pixels.Length; pointIndex++)
            {
                cumulative += minimumSquaredDistances[pointIndex];
                if (cumulative > target)
                {
                    selectedIndex = pointIndex;
                    break;
                }
            }

            centers[centerIndex] = Center.FromPixel(pixels[selectedIndex]);
        }

        return centers;
    }

    private static Center FindFarthestPoint(Pixel[] pixels, Center[] centers)
    {
        var farthest = pixels[0];
        var farthestDistance = -1d;

        foreach (var pixel in pixels)
        {
            var nearestDistance = centers.Min(center => SquaredDistance(pixel, center));
            if (nearestDistance > farthestDistance)
            {
                farthestDistance = nearestDistance;
                farthest = pixel;
            }
        }

        return Center.FromPixel(farthest);
    }

    private static int FindNearestCenter(Pixel pixel, Center[] centers)
    {
        var nearestIndex = 0;
        var nearestDistance = SquaredDistance(pixel, centers[0]);

        for (var index = 1; index < centers.Length; index++)
        {
            var distance = SquaredDistance(pixel, centers[index]);
            if (distance < nearestDistance)
            {
                nearestIndex = index;
                nearestDistance = distance;
            }
        }

        return nearestIndex;
    }

    private static double SquaredDistance(Pixel pixel, Center center)
    {
        var red = pixel.Red - center.Red;
        var green = pixel.Green - center.Green;
        var blue = pixel.Blue - center.Blue;
        return (red * red) + (green * green) + (blue * blue);
    }

    private static ulong StableSeed(RgbaImage image, ulong passSalt)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis ^ passSalt;

        hash = AddInteger(hash, image.Width, prime);
        hash = AddInteger(hash, image.Height, prime);

        foreach (var value in image.Pixels.Span)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash == 0 ? offsetBasis : hash;
    }

    private static ulong AddInteger(ulong hash, int value, ulong prime)
    {
        unchecked
        {
            for (var shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= prime;
            }
        }

        return hash;
    }

    private readonly record struct Pixel(byte Red, byte Green, byte Blue)
    {
        public RgbColor ToRgbColor() => new(Red, Green, Blue);
    }

    private readonly record struct Center(double Red, double Green, double Blue)
    {
        public static Center FromPixel(Pixel pixel) =>
            new(pixel.Red, pixel.Green, pixel.Blue);

        public RgbColor ToRgbColor() =>
            new(
                (byte)Math.Clamp((int)Math.Round(Red), 0, 255),
                (byte)Math.Clamp((int)Math.Round(Green), 0, 255),
                (byte)Math.Clamp((int)Math.Round(Blue), 0, 255));
    }

    private readonly record struct ClusterResult(RgbColor Center, int Population);

    private readonly record struct SampleResult(Pixel[] Pixels, int EligibleCount);

    private sealed class StableRandom
    {
        private ulong _state;

        public StableRandom(ulong seed)
        {
            _state = seed;
        }

        public int NextIndex(int exclusiveMaximum) =>
            (int)(NextUInt64() % (uint)exclusiveMaximum);

        public double NextDouble() =>
            (NextUInt64() >> 11) * (1d / (1UL << 53));

        private ulong NextUInt64()
        {
            // xorshift64* has a completely specified bitstream on every runtime.
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 2685821657736338717UL;
        }
    }
}
