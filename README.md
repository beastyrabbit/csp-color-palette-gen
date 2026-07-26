# CSP Palette Companion

A small Windows companion for Clip Studio Paint PRO/EX. It reads pixels from
CSP or the Windows clipboard, extracts a deterministic major/minor palette, and
writes a named Adobe Color Swatch (`.aco`) file that CSP can import as a new
Color Set.

## Current workflow

1. Open a document in Clip Studio Paint.
2. Run `CSP Palette Companion.exe`.
3. For direct Canvas access, select CSP's **Connect to smartphone** command so
   its QR code is visible, then select **Connect** in Palette Companion. The app
   keeps scanning until it connects or you select **Stop**.
4. Choose a source and the major/minor counts.
5. Select **Extract Palette**.
6. Drag the green **Add** card onto CSP's Color Set palette. CSP imports the ACO
   as a new Color Set.

The generated ACO remains available under:

```text
%LOCALAPPDATA%\CSP Palette Companion\Palettes
```

### Source behavior

- **Canvas** reads the flattened local preview after the explicit Companion
  connection is established. Pairing endpoints are limited to
  loopback/private-network addresses and the pairing secret is not saved. If
  Companion Mode is disconnected, an already-prepared merged clipboard image is
  used immediately as the fallback; Extract never starts a hidden QR scan.
- **Layer** focuses CSP and sends its default Copy shortcut. Deselect in CSP
  first. If the copied bitmap does not match known canvas dimensions, the app
  treats it as a cropped result and stops.
- **Selection** focuses CSP and copies pixels from the active/selected layers
  inside the current selection. It is deliberately labeled as active-layer
  compatibility behavior, not a merged visible selection. A full-canvas copy is
  rejected because CSP offers no supported active-selection query. A layer whose
  content bounds are smaller than the canvas can still produce a cropped copy
  without a selection, so confirm a bounded selection is active before running.

The app does not edit CSP private files or databases. Import automation is not
performed by coordinates because CSP palette controls are custom-rendered and
localized. The draggable ACO is the stable handoff.

## Palette behavior

- Downscales sources to a maximum dimension of 1200 px without upscaling.
- Ignores alpha below 128, near-black, and near-white pixels.
- Samples every fifth eligible pixel.
- Uses deterministically seeded k-means++ with at most 20 iterations.
- Orders major colors by population.
- Selects brightness-ordered minor candidates using RGB distance thresholds.
- Removes duplicates and reports when fewer distinct colors exist.
- Writes RGB ACO v1 and named UTF-16BE ACO v2 sections.

## Build and test

Prerequisite: .NET 8 SDK.

```powershell
dotnet restore CspPaletteCompanion.sln
dotnet build CspPaletteCompanion.sln -c Release --no-restore
dotnet test CspPaletteCompanion.sln -c Release --no-build --no-restore
```

Create a distributable single executable:

```powershell
dotnet publish src/CspPaletteCompanion.App/CspPaletteCompanion.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/win-x64
```

Generate disposable smoke fixtures and run the 4K benchmark:

```powershell
dotnet run --project tools/CspPaletteCompanion.SmokeFixture `
  -c Release -- artifacts/smoke --benchmark
```

## Privacy and clipboard limits

Image processing and ACO generation are local. The only network traffic is the
direct Companion Mode TCP connection to a private or local CSP address decoded
from CSP's QR code. The app does not contact an internet service.

For Layer and Selection, it snapshots ordinary text, bitmap, and file drop
clipboard formats and attempts to restore them after CSP Copy. Windows clipboard
history, cloud clipboard, delayed rendering, and private formats cannot be
restored generically; a newer user clipboard change is never overwritten.

## Known limitations

- Tested against native Windows CSP PRO 4.0.10 with a German UI.
- Companion Mode is an unofficial, reverse-engineered integration and may need
  updating if CSP changes its private wire protocol.
- The status indicator is green only for an authenticated Companion connection.
  A running CSP process by itself is shown as disconnected.
- Companion Canvas uses CSP's Webtoon Preview representation, whose pixels are
  opaque and whose available canvases can be ambiguous in multi-page documents.
  The app fails closed instead of guessing between matching canvases.
- The app cannot query CSP for active-selection presence. Layer mode therefore
  requires the user to deselect first and applies a dimension guard.
- Selection mode is active/selected-layer compatibility mode and deliberately
  rejects a full-canvas-sized result. CSP can still return smaller active-layer
  content bounds when no selection exists, which cannot be distinguished safely.
- The ACO drag handoff remains the supported import path; automatic palette-menu
  clicking is intentionally omitted.

## Third-party components

The Companion Mode implementation follows the MIT-licensed protocol work in
[`chocolatkey/clipremote`](https://github.com/chocolatkey/clipremote). QR
decoding uses ZXing.Net. See `THIRD-PARTY-NOTICES.md` in the source and
published output for license details.

## License

Copyright (C) 2026 beasty.

This project is licensed under the [GNU General Public License v3.0](LICENSE).
