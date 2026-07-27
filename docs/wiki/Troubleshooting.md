# Troubleshooting

Auto Action problems:
[Selection Canvas Auto Action](Selection-Canvas-Auto-Action#troubleshooting).

## Connecting

| Symptom | Fix |
| --- | --- |
| **Offline** while CSP runs | A running CSP is not a connection. Enable Companion Mode, then **Connect**. |
| Scanning never finds the QR | Keep CSP's QR window in front, on the primary display. |
| Companion Mode switched off | **Connect to smartphone** is a toggle. Select it again. |
| Any **CSP Mux** message | Start sharing, set the Mux to **This computer only**, or scan its proxy QR. |

## Capture, extraction, app

| Symptom | Fix |
| --- | --- |
| A source is greyed out | Enable **Clipboard capture** (Layer, Selection) or **Companion canvas capture**. |
| A capture is refused over its size | Layer: deselect in CSP. Selection · Layer: make a selection. |
| Canvas gives another page, or opaque pixels | It reads Webtoon Preview and stops rather than guess. Use a clipboard source. |
| "No eligible pixels" | All transparent, near-black or near-white. Extract a mid-tone region. |
| Too few swatches, or all sky | Raise the minor count, or select a smaller region. |
| Dragging the card does nothing | Drop it on the Color Set palette, or **Show file** and import the `.aco`. |
| Closing the window did not exit | **Exit** in the tray menu, or turn off **System tray**. |
| Off-screen window, or settings reset | Delete `settings.json` under `%LOCALAPPDATA%\CSP Palette Companion`. |
| Missing framework, or antivirus quarantine | See [Installation](Installation). |

Still stuck: open an issue at
<https://git.heerlab.com/beasty/csp-color-palette-gen/issues> with your CSP
version and language, the build, the source, and the status text shown.
