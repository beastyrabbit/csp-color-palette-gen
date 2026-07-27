#requires -Version 7
<#
.SYNOPSIS
    Builds the two release downloads for CSP Palette Companion, win-x64.

.DESCRIPTION
    Produces exactly what .forgejo/workflows/release.yml produces — the workflow calls this
    same script — so a release can be cut by hand without a runner.

        CSP-Palette-Companion-<version>-win-x64-needs-dotnet8.exe
            Framework-dependent, single file. Requires the .NET 8 Desktop Runtime.
        CSP-Palette-Companion-<version>-win-x64-standalone.exe
            Self-contained, single file, compressed. No prerequisites.
        CSP_Palette_Companion.laf
            Importable CSP Auto Action set for merged selection capture.
        CSP_Palette_Companion_AutoAction.zip
            Auto Action set, illustrated guide, and layered sample artwork.
        THIRD-PARTY-NOTICES.md
        SHA256SUMS.txt          sha256sum format, covers every shipped file above
        release-body.md         the release description (not an asset)

    Trimming and NativeAOT are not options here: the app references WinForms (NotifyIcon,
    ZXing), and PublishTrimmed fails with NETSDK1175.

.EXAMPLE
    ./tools/publish-local.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    # Assembly/file version. Defaults to the tag with its leading "v" removed.
    [string] $Version,

    # Release tag. Defaults to the newest tag reachable from HEAD.
    [string] $Tag,

    # Where the finished files land. Cleared first.
    [string] $OutputDirectory,

    # Used to build the antivirus link in release-body.md.
    [string] $RepoUrl = 'https://git.heerlab.com/beasty/csp-color-palette-gen'
)

$ErrorActionPreference = 'Stop'

# UTF-8, no BOM, LF endings — so SHA256SUMS.txt verifies with `sha256sum -c` unchanged.
function Write-Text([string] $path, [string] $text) {
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

# Invariant culture, so a German-locale machine does not write "2,4 MB".
function Format-Size([string] $name) {
    [string]::Format([cultureinfo]::InvariantCulture, '{0:N1} MB',
        ((Get-Item (Join-Path $out $name)).Length / 1MB))
}

$repoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project    = Join-Path $repoRoot 'src/CspPaletteCompanion.App/CspPaletteCompanion.App.csproj'
$notices    = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
$actionSet  = Join-Path $repoRoot 'docs/assets/CSP_Palette_Companion.laf'
$actionPack = Join-Path $repoRoot 'docs/assets/CSP_Palette_Companion_AutoAction.zip'
$builtExe   = 'CSP Palette Companion.exe'
$stem       = 'CSP-Palette-Companion'

# ---- version ---------------------------------------------------------------------------
if (-not $Tag) { $Tag = (& git -C $repoRoot describe --tags --abbrev=0 2>$null) }
if (-not $Version -and $Tag) { $Version = $Tag -replace '^v', '' }
if (-not $Version) { $Version = '0.0.0-dev' }
if (-not $Tag) { $Tag = "v$Version" }

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'dist/release' }
$out     = [IO.Path]::GetFullPath($OutputDirectory)
$staging = Join-Path $out '.staging'

