using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CspPaletteCompanion.App;

internal sealed class ClipboardSnapshot
{
    private readonly string? _text;
    private readonly BitmapSource? _bitmap;
    private readonly StringCollection? _files;

    private ClipboardSnapshot(string? text, BitmapSource? bitmap, StringCollection? files)
    {
        _text = text;
        _bitmap = bitmap;
        _files = files;
    }

    internal static ClipboardSnapshot Capture()
    {
        string? text = null;
        BitmapSource? bitmap = null;
        StringCollection? files = null;

        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }

            if (Clipboard.ContainsImage())
            {
                bitmap = Clipboard.GetImage();
                bitmap?.Freeze();
            }

            if (Clipboard.ContainsFileDropList())
            {
                files = Clipboard.GetFileDropList();
            }
        }
        catch (Exception)
        {
            // Clipboard ownership can change between format checks. A partial
            // snapshot is still safer than failing the entire extraction.
        }

        return new ClipboardSnapshot(text, bitmap, files);
    }

    internal bool TryRestore(uint expectedSequenceNumber)
    {
        if (NativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber)
        {
            return false;
        }

        try
        {
            var data = new DataObject();
            if (_text is not null)
            {
                data.SetText(_text);
            }

            if (_bitmap is not null)
            {
                data.SetImage(_bitmap);
            }

            if (_files is not null)
            {
                data.SetFileDropList(_files);
            }

            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
