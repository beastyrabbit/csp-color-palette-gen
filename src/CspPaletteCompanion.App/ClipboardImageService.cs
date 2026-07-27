using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CspPaletteCompanion.Core.Palette;

namespace CspPaletteCompanion.App;

internal sealed class ClipboardImageService
{
    private const int PreflightRows = 64;

    internal ClipboardImageReadResult Read()
    {
        if (!Clipboard.ContainsImage())
        {
            return new ClipboardImageReadResult(null, false);
        }

        var source = Clipboard.GetImage();
        if (source is null)
        {
            return new ClipboardImageReadResult(null, false);
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        // A transparent/solid selection from a very large CSP document used to
        // allocate and convert the entire bitmap before extraction discovered
        // that it had no usable colors. Scan bounded row chunks first. Ordinary
        // artwork exits this preflight as soon as an opaque, non-edge range is
        // observed; definitely empty/black/white payloads allocate no full-size
        // pixel arrays.
        var rowStride = checked(converted.PixelWidth * 4);
        var rowsPerChunk = Math.Min(PreflightRows, converted.PixelHeight);
        var preflightBuffer = new byte[checked(rowStride * rowsPerChunk)];
        var preflight = new BgraEligibilityPreflight();
        for (var y = 0; y < converted.PixelHeight; y += rowsPerChunk)
        {
            var rowCount = Math.Min(rowsPerChunk, converted.PixelHeight - y);
            var byteCount = checked(rowStride * rowCount);
            converted.CopyPixels(
                new Int32Rect(0, y, converted.PixelWidth, rowCount),
                preflightBuffer,
                rowStride,
                0);
            preflight.Observe(preflightBuffer.AsSpan(0, byteCount));
            if (preflight.CanStopScanning)
            {
                break;
            }
        }

        if (preflight.IsDefinitelyIneligible)
        {
            return new ClipboardImageReadResult(null, true);
        }

        var rgba = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(rgba, converted.PixelWidth * 4, 0);
        var hasNonZeroAlpha = false;
        for (var index = 3; index < rgba.Length; index += 4)
        {
            if (rgba[index] != 0)
            {
                hasNonZeroAlpha = true;
                break;
            }
        }

        // Convert BGRA to RGBA in place. A 10,736 × 14,800 canvas is about
        // 606 MiB, so avoiding a second full-size array materially reduces both
        // allocation pressure and the delay before extraction can begin.
        for (var index = 0; index < rgba.Length; index += 4)
        {
            (rgba[index], rgba[index + 2]) = (rgba[index + 2], rgba[index]);
            // Many Windows CF_DIB producers store zero in the unused fourth
            // byte of an otherwise opaque 32-bit bitmap. Only treat that byte
            // as alpha when the payload contains at least one non-zero alpha.
            if (!hasNonZeroAlpha)
            {
                rgba[index + 3] = byte.MaxValue;
            }
        }

        return new ClipboardImageReadResult(
            new ClipboardImage(converted.PixelWidth, converted.PixelHeight, rgba),
            false);
    }
}

internal readonly record struct ClipboardImageReadResult(
    ClipboardImage? Image,
    bool IsDefinitelyIneligible);