foreach ($d in @($out, $staging)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null

Write-Host "version $Version   tag $Tag"
Write-Host "output  $out"

# ---- publish ---------------------------------------------------------------------------
# IncludeAllContentForSelfExtract is not optional. Without it a self-contained single-file
# publish leaves five native WPF DLLs (D3DCompiler_47_cor3, wpfgfx_cor3,
# PresentationNative_cor3, PenImc_cor3, vcruntime140_cor3 — 8.4 MB) loose beside the .exe,
# and both builds leave docs/selection-canvas-setup.md loose, which the Setup guide button
# reads from AppContext.BaseDirectory. With it, each download is one file that works.
$common = @(
    '-c', 'Release'
    '-r', 'win-x64'
    '--nologo'
    '-p:PublishSingleFile=true'
    '-p:IncludeAllContentForSelfExtract=true'
    "-p:Version=$Version"
)

$builds = @(
    @{
        Name  = "$stem-$Version-win-x64-needs-dotnet8.exe"
        Dir   = Join-Path $staging 'framework-dependent'
        Extra = @('--self-contained', 'false')
    }
    @{
        Name  = "$stem-$Version-win-x64-standalone.exe"
        Dir   = Join-Path $staging 'self-contained'
        # EnableCompressionInSingleFile is what roughly halves this build.
        Extra = @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true')
    }
)

$assets = @()
foreach ($b in $builds) {
    Write-Host ''
    Write-Host "publishing $($b.Name)"
    & dotnet publish $project @common @($b.Extra) -o $b.Dir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($b.Name)" }

    $src = Join-Path $b.Dir $builtExe
    if (-not (Test-Path $src)) { throw "expected $src" }

    # Only the .exe ships. Anything else in the publish folder (other than symbols) would
    # be a runtime dependency left behind, so fail rather than ship a broken download.
    $stray = Get-ChildItem $b.Dir -Recurse -File |
             Where-Object { $_.Name -ne $builtExe -and $_.Extension -ne '.pdb' }
    if ($stray) {
        throw "publish left files beside the .exe: $($stray.Name -join ', ')"
    }

    Copy-Item $src (Join-Path $out $b.Name) -Force
    $assets += $b.Name
}

foreach ($source in @($actionSet, $actionPack)) {
    $name = Split-Path $source -Leaf
    Copy-Item $source (Join-Path $out $name) -Force
    $assets += $name
}

Copy-Item $notices (Join-Path $out 'THIRD-PARTY-NOTICES.md') -Force
$assets += 'THIRD-PARTY-NOTICES.md'

Remove-Item $staging -Recurse -Force

# ---- checksums -------------------------------------------------------------------------
# LF endings and no BOM, so `sha256sum -c SHA256SUMS.txt` works as-is.
$lines = foreach ($name in $assets) {
    $hash = (Get-FileHash (Join-Path $out $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
Write-Text (Join-Path $out 'SHA256SUMS.txt') (($lines -join "`n") + "`n")

# ---- release body ----------------------------------------------------------------------
$small = $assets[0]
$big   = $assets[1]
$autoAction = Split-Path $actionSet -Leaf
$autoActionPack = Split-Path $actionPack -Leaf
$guideUrl = "$RepoUrl/src/tag/$Tag/docs/selection-canvas-setup.md"
$installUrl = "$RepoUrl/wiki/Installation"

$body = @"
The first public release of **CSP Palette Companion** turns artwork in CLIP STUDIO
PAINT into a ready-to-import Color Set. It extracts a balanced set of major colors
and distinctive accents, then writes a named Adobe Color Swatch file that can be
dragged straight onto CSP's Color Set palette.

## Highlights

- Capture the complete canvas through CSP Companion Mode without touching the clipboard.
- Extract from the active layer or a bounded selection.
- Generate deterministic major and minor colors with transparent, near-black, and
  near-white pixels filtered out.
- Click a generated swatch to set CSP's current drawing color.
- Drag the finished ``.aco`` file directly from the app into CSP.
- Connect through CSP Mux when it is available.
- Run in the notification area with explicit permissions for canvas, clipboard,
  and Auto Action access.
- Import the included Auto Action for merged, multi-layer selection capture:
  [setup guide]($guideUrl).

## Downloads

Windows 10 or 11, 64-bit. CLIP STUDIO PAINT PRO or EX.

| Download | Size | You need |
| --- | --- | --- |
| ``$small`` | $(Format-Size $small) | the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| ``$big`` | $(Format-Size $big) | nothing |

Choose the smaller download if the .NET 8 Desktop Runtime is already installed.
Both downloads contain the same application and require no installer.

### Optional Auto Action

| Download | Includes |
| --- | --- |
| ``$autoAction`` | Importable CSP Auto Action set |
| ``$autoActionPack`` | Auto Action set, illustrated guide, and layered sample artwork |

The Auto Action is needed only for merged **Selection · Canvas** capture across
multiple layers. Layer and full-canvas extraction work without it.

## Quick start

1. Open artwork in CSP.
2. In CSP, open **File → Connect to smartphone** and leave its QR code visible.
3. Start CSP Palette Companion and select **Connect**.
4. Choose a source and the number of major/minor colors.
5. Select **Extract palette**.
6. Drag **Drop onto CSP Color Set** onto CSP's Color Set palette.

## Integrity and antivirus

``SHA256SUMS.txt`` holds the SHA-256 of each file. To check one:

``````
Get-FileHash "$big" -Algorithm SHA256
``````

These executables are currently unsigned single-file bundles. Some antivirus products
may flag that packaging style heuristically. Verify the SHA-256 checksum and see the
[installation notes]($installUrl) if Windows or antivirus software blocks the download.

Thank you for trying the first release. Bug reports and palette-quality examples are
welcome in the repository's issue tracker.
"@

Write-Text (Join-Path $out 'release-body.md') ($body -replace "`r`n", "`n")

# ---- report ----------------------------------------------------------------------------
Write-Host ''
Get-ChildItem $out -File |
    Where-Object { $_.Name -ne 'release-body.md' } |
    Select-Object Name, @{ n = 'Size'; e = { Format-Size $_.Name } }, Length |
    Format-Table -AutoSize
