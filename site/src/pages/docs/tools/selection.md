---
layout: ../../../layouts/DocsLayout.astro
---

A selection is a mask over the current image. Once one exists, every paint tool, adjustment, effect, and clipboard command works only inside it, and a selection outline is drawn as a moving dashed line around the marked area.

The Select section of the toolbox holds four tools that create selections. All four share the same selection-mode dropdown.

| Tool | Key | Gesture |
|---|---|---|
| Rectangle Select | <kbd>S</kbd> | Click and drag out a rectangle. |
| Ellipse Select | <kbd>S</kbd> | Click and drag out an ellipse; hold <kbd>Shift</kbd> to constrain it to a circle. |
| Lasso Select | <kbd>S</kbd> | Draw the outline by hand, or click point by point. |
| Magic Wand Select | <kbd>S</kbd> | Click a pixel to select the region of similar color around it. |

## Combine modes

The **Selection Mode** dropdown decides how a new selection is merged with the one already there:

| Mode | Effect |
|---|---|
| Replace | The new area replaces the old selection. |
| Union (+) | Adds the new area to the selection. |
| Exclude (-) | Removes the new area from the selection. |
| Xor | Keeps only the parts of the two areas that do not overlap. |
| Intersect | Keeps only the overlapping part. |

The dropdown is only the default: the mouse buttons and modifiers override it while you drag, which is much faster than changing the dropdown back and forth.

| Gesture | Mode |
|---|---|
| Left drag | The mode selected in the dropdown |
| <kbd>Ctrl</kbd> + left drag | Union |
| <kbd>Alt</kbd> + left drag | Intersect |
| Right drag | Exclude |
| <kbd>Ctrl</kbd> + right drag | Xor |

On macOS the modifiers are <kbd>⌘ Command</kbd> and <kbd>Option</kbd> respectively. Note that right-drag paints with the secondary color when the tool is not a selection tool.

## The Lasso

The Lasso has a **Lasso Mode** dropdown with two behaviours:

- **Freeform** — click and drag to draw the outline by hand, and release to close it.
- **Polygon** — click to place a point, click again for the next one, and press <kbd>Enter</kbd> to close the shape. <kbd>Backspace</kbd> removes the last point.

In either mode the tool options bar shows a confirm button and a back button while an outline is in progress, so you can finish or discard it without touching the keyboard. <kbd>Esc</kbd> cancels the outline in progress.

## The Magic Wand

The Magic Wand selects a contiguous region of similar color. Its options are:

- **Tolerance** — how far a pixel's color may differ from the one you clicked and still be included. Higher includes more colors.
- **Fill Mode** — **Contiguous** (the default) floods only the connected region touching the click, while **Global** floods every matching pixel in the image, however disconnected.
- **Selection Mode** — the combine modes above.

Hold <kbd>Shift</kbd> while clicking to flood globally without changing the dropdown.

Tolerance is live. With a selection already made by the wand, dragging the tolerance slider re-floods the points you clicked, so the selected area grows and shrinks under the pointer instead of you having to click again for each guess. The whole run of the slider is a single undo step, and if something else has taken over the selection in the meantime - Select All, another tool, an undo - moving the slider leaves that alone.

## Moving a selection

There are two ways to move what you have selected, and they are different tools:

- **Move Selected Pixels** (<kbd>M</kbd>) moves the pixels inside the selection, leaving the selection outline behind at its original place.
- **Move Selection** (<kbd>M</kbd>) moves only the outline; the pixels stay where they are.

Both live in the Move section, so pressing <kbd>M</kbd> again cycles between them. Arrow keys nudge, <kbd>Shift</kbd>+arrow keys nudge in larger steps.

While a selection is visible, its corner and edge grips are drawn, and dragging a grip resizes the selection. Grips that belong to a hidden selection are not grabbable, so a press near the canvas origin starts a new selection rather than grabbing an invisible full-canvas rectangle.

## Acting on a selection

| Command | Shortcut | Effect |
|---|---|---|
| Select All | <kbd>Ctrl</kbd>+<kbd>A</kbd> | Selects the whole canvas. |
| Deselect All | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>A</kbd> or <kbd>Ctrl</kbd>+<kbd>D</kbd> | Clears the selection. |
| Deselect All (Quick) | <kbd>Esc</kbd> | The same, on one key press. |
| Invert Selection | <kbd>Ctrl</kbd>+<kbd>I</kbd> | Selects everything that is not selected. |
| Erase Selection | <kbd>Delete</kbd> | Clears the selected pixels of the current layer to transparent. |
| Fill Selection | <kbd>Backspace</kbd> | Fills the selection with the primary color. |
| Offset Selection | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd> | Moves the selection by a number of pixels in each direction, wrapping it around the canvas. |
| Crop to Selection | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd> | Crops the image so the selection fills it. |

<kbd>Esc</kbd> finishes any text or shape you are editing before it deselects, and with the pointer over the canvas the Text and Lasso tools use the first press to finish typing or cancel an outline. Because it is a single press, it is easy to clear a selection by accident: <kbd>Ctrl</kbd>+<kbd>Z</kbd> restores it, because the selection itself is a history step.

Selections are per document. Each image remembers its own, and switching tabs does not lose either. The status bar shows a chip while a selection exists, reporting its position and size.

## Cutting, copying, and pasting

| Command | Shortcut | Effect |
|---|---|---|
| Cut | <kbd>Ctrl</kbd>+<kbd>X</kbd> | Copies the selected pixels and clears them from the layer. |
| Copy | <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copies the selected pixels of the current layer. |
| Copy Merged | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd> | Copies what is visible, with all layers composited. |
| Paste | <kbd>Ctrl</kbd>+<kbd>V</kbd> | Pastes onto the current layer. |
| Paste Alternate | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> | Pastes onto a new layer. |
| Paste Into New Image | <kbd>Shift</kbd>+<kbd>V</kbd> or <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>V</kbd> | Pastes into a new image, sized to fit. |

Pasting leaves the new content under a selection of its own, so it can be moved straight away. Turning on **Edit > Settings... > Keyboard > Paste external images onto a new layer by default** swaps the first two: Paste then goes to a new layer and Paste Alternate to the current one.

Cut, Copy, Erase Selection, and Fill Selection act on the layer's mask when the mask row is the selected paint target, matching what the paint tools do - see [Layers and objects](/docs/layers/#masks).