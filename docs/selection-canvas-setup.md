# Selection · Canvas setup

Clip Studio Paint's Companion Mode can run commands that are already registered
in **Quick Access**, but it cannot create or inspect the recorded steps of an
Auto Action.

Create an Auto Action in CSP with exactly these steps:

1. **Layer > Merge visible to new layer**
   (German: *Ebene > Kopien sichtbarer Ebenen kombinieren*)
2. **Edit > Copy** (*Bearbeiten > Kopieren*)
3. **Layer > Delete layer** (*Ebene > Ebene löschen*)

The action must leave the original layer stack and selection unchanged. Test it
on a disposable document before using it on artwork.

Then:

1. Add the Auto Action to a CSP **Quick Access** set.
2. Open **Settings** in CSP Palette Companion.
3. Enable **Clipboard capture**.
4. Enable **Run selected CSP Auto Action**.
5. Select **Refresh CSP actions**.
6. Choose the action you created.

The app stores the exact Quick Access command identity and verifies that it is
still enabled before each run. CSP does not expose the action's internal steps,
so the app cannot independently prove that a differently configured action is
safe.

When this project has a public repository, this file is intended to be the
target of the in-app setup-guide link.
