# Installation

Windows 10 or newer, x64. No installer: put the `.exe` anywhere.

| Build | Size | Prerequisite |
| --- | --- | --- |
| `CSP-Palette-Companion-win-x64.exe` | 2.3 MiB | .NET 8 Desktop Runtime. Flagged by scanners less often. |
| `CSP-Palette-Companion-win-x64-self-contained.exe` | 68.7 MiB | none |

Get **.NET Desktop Runtime 8.0.x, x64** from
<https://dotnet.microsoft.com/download/dotnet/8.0>. The plain .NET and ASP.NET
Core runtimes do not work; WPF needs the *Desktop* runtime. Check with
`dotnet --list-runtimes | Select-String WindowsDesktop`.

Verify the download against the SHA256 on the release page:

```powershell
Get-FileHash .\CSP-Palette-Companion-win-x64.exe -Algorithm SHA256
```

## Antivirus and SmartScreen

A single-file .NET app unpacks a runtime payload on first launch, exactly like
packed malware, and these binaries are unsigned — so heuristics score them. The
68.7 MiB build unpacks more and is flagged more often. Unsigned executables also
show **Windows protected your PC**: select **More info**, then **Run anyway**.

If a scanner quarantines the file, check the SHA256 first. A match means it is
byte-for-byte the published file: exclude that one file, not its folder (Windows
Security > Virus & threat protection > Manage settings > Exclusions > Add an
exclusion > File). No match: delete it. Report false positives at
<https://www.microsoft.com/en-us/wdsi/filesubmission>.

## Files

| What | Path |
| --- | --- |
| Settings | `%LOCALAPPDATA%\CSP Palette Companion\settings.json` |
| Palettes | `%LOCALAPPDATA%\CSP Palette Companion\Palettes` |
| Mux handoff | `%LOCALAPPDATA%\CSP Suite\mux-session.json`, read-only |

No registry keys; the pairing secret is never saved. Uninstall: exit from the
tray, delete the `.exe` and `%LOCALAPPDATA%\CSP Palette Companion`.

## Build from source

.NET 8 SDK on Windows; WPF does not build elsewhere. Publish
`src/CspPaletteCompanion.App` with `-r win-x64 -p:PublishSingleFile=true`, plus
`--self-contained true -p:IncludeNativeLibrariesForSelfExtract=true
-p:EnableCompressionInSingleFile=true` for the standalone build. Trimming and
NativeAOT are out: the tray icon references Windows Forms (`NETSDK1175`).
