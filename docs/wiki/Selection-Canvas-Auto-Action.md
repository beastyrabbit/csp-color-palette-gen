# Selection · Canvas Auto Action

Companion Mode cannot ask CSP what your selection is, so **Selection · Canvas**
triggers a Quick Access command that copies the merged visible pixels inside it
to the clipboard. You record that command once — five minutes.

> **CSP exposes only a command's name over Companion Mode, never its recorded
> steps. The app cannot verify what the action you pick does.** An action that
> flattens the document or exports a file gets run just as readily. Record the
> action below, test it, and pick that one.

Menu paths: English, German in parentheses. Start with a scrap document open,
Companion Mode on (**File > Connect to smartphone** / *Datei > Mit Smartphone
verbinden…*) and Palette Companion connected — the Quick Access list is read
over that connection.

## Part 1 — Record the action

**Window > Auto Action** (*Fenster > Auto-Aktion*).

![Auto Action palette](../assets/autoaction-01-auto-action-palette.png)

Add an action to a set. Name it for a command list — `Palette Companion
selection`, not `Action 1`.

![New action](../assets/autoaction-02-new-action.png)

Select the record button. Everything you do from here is captured.

![Recording started](../assets/autoaction-03-record.png)

Run these three menu commands in order, and nothing else.

| # | English | German |
| --- | --- | --- |
| 1 | Layer > Merge visible to new layer | Ebene > Auf neue Ebene zusammenfassen |
| 2 | Edit > Copy | Bearbeiten > Kopieren |
| 3 | Layer > Delete layer | Ebene > Ebene löschen |

With a selection active, step 2 copies only what is inside it; step 3 leaves the
document as it was.

![Merge visible to new layer](../assets/autoaction-04-merge-visible.png)

![Copy](../assets/autoaction-05-copy.png)

![Delete layer](../assets/autoaction-06-delete-layer.png)

Stop recording and expand the action: exactly three steps, in that order.

![The three recorded steps](../assets/autoaction-07-stop-recording.png)

Test it on the scrap document — layer stack and selection unchanged, and pasting
gives you the selected region, merged.

## Part 2 — Add it to Quick Access

Companion Mode sees Quick Access commands only. Open **Window > Quick Access**
(*Fenster > Schnellzugriff*) and drag the action onto a set.

![Registering the action](../assets/autoaction-08-quick-access-register.png)

![The action in the set](../assets/autoaction-09-quick-access-set.png)

## Part 3 — Point the app at it

![Palette Companion settings](../assets/autoaction-10-companion-settings.png)

In **Settings**, enable **Clipboard capture** and **Run selected CSP Auto
Action**, select **Refresh**, then choose your action. The app stores the
command's protocol identity, not its display name.

![Choosing the action](../assets/autoaction-11-choose-action.png)

![Selection · Canvas result](../assets/autoaction-12-selection-canvas-result.png)

## Using it

Select a region in CSP, choose **Selection · Canvas**, select **Extract
palette**. The app focuses CSP, runs the command and reads the clipboard. Text,
bitmaps and file drops are put back afterwards; other formats cannot be.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| **Refresh** returns nothing, or the action is missing | Connect first, and put the action in a Quick Access set. |
| **Selection · Canvas** greyed out | Both toggles on, one action chosen. |
| The palette covers the whole canvas | No selection was active. CSP cannot be asked whether one exists. |
| A layer is left behind, the selection is gone, or CSP hangs on a dialog | A step is missing, extra, or needs confirmation. Re-record. |
| The command is no longer enabled | It left Quick Access. Re-register, Refresh, re-select. |
