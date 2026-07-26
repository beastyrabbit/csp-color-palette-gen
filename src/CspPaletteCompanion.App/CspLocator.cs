using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CspPaletteCompanion.App;

internal sealed partial class CspLocator
{
    internal CspSession? Find()
    {
        return Process.GetProcessesByName("CLIPStudioPaint")
            .OrderByDescending(process => process.StartTime)
            .Select(FindForProcess)
            .OfType<CspSession>()
            .FirstOrDefault();
    }

    private static CspSession? FindForProcess(Process process)
    {
        var candidates = new List<(nint Handle, string Title)>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId != process.Id ||
                !NativeMethods.IsWindowVisible(handle) ||
                NativeMethods.GetWindow(handle, 4) != nint.Zero)
            {
                return true;
            }

            var title = ReadWindowText(handle);
            var className = ReadClassName(handle);
            if (!string.IsNullOrWhiteSpace(title) &&
                !className.Equals("SysShadow", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add((handle, title));
            }

            return true;
        }, nint.Zero);

        var selected = candidates
            .OrderByDescending(candidate =>
                candidate.Title.Contains("CLIP STUDIO PAINT", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => ParseCanvasSize(candidate.Title).HasValue)
            .FirstOrDefault();
        if (selected.Handle == nint.Zero)
        {
            return null;
        }

        return new CspSession(
            process.Id,
            selected.Handle,
            selected.Title,
            ReadVersion(process),
            ParseCanvasSize(selected.Title));
    }

    private static string ReadWindowText(nint handle)
    {
        var buffer = new StringBuilder(NativeMethods.GetWindowTextLength(handle) + 1);
        NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ReadClassName(nint handle)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ReadVersion(Process process)
    {
        try
        {
            return process.MainModule?.FileVersionInfo.ProductVersion ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static (int Width, int Height)? ParseCanvasSize(string title)
    {
        var match = CanvasSizeRegex().Match(title);
        return match.Success
            ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value))
            : null;
    }

    [GeneratedRegex(@"(\d+)\s*[x×]\s*(\d+)\s*px", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CanvasSizeRegex();
}

internal sealed record CspSession(
    int ProcessId,
    nint WindowHandle,
    string WindowTitle,
    string Version,
    (int Width, int Height)? CanvasSize);
