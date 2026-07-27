# Selection · Canvas Auto Action

The imported action is named **Sichtbare Ebenen kopieren**. It creates a
temporary merged copy of all visible layers, copies it to the clipboard, and
deletes the temporary layer again. The original layer stack stays unchanged.

## Download

- [Download the `.laf` set](https://git.heerlab.com/beasty/csp-color-palette-gen/raw/branch/main/docs/assets/CSP_Palette_Companion.laf)
- [Download the complete ZIP](https://git.heerlab.com/beasty/csp-color-palette-gen/raw/branch/main/docs/assets/CSP_Palette_Companion_AutoAction.zip)

## Written setup — step by step

### 1. Import the downloaded set

1. In Clip Studio Paint, open **Window > Auto Action**
   (*Fenster > Auto-Aktion*).
2. Open the Auto Action palette menu.
3. Choose **Import set…** (*Set importieren…*).
4. Select `CSP_Palette_Companion.laf`.
5. Confirm that **Sichtbare Ebenen kopieren** appears in the palette.

### 2. Add the action to Quick Access

1. Open **Window > Quick Access** (*Fenster > Schnellzugriff*).
2. Drag **Sichtbare Ebenen kopieren** from Auto Action into a Quick Access set.

### 3. Select it in Palette Companion

1. Connect CSP Palette Companion to Clip Studio Paint.
2. Open **Settings**.
3. Enable **Clipboard capture**.
4. Enable **Run selected CSP Auto Action**.
5. Select **Refresh CSP actions**.
6. Choose **Sichtbare Ebenen kopieren**.

### 4. Test it once

1. Use a disposable document with several visible layers.
2. Make a small selection.
3. Run **Sichtbare Ebenen kopieren**.
4. Paste the clipboard contents.
5. Confirm that the pasted result contains the merged visible pixels and that
   the original layer stack is unchanged.

> CSP exposes only the command name over Companion Mode. It does not expose the
> recorded steps, so test the selected action before using it on artwork.

## Build the action manually instead

1. Open **Window > Auto Action** (*Fenster > Auto-Aktion*).
2. Create an action named **Sichtbare Ebenen kopieren**.
3. Start recording.
4. Record exactly these three commands, in this order:

| # | English | German |
| --- | --- | --- |
| 1 | Layer > Merge visible to new layer | Ebene > Kopien sichtbarer Ebenen kombinieren |
| 2 | Edit > Copy | Bearbeiten > Kopieren |
| 3 | Layer > Delete layer | Ebene > Ebene löschen |

5. Stop recording.
6. Verify that the action contains only those three enabled commands.
7. Continue with **Add the action to Quick Access** above.

## Use it

Select a region in CSP, choose **Selection · Canvas**, then
**Extract palette**. The app focuses CSP, runs the selected Auto Action, reads
the clipboard, and restores the previous clipboard contents.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| **Refresh CSP actions** is empty, or the action is missing | Connect first and make sure the action is in a Quick Access set. |
| **Selection · Canvas** is disabled | Enable both capture toggles and select one Auto Action. |
| The palette covers the whole canvas | No selection was active. CSP does not expose selection state. |
| A merged layer remains, or CSP waits for input | A step is missing, extra, or requires confirmation. Import the tested set again or re-record it. |
| The command is no longer enabled | Add it to Quick Access again, refresh, and re-select it. |
