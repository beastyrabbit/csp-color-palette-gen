# CSP Palette Companion

A Windows desktop app for Clip Studio Paint PRO/EX. It reads pixels from CSP,
extracts a major/minor colour palette, and writes an Adobe Color Swatch (`.aco`)
file you drag onto CSP's Color Set palette.

![CSP Palette Companion after an extraction](../assets/companion-result.png)

Window: 460 x 620. It lives in the system tray by default; the close button
hides it and **Exit** is in the tray menu.

## The four sources

| Source | Reads | Requires | Notes |
| --- | --- | --- | --- |
| **Canvas** | The whole visible canvas | Companion canvas capture | Read-only, no clipboard. Uses the Companion Mode connection. |
| **Layer** | The active layer | Clipboard capture | Deselect in CSP first. A copy that does not match a known canvas size is rejected. |
| **Selection · Canvas** | Merged visible pixels inside the selection | Clipboard capture + a CSP Auto Action | [Setup guide](Selection-Canvas-Auto-Action) — this one needs work before it runs. |
| **Selection · Layer** | Selected pixels on the active layer | Clipboard capture | Active-layer behaviour, not merged visible. A full-canvas-sized result is rejected. |

Canvas is enabled out of the box. The other three need **Clipboard capture** in
Settings; Selection · Canvas needs the Auto Action toggle as well.

## Workflow

1. Open a document in Clip Studio Paint.
2. In CSP, select **File > Connect to smartphone** (German: *Datei > Mit Smartphone verbinden…*) so the QR code is on screen.
3. In Palette Companion, select **Connect**. The app scans until it connects or you select **Stop**.
4. Choose a source and the major/minor counts.
5. Select **Extract palette**.
6. Drag the green **Add** card onto CSP's Color Set palette. CSP imports the ACO as a new Color Set.

**File > Connect to smartphone** is a toggle with a checkmark, not a dialog.
Selecting it while it is already enabled turns Companion Mode **off**.

## Connecting through CSP Mux

CSP offers one Companion Mode connection. [CSP Mux](https://git.heerlab.com/beasty/csp-app-multiplexer)
takes that connection and re-shares it, so Palette Companion can run alongside
other Companion tools.

Enable **Use CSP Mux when it is running** in Settings. When the Mux is sharing on
loopback, **Connect** routes through it and the status reads
`Ready — through CSP Mux`. Details: [Palette Companion Integration](https://git.heerlab.com/beasty/csp-app-multiplexer/wiki/Palette-Companion-Integration).

## Pages

| Page | Covers |
| --- | --- |
| [Installation](Installation) | The two downloads, the .NET 8 Desktop Runtime, SHA256 verification, antivirus, file locations |
| [Selection Canvas Auto Action](Selection-Canvas-Auto-Action) | Recording the CSP Auto Action, Quick Access, the app settings, the safety caveat |
| [How Palette Extraction Works](How-Palette-Extraction-Works) | The pipeline, the constants, the ACO format, determinism, a measured example |
| [Troubleshooting](Troubleshooting) | Connection, capture, extraction and import failures |

## Settings

![Settings page](../assets/companion-settings.png)

| Toggle | Effect |
| --- | --- |
| Companion canvas capture | Allows read-only canvas reads over Companion Mode. On by default. |
| Clipboard capture | Allows the Layer and Selection sources, which route pixels through the Windows clipboard. |
| Run selected CSP Auto Action | Allows the app to run one Quick Access command you pick. Requires clipboard capture. |
| Use CSP Mux when it is running | Connects through the Mux instead of scanning CSP's QR. |
| System tray | Close hides the window; Exit lives in the tray icon menu. |

## File locations

| What | Path |
| --- | --- |
| Settings | `%LOCALAPPDATA%\CSP Palette Companion\settings.json` |
| Generated palettes | `%LOCALAPPDATA%\CSP Palette Companion\Palettes` |

## Scope

- Image processing and ACO generation are local. The only network traffic is the Companion Mode TCP connection to a loopback or private address decoded from a QR code.
- The app does not edit CSP's private files or databases.
- Palette import is not automated by clicking. CSP's palette controls are custom-rendered and localised; the draggable ACO is the stable handoff.
- Tested against native Windows CSP PRO 4.0.10 with a German UI. Companion Mode is a reverse-engineered integration and may break if CSP changes its wire protocol.

Licensed GPL-3.0.
