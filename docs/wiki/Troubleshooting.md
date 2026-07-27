# Troubleshooting

Auto Action problems have their own table in
[Selection Canvas Auto Action](Selection-Canvas-Auto-Action#troubleshooting).

## Connecting

| Symptom | Cause | Fix |
| --- | --- | --- |
| Status stays **Offline** with CSP running | A running CSP process is not a connection. The indicator is green only for an authenticated Companion session. | Enable Companion Mode in CSP, then select **Connect**. |
| Scanning never finds the QR | The QR window is behind another window, off-screen, or on a display the app cannot capture | Bring CSP's QR window to the front on the primary display and leave it visible while scanning. |
| Selecting **Connect to smartphone** closed Companion Mode | It is a toggle with a checkmark, not a dialog | Select it again to turn it back on. The QR window reappears. |
| Connection drops when the QR window is closed | Expected while pairing | Keep the QR visible until the app reports a connection. |
| **CSP Mux is not sharing** | The Mux is running but not sharing | Start sharing in the Mux, or connect straight to CSP. |
| **CSP Mux is sharing on a network. Scan its QR.** | The Mux is bound to a LAN address; the handoff file is only trusted for loopback | Scan the Mux's proxy QR, or set the Mux to **This computer only**. |
| **Could not verify CSP Mux** | The Mux runs at a different integrity level, so its process cannot be inspected | Scan the Mux's proxy QR instead. |
| **CSP Mux is newer than this app** | Handoff schema version is ahead of this build | Update Palette Companion. |

## Sources and capture

| Symptom | Cause | Fix |
| --- | --- | --- |
| Layer or Selection sources are greyed out | Clipboard capture is off | Enable **Clipboard capture** in Settings. |
| Canvas is greyed out | Companion canvas capture is off | Enable it in Settings. |
| Layer capture stops with a size mismatch | The copied bitmap does not match a known canvas size, so it is treated as a cropped result | Deselect in CSP, then extract again. |
| Selection · Layer refuses a full-canvas result | A canvas-sized copy means no selection was active | Make a selection first. CSP offers no supported active-selection query. |
| Canvas returns something other than the current page | Multi-page documents can expose more than one matching canvas | The app stops instead of guessing. Reduce the ambiguity in CSP or use a clipboard source. |
| Canvas pixels are opaque where the artwork is transparent | Companion Canvas uses CSP's Webtoon Preview representation, whose pixels are opaque | Use a clipboard source when transparency matters. |
| The clipboard did not come back after extraction | Only text, bitmap and file drop formats can be restored generically | Expected. Clipboard history, cloud clipboard, delayed rendering and private formats cannot be restored. |

## Extraction

| Symptom | Cause | Fix |
| --- | --- | --- |
| "No eligible pixels" | Everything was transparent, near-black or near-white | Extract a region with mid-tone colour. Thresholds are in [How Palette Extraction Works](How-Palette-Extraction-Works). |
| Fewer swatches than requested | The image has fewer distinct colours than the requested count | Lower the count, or extract from a more varied region. |
| The palette misses a small accent | Raise the minor count | Minor colours are chosen by distance, not by area. |
| The palette is all sky | Raise the minor count, or select a smaller region | Major colours are ordered by area. |
| The same image gives a different palette | It should not | Extraction is deterministic on the downscaled pixels. Confirm the source really is the same — a Canvas read and a clipboard read of the same document are different images. |

## Importing into CSP

| Symptom | Cause | Fix |
| --- | --- | --- |
| Dragging the card does nothing | It was not dropped on the Color Set palette | Drop it on CSP's Color Set palette specifically. |
| You want the file instead | — | Select **Show file** to open the folder, or import from `%LOCALAPPDATA%\CSP Palette Companion\Palettes`. |
| You want automatic import | Not offered | CSP's palette controls are custom-rendered and localised. Clicking them by coordinates is unreliable, so the ACO drag is the supported path. |

## App behaviour

| Symptom | Cause | Fix |
| --- | --- | --- |
| Closing the window did not exit the app | Tray mode is on by default | Right-click the tray icon and select **Exit**, or turn off **System tray** in Settings. |
| The window opened off-screen | A saved position from a display that is gone | Delete `%LOCALAPPDATA%\CSP Palette Companion\settings.json` and restart. |
| Settings reset themselves | The settings file was unreadable or corrupt, so defaults were loaded | Nothing to fix. Re-set your toggles; they will persist. |
| Windows reports a missing framework at launch | The framework-dependent build without the .NET 8 Desktop Runtime | Install the runtime or use the self-contained build. See [Installation](Installation). |
| Antivirus quarantined the download | Unsigned single-file bundle | Verify the SHA256, then add an exclusion. See [Installation](Installation#antivirus-and-smartscreen). |

## Still stuck

Open an issue at
<https://git.heerlab.com/beasty/csp-color-palette-gen/issues> with:

- CSP version and UI language,
- which build you are running (2.3 MB or 73.2 MB),
- the source you selected,
- the exact status text the app showed.
