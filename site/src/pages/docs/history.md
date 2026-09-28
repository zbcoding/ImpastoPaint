---
layout: ../../layouts/DocsLayout.astro
---

Every change you make to an image is recorded as a step, and the **History** pad lists them in order. You can walk back and forward one step at a time with **Undo** and **Redo**, or click any step in the list to jump straight to it.

## Undo and redo

| Command | Default shortcut |
|---|---|
| Undo | <kbd>Ctrl</kbd>+<kbd>Z</kbd> |
| Redo | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Z</kbd>, or <kbd>Ctrl</kbd>+<kbd>Y</kbd> |

Both are in the **Edit** menu and on the quick access toolbar, and the History pad has its own Undo and Redo buttons in its header.

History is per document: each open image has its own list, so undo in one tab cannot disturb another. It is not bounded by a step count - a long session keeps every step until the document is closed, which is why a very long painting session uses more memory over time.

## The History pad

Steps are listed oldest first, with the current position highlighted. The label on each step says what it was: *Paintbrush*, *Rectangle Select*, *Add New Layer*, *Fill Selection*, *Blur*, *Point Added*, and so on.

Clicking a step moves the document to that point, undoing or redoing as many steps as it takes. This is the fastest way to get back to how things looked before your last few edits, without working out how many times to press <kbd>Ctrl</kbd>+<kbd>Z</kbd>.

Steps that you have undone stay in the list, greyed, until you make a new edit. Making a new edit after undoing discards everything that was undone - there is no way back to it, so redo is only available while the pointer is behind the end of the list.

## What makes one step

- A brush stroke is one step, however many pixels it covers - the stroke is painted onto a scratch surface and committed to the layer when you release the button.
- Moving a selection and then releasing is one step, not one per mouse movement.
- Dragging a shape's control point is one step per adjustment, not per frame.
- An object's own edits are separate steps from the pixels: moving a text object's alignment, changing a shape's fill, or re-configuring a live effect each add their own entry, with labels like *Modified*, *Rotated*, *Rename Object*, *Reorder Object*, or *Hide Object*.
- Some operations add more than one entry on purpose, and the extra entries are not clutter - they are what makes the operation undoable in the right order. Rasterizing objects before an operation that needs flat pixels is pushed as its own step, so undoing the edit leaves the operation undone but the objects still rasterized. The warning dialog before that happens tells you so.
- Settings that change an object's look from a popover - opacity, blend mode - are pushed once when you close the popover, not on every slider frame, so one drag is one step.

If a tool is mid-operation when a history step is applied - you are part-way through typing text, or part-way through a shape - the tool commits first, so the pending edit becomes its own step rather than being lost.

## Unsaved changes and autosave

Impasto marks a document as clean at the point it was last saved. Undoing back to that point makes it clean again, so closing an image after undoing your last few edits does not prompt you to save. An image with unsaved changes shows an asterisk after its name in the image tab, and closing one asks first.

If Impasto does not shut down normally, it saves the open documents as OpenRaster files and offers to recover them the next time it starts. Recovered files live in the settings folder listed under [The interface](/docs/interface/#where-the-settings-live), and if one cannot be read - the file went missing, or is empty or incomplete - the recovery dialog says which document failed and why.

## Undo and text

While you are typing, <kbd>Ctrl</kbd>+<kbd>Z</kbd> undoes your last text edit (the last word or paste) rather than the last image operation, because the Text tool has its own undo stack for the text being typed. Press <kbd>Esc</kbd> to finish the text, and <kbd>Ctrl</kbd>+<kbd>Z</kbd> goes back to undoing image operations.

## Things that are not undoable

- The view: zoom, pan, showing or hiding the grid or the rulers, and which layer is selected are not history steps.
- The palette: changing the primary or secondary color, or editing a palette file, is not part of the image's history.
- Preferences, keyboard shortcuts, and which pads are shown.
- Saving and exporting: writing a file marks the history's clean point but adds no step.

Clipboard contents are not history either, though the paste that put them on the canvas is.