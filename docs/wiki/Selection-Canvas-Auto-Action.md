# Selection · Canvas Auto Action

**Selection · Canvas** extracts a palette from the merged visible result inside
your current selection. It is the only source that needs setup in Clip Studio
Paint first: you record a three-step Auto Action, register it in Quick Access,
then point the app at it.

Budget about five minutes. CSP menu paths are given in English with the German
in parentheses.

## Why an Auto Action is needed

Companion Mode can run a command that is already registered in **Quick Access**.
It offers no way to ask CSP what the active selection is, and no way to ask for
the merged visible pixels inside it.

So the app does not query. It has CSP run a recorded action that produces the
pixels itself:

| Without the action | With the action |
| --- | --- |
| App asks CSP for the selection → no such request exists | App triggers your Quick Access command |
| App asks for merged visible pixels → no such request exists | CSP merges the visible result, copies it, and removes the temporary layer |
| — | App reads the clipboard and extracts the palette |

That is also why **Clipboard capture** is required: the clipboard is the return
path.

## Before you start

| Requirement | Why |
| --- | --- |
| A scrap document open in CSP | You will test the action on it, not on artwork |
| Companion Mode enabled (**File > Connect to smartphone** / *Datei > Mit Smartphone verbinden…*) | The app reads the Quick Access list over the connection |
| Palette Companion connected | **Refresh** only returns commands while connected |

## Part 1 — Record the action

### 1. Open the Auto Action palette

**Window > Auto Action** (*Fenster > Auto-Aktion*).

![The Auto Action palette in CSP](../assets/autoaction-01-auto-action-palette.png)

### 2. Create a new action

Use the palette menu to add a new action to a set. Give it a name you will
recognise in a list of Quick Access commands — `Palette Companion selection` is
better than `Action 1`.

![Creating a new Auto Action](../assets/autoaction-02-new-action.png)

### 3. Start recording

Select the record button at the bottom of the palette. Everything you do in CSP
from here is captured, so do not click anything that is not one of the three
steps.

![Recording started](../assets/autoaction-03-record.png)

### 4. Record the three steps

Run these three menu commands, in this order, and nothing else.

| # | English | German |
| --- | --- | --- |
| 1 | Layer > Merge visible to new layer | Ebene > Auf neue Ebene zusammenfassen |
| 2 | Edit > Copy | Bearbeiten > Kopieren |
| 3 | Layer > Delete layer | Ebene > Ebene löschen |

Step 1 flattens what is visible into a new layer above the stack. Step 2 copies
it — with a selection active, CSP copies only what is inside the selection.
Step 3 removes the temporary layer, so the document ends where it started.

![Step 1, merge visible to new layer](../assets/autoaction-04-merge-visible.png)

![Step 2, copy](../assets/autoaction-05-copy.png)

![Step 3, delete layer](../assets/autoaction-06-delete-layer.png)

### 5. Stop recording and check the steps

Stop recording, then expand the action. It must contain exactly three steps in
that order. If a fourth step got recorded, delete it.

![The three recorded steps](../assets/autoaction-07-stop-recording.png)

### 6. Test it on the scrap document

Make a rectangular selection, run the action, and confirm:

- the layer stack is unchanged afterwards,
- the selection is unchanged afterwards,
- pasting into a new document gives you the selected region, merged.

Do this before you point the app at it.

## Part 2 — Add it to Quick Access

Companion Mode can only see Quick Access commands. An Auto Action that is not
registered there does not appear in the app.

Open **Window > Quick Access** (*Fenster > Schnellzugriff*) and drag the action
onto a Quick Access set.

![Registering the action in Quick Access](../assets/autoaction-08-quick-access-register.png)

![The action in the Quick Access set](../assets/autoaction-09-quick-access-set.png)

## Part 3 — Point the app at it

![Palette Companion settings](../assets/autoaction-10-companion-settings.png)

1. Open **Settings** in CSP Palette Companion.
2. Enable **Clipboard capture**.
3. Enable **Run selected CSP Auto Action**.
4. Select **Refresh**.
5. Choose your action from the list.

![Choosing the action](../assets/autoaction-11-choose-action.png)

The app stores the exact Companion protocol identity of the command — not just
its display name — and re-checks that it is still enabled before every run.

Selection · Canvas becomes selectable on the main page once both toggles are on
and an action is chosen.

![Selection · Canvas extraction result](../assets/autoaction-12-selection-canvas-result.png)

## Safety caveat

**CSP exposes only a command's name over Companion Mode, never its recorded
steps. The app cannot verify what the action you choose actually does.** If you
select an action that deletes layers, flattens the document, or exports a file,
the app will run it and will not know the difference.

Build the action from this guide, test it on a scrap document, and pick that one
from the list.

## Using it

1. Make a selection in CSP.
2. In Palette Companion, choose **Selection · Canvas**.
3. Select **Extract palette**.

The app focuses CSP, triggers the Quick Access command, reads the clipboard, and
restores the previous clipboard contents when it can. Windows clipboard history,
cloud clipboard, delayed rendering and private formats cannot be restored
generically, and a newer clipboard change made by you is never overwritten.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| **Refresh** returns nothing | Not connected to CSP | Connect first. The Quick Access list is read over the Companion connection. |
| The action is missing from the list | It is not in a Quick Access set | Drag it onto a Quick Access set, then Refresh again. |
| **Selection · Canvas** is greyed out | Clipboard capture off, Auto Action toggle off, or no action chosen | Enable both toggles and pick an action. |
| Extraction stops with a permission message | A toggle was turned off after the action was chosen | Re-enable it in Settings. |
| The palette covers the whole canvas | No selection was active when you extracted | Make a selection in CSP first. CSP cannot be asked whether one exists. |
| "No eligible pixels" | The selected region is empty, fully transparent, near-black or near-white | Select a region with mid-tone colour. See [How Palette Extraction Works](How-Palette-Extraction-Works). |
| A merged layer is left behind in CSP | Step 3 was not recorded | Re-record the action with all three steps. |
| The selection is gone after extraction | A recorded step deselected | Re-record. Only the three steps above belong in the action. |
| CSP opens a dialog and the run hangs | A recorded step needs confirmation | Re-record with **Merge visible to new layer**, not a flatten or export command. |
| The app reports the command is no longer enabled | The Quick Access item was removed or disabled in CSP | Re-register the action, then Refresh and re-select it. |
| Nothing happens and CSP never comes forward | CSP is on another desktop or minimised to a state Windows will not raise | Bring CSP up manually, then extract again. |
| The clipboard did not come back | A format Windows cannot re-offer generically | Expected. Text, bitmap and file drops are restored; the rest cannot be. |
