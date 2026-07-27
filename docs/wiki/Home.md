# CSP Palette Companion

Windows app for Clip Studio Paint PRO/EX. Reads pixels from CSP and writes a
major/minor palette as an Adobe Color Swatch (`.aco`) you drag onto CSP's Color
Set palette.

![CSP Palette Companion after an extraction](../assets/companion-result.png)

Window 460 x 620. Closing hides it to the tray; **Exit** is in the tray menu.

## Sources

| Source | Requires |
| --- | --- |
| **Canvas** — whole visible canvas | Companion canvas capture, on by default |
| **Layer** — active layer | Clipboard capture. Deselect in CSP first. |
| **Selection · Canvas** — merged visible pixels inside the selection | Clipboard capture plus [an Auto Action](Selection-Canvas-Auto-Action) |
| **Selection · Layer** — selected pixels on the active layer | Clipboard capture. A canvas-sized result is rejected. |

## Workflow

1. Open a document in CSP.
2. CSP: **File > Connect to smartphone** (*Datei > Mit Smartphone verbinden…*) — a toggle, so selecting it while on turns Companion Mode off.
3. Companion: **Connect**. It scans for the QR code until it connects or you select **Stop**.
4. Pick a source and the major/minor counts, then **Extract palette**.
5. Drag the green **Add** card onto CSP's Color Set palette.

![Settings page](../assets/companion-settings.png)

CSP allows one Companion connection.
[CSP Mux](https://git.heerlab.com/beasty/csp-app-multiplexer) re-shares it;
enable **Use CSP Mux when it is running**.

## Pages

[Installation](Installation) ·
[Selection Canvas Auto Action](Selection-Canvas-Auto-Action) ·
[How Palette Extraction Works](How-Palette-Extraction-Works) ·
[Troubleshooting](Troubleshooting) ·
[Build from source](Build-from-Source)

Settings and palettes live in `%LOCALAPPDATA%\CSP Palette Companion`. Processing
is local. GPL-3.0.
