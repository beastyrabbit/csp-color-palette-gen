# CSP Palette Companion

Pulls the colors out of your Clip Studio Paint canvas and hands you back a Color
Set you can drag straight into CSP. Twelve swatches from a finished painting in
about a fifth of a second.

![CSP Palette Companion after extracting a palette: twelve swatches and the drag chip](docs/assets/companion-result.png)

Windows 10 or 11, 64-bit. Clip Studio Paint PRO or EX.

## Download

From the [releases page](https://git.heerlab.com/beasty/csp-color-palette-gen/releases).

| File | Size | You need |
| --- | --- | --- |
| `CSP-Palette-Companion-<version>-win-x64-needs-dotnet8.exe` | 2.4 MB | the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| `CSP-Palette-Companion-<version>-win-x64-standalone.exe` | 68.7 MB | nothing |

Same app, both files. Take the small one unless you would rather not install a
runtime. No installer either way — put the `.exe` where you want it and run it.

If your antivirus complains, see [Antivirus](#antivirus).

## How to use it

1. **Open your artwork in Clip Studio Paint.**

2. **In CSP, turn on Companion Mode.** `File > Connect to smartphone`
   (German: `Datei > Mit Smartphone verbinden...`). A window with a QR code
   appears — leave it open.

   This menu item is a toggle with a checkmark, not a dialog. Choosing it again
   while it is already on switches Companion Mode **off**.

3. **Start CSP Palette Companion and select `Connect`.** It looks for the QR
   code on screen and keeps looking until it connects or you select `Stop`. The
   dot next to the title turns green and reads `Connected`.

4. **Pick a source and your counts.** `Canvas` is the whole visible image and
   needs no setup. See [Sources](#sources) for the other three. Major and minor
   default to 6 and 6.

5. **Select `Extract palette`.** The swatches appear, and the file is written to
   `%LOCALAPPDATA%\CSP Palette Companion\Palettes`. `Show file` opens the
   folder.

6. **Drag the green `Drop onto CSP Color Set` bar onto CSP's Color Set palette.**
   That is the whole handoff — CSP imports it as a new Color Set. Drag the bar
   itself, not a swatch.

The app does not click through CSP's menus to import for you. CSP's palette
controls are custom-drawn and localized; the drag is the handoff that keeps
working.

## Sources

![Settings, showing the capture toggles](docs/assets/companion-settings.png)

| Source | What it reads | What it needs |
| --- | --- | --- |
| **Canvas** | The whole visible canvas | Companion canvas capture (on by default) and a live connection |
| **Layer** | The active layer | Clipboard capture. Deselect in CSP first |
| **Selection · Canvas** | Everything visible inside your selection | Clipboard capture, Run selected CSP Auto Action, and [an Auto Action you build](docs/selection-canvas-setup.md) |
| **Selection · Layer** | Selected pixels on the active layer | Clipboard capture and an active selection |

`Layer` stops if what it copied is not the size of the canvas — that means
something was still selected. `Selection · Layer` refuses a full-canvas result
for the same reason in reverse. CSP cannot be asked whether a selection exists,
so these guards are how the app avoids handing you a palette of the wrong thing.

`Selection · Canvas` is the only source that needs setup, because CSP will run a
command from Quick Access but will not tell an app what that command actually
does. You record a three-step Auto Action yourself and point the app at it. The
app checks that the exact command is still there before each run, but it cannot
see the steps inside it — if you point it at a different action, it will run
that instead. Build it from the [setup guide](docs/selection-canvas-setup.md)
and test it on a scrap document.

## How the palette is made

Your picture gets shrunk down small. Pixels that are nearly see-through, nearly
black or nearly white are thrown away — they are not colors you would paint
with. Every fifth pixel that survives goes into a bucket with the pixels that
look most like it. Each bucket gets averaged, and that average is one swatch.

Then the buckets get split two ways:

- **Major colors** are the biggest buckets — the colors that cover the most
  picture.
- **Minor colors** are the odd ones out — small patches that are too different
  from everything else to be ignored.

Here is the test image — 1600x1000, six major and six minor, 220 ms:

![The sample artwork: a sunset over a ridge and water, with a few tiny colored dots](docs/assets/sample-artwork.png)

**Major** — the big stuff. Sky, ridge, water, sun.

| | | | | | |
| --- | --- | --- | --- | --- | --- |
| `#1A2535` | `#48334D` | `#3D4B65` | `#804C58` | `#B26367` | `#F4B66E` |
| deep sky | purple ridge | sky over ridge | dusk | water | sun |

**Minor** — the odd ones out.

| | | | | | |
| --- | --- | --- | --- | --- | --- |
| `#EC6CA8` | `#77DAE6` | `#276060` | `#674153` | `#252346` | `#4A3F68` |
| pink dots | cyan dot | green hill | ridge edge | night sky | ridge shadow |

Look at the picture again. The cyan dot up on the left and the five pink dots
down in the shadow are together well under one percent of it. If you only asked
for the biggest colors they would never show up — they would be swallowed by the
sky. They are the two colors an artist would actually want to know about.

Small and distinct beats large and dull. That is what the minor half is for.

The same picture always gives the same palette. Run it twice, get the same
twelve colors.

### The technical version

- Downscales to 1200 px on the long side, never upscales.
- Drops alpha < 128, near-black and near-white pixels; samples every 5th
  survivor.
- Deterministically seeded k-means++, max 20 iterations. Major colors ordered by
  cluster population.
- Minor colors are brightness-ordered candidates selected by RGB distance
  thresholds, so small distinct clusters are not crowded out by large ones.
- Duplicates removed; the app reports when fewer distinct colors exist than you
  asked for.
- Output is RGB ACO v1 plus a named UTF-16BE ACO v2 section.

## CSP Mux

Clip Studio Paint accepts exactly **one** Companion Mode connection. Connect
this app and nothing else can connect. [CSP Mux](https://git.heerlab.com/beasty/csp-app-multiplexer)
takes that one connection and re-shares it, so several tools can talk to CSP at
the same time.

![CSP Mux sharing its connection, with the proxy QR code visible](docs/assets/mux-sharing.png)

Turn on **Use CSP Mux when it is running** in Settings. The Companion then reads
the Mux handoff file and connects through the proxy instead of scanning CSP's
QR code, and its status reads `Ready · through CSP Mux`.

## Antivirus

Both downloads may get flagged. Here is why, and what to do.

**Why.** They are single-file bundles: the whole app is packed into one `.exe`
that unpacks itself into a temp folder when you launch it. Heuristic scanners
score self-extracting behavior as suspicious on its own. The builds are also
unsigned — code signing certificates cost money this project does not have.
Nothing about the flag is specific to this app; it is the packaging.

**What helps.** The 2.4 MB framework-dependent build trips scanners less often
than the 68.7 MB standalone one, because there is far less packed inside it.

**Check the file first.** Every release ships `SHA256SUMS.txt`. Compare:

```powershell
Get-FileHash "CSP-Palette-Companion-1.0.0-win-x64-standalone.exe" -Algorithm SHA256
```

against the line for that filename. If it matches, the file is the one that was
built and published. If it does not, delete it and download again.

**Add an exclusion** (Microsoft Defender): Windows Security > Virus & threat
protection > Manage settings > Exclusions > Add an exclusion > File, and pick
the `.exe`. Other scanners have the same setting under a different name. Only do
this after the hash checks out.

## What it can read

Every capability is a toggle in Settings. Anything that reaches past a read-only
canvas is off until you turn it on.

| Toggle | Default | What it allows |
| --- | --- | --- |
| Companion canvas capture | **On** | Read-only canvas pixels over the Companion connection |
| Clipboard capture | **Off** | Copying pixels through the Windows clipboard |
| Run selected CSP Auto Action | **Off** | Running the one Quick Access command you selected |
| Use CSP Mux when it is running | **Off** | Reading the Mux handoff file to connect through the proxy |
| System tray | **On** | Close hides the window; Exit lives in the tray menu |

- No internet traffic. The only connection is TCP to the loopback or
  private-network address decoded from CSP's own QR code. Pairing endpoints are
  restricted to those ranges and the pairing secret is not saved.
- Image processing and ACO writing happen on your machine.
- It writes two things: `settings.json` and the generated `.aco` files, both
  under `%LOCALAPPDATA%\CSP Palette Companion`. It does not touch CSP's files or
  databases.
- Clipboard modes snapshot text, bitmap and file-drop formats and restore them
  after CSP copies. Clipboard history, cloud clipboard, delayed rendering and
  private formats cannot be restored generically. A newer clipboard change of
  yours is never overwritten.
- Companion Mode is an unofficial, reverse-engineered integration. It may need
  updating if CSP changes its wire protocol.

Verified against native Windows CSP PRO 4.0.10, German UI.

## Build from source

.NET 8 SDK. Windows only — WPF does not build on Linux.

```powershell
dotnet restore CspPaletteCompanion.sln
dotnet build CspPaletteCompanion.sln -c Release --no-restore
dotnet test CspPaletteCompanion.sln -c Release --no-build --no-restore
```

Both release executables, hashes and notices:

```powershell
tools\publish-local.ps1 -Version 1.0.0 -Tag v1.0.0
```

Output lands in `dist\release\`. Trimming and NativeAOT are not options here —
the app uses WinForms for the tray icon and QR decoding, and the SDK refuses to
trim WinForms.

## Links

- [Wiki](https://git.heerlab.com/beasty/csp-color-palette-gen/wiki)
- [Issues](https://git.heerlab.com/beasty/csp-color-palette-gen/issues)
- [Selection · Canvas setup guide](docs/selection-canvas-setup.md)
- [CSP Mux](https://git.heerlab.com/beasty/csp-app-multiplexer)

## Third-party components

Companion Mode follows the MIT-licensed protocol work in
[`chocolatkey/clipremote`](https://github.com/chocolatkey/clipremote). QR
decoding uses ZXing.Net. Full details in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md), which ships with every
build.

## License

Copyright (C) 2026 beasty. [GNU General Public License v3.0](LICENSE).
