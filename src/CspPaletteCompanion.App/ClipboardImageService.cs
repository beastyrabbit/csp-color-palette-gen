using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CspPaletteCompanion.App;

internal sealed class ClipboardImageService
{
    internal ClipboardImage? Read()
    {
        if (!Clipboard.ContainsImage())
        {
            return null;
        }

        var source = Clipboard.GetImage();
        if (source is null)
        {
            return null;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var bgra = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(bgra, converted.PixelWidth * 4, 0);
        var hasNonZeroAlpha = false;
        for (var index = 3; index < bgra.Length; index += 4)
        {
            if (bgra[index] != 0)
            {
                hasNonZeroAlpha = true;
                break;
            }
        }

        var rgba = new byte[bgra.Length];
        for (var index = 0; index < bgra.Length; index += 4)
        {
            rgba[index] = bgra[index + 2];
            rgba[index + 1] = bgra[index + 1];
            rgba[index + 2] = bgra[index];
            // Many Windows CF_DIB producers store zero in the unused fourth
            // byte of an otherwise opaque 32-bit bitmap. Only treat that byte
            // as alpha when the payload contains at least one non-zero alpha.
            rgba[index + 3] = hasNonZeroAlpha ? bgra[index + 3] : byte.MaxValue;
        }

        return new ClipboardImage(converted.PixelWidth, converted.PixelHeight, rgba);
    }
}
