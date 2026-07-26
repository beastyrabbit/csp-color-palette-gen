using System.Diagnostics;
using System.IO;

namespace CspPaletteCompanion.App;

internal sealed class PaletteHandoffService
{
    internal string CreateOutputPath(string documentTitle)
    {
        var baseName = documentTitle.Split('(', StringSplitOptions.TrimEntries)[0]
            .Trim()
            .TrimEnd('*');
        if (string.IsNullOrWhiteSpace(baseName) ||
            baseName.Equals("CLIP STUDIO PAINT", StringComparison.OrdinalIgnoreCase))
        {
            baseName = $"CSP Palette {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        }

        var safeName = string.Concat(baseName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CSP Palette Companion",
            "Palettes");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{safeName}.aco");
    }

    internal void ShowInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }
}
