---
layout: ../../layouts/DocsLayout.astro
---

The toolbox holds every tool Impasto ships with, divided into sections by priority. This page is the index: what each tool does, the key that selects it, and where its options appear. The four pages that follow cover the groups in detail.

## Every tool

| Tool | Key | What it does | Options |
|---|---|---|---|
| Move Selected Pixels | <kbd>M</kbd> | Drags the pixels of the current layer, or of the selected object. | — |
| Move Selection | <kbd>M</kbd> | Moves the selection outline without moving the pixels inside it. | — |
| Zoom | <kbd>Z</kbd> | Click or drag a rectangle to zoom in; right click to zoom out. | — |
| Pan | <kbd>H</kbd> | Drags the view around. | — |
| Rectangle Select | <kbd>S</kbd> | Selects a rectangular area. | Selection mode |
| Ellipse Select | <kbd>S</kbd> | Selects an elliptical area; hold <kbd>Shift</kbd> for a circle. | Selection mode |
| Lasso Select | <kbd>S</kbd> | Freehand or polygon selection outline. | Selection mode, lasso mode |
| Magic Wand Select | <kbd>S</kbd> | Selects a region of similar color. Hold <kbd>Shift</kbd> to flood globally. | Selection mode, tolerance, fill mode |
| Paintbrush | <kbd>B</kbd> | Freehand painting with the primary or secondary color. | Brush width, brush type, antialiasing |
| Pencil | <kbd>P</kbd> | Draws freehand one-pixel-wide lines. | Alpha blending |
| Eraser | <kbd>E</kbd> | Erases to transparent, or to the secondary color. | Brush width, eraser type, antialiasing |
| Paint Bucket | <kbd>F</kbd> | Flood-fills an area with color. | Tolerance, fill mode |
| Gradient | <kbd>G</kbd> | Draws a gradient from the primary to the secondary color. | Gradient type, color mode, alpha blending |
| Color Picker | <kbd>K</kbd> | Picks a color from the image into the primary or secondary slot. | Sample size, sampling source, what to do afterwards |
| Text | <kbd>T</kbd> | Places editable text. | Font, size, style, alignment, fill, outline, antialiasing, mode |
| Line/Curve | <kbd>O</kbd> | Draws editable lines and curves. | Type (Line/Curve), outline width, arrowheads, curved segments, antialiasing, mode |
| Rectangle | <kbd>O</kbd> | Draws an editable rectangle. | Fill style, outline width, antialiasing, mode |
| Rounded Rectangle | <kbd>O</kbd> | Draws an editable rectangle with rounded corners. | Fill style, outline width, corner radius, antialiasing, mode |
| Ellipse | <kbd>O</kbd> | Draws an editable ellipse. | Fill style, outline width, antialiasing, mode |
| Triangle | <kbd>O</kbd> | Draws an editable triangle. | Fill style, outline width, antialiasing, mode |
| Freeform Shape | <kbd>O</kbd> | Draws an editable closed shape with a freehand outline. | Fill style, outline width, antialiasing, mode |
| Clone Stamp | <kbd>L</kbd> | Paints a copy of one part of the image somewhere else. | Brush width, antialiasing |
| Recolor | <kbd>R</kbd> | Replaces one color with another, within a tolerance. | Tolerance, brush width, antialiasing |

Not every tool has options of its own: the Move tools, Zoom, and Pan have none. **Antialiasing On/Off** appears on the brush tools, the shape tools, and the Text tool; **Alpha Blending** appears only on the Pencil and the Gradient, where **Normal Blending** composites with what is already there and **Overwrite** replaces it, including the alpha. Either control, when a tool has it, sits at the right-hand end of the tool options bar.

## How tools are grouped

The toolbox divides into sections by separator, and the same grouping drives the tool dropdown you can use instead of the toolbox:

| Section | Tools |
|---|---|
| Move | Move Selected Pixels, Move Selection |
| View | Zoom, Pan |
| Select | Rectangle, Ellipse, Lasso, Magic Wand |
| Paint | Paintbrush, Pencil, Eraser, Paint Bucket, Gradient, Color Picker, Text |
| Shapes | Line/Curve, Rectangle, Rounded Rectangle, Ellipse, Triangle, Freeform Shape |
| Retouch | Clone Stamp, Recolor |

Tools you pin collect in a highlighted strip at the top of the toolbox, but pinning copies a tool rather than moving it: the pinned copy sits above, and the tool keeps its original place in its section as well. Sections that hold nothing are omitted, so the Add-ins section only appears once you install an add-in that adds a tool.

## Stacks, pinning, and the dropdown

Some tools share a key and a slot in the toolbox: **Rectangle**, **Rounded Rectangle**, **Ellipse**, **Triangle**, and **Freeform Shape** occupy one button with a small corner marker. Press and hold the button, or click the marker, to open the flyout and pick another tool from that group. **Line/Curve** is separate, and the four Select tools each have a button of their own even though <kbd>S</kbd> cycles through them. Drag a tool from the flyout onto the toolbox to pin it, so it gets a button of its own; drag a pinned tool back to unpin it. Buttons can be reordered by dragging.

If you would rather not have the column at all, turn on **Edit > Settings... > UI > Pick tools from a dropdown instead of the tool box**. The tool name shown above the canvas becomes a dropdown listing every tool, and the toolbox is hidden. You can still show the toolbox again afterwards from **View > Show/Hide > Tool Box**; the setting only switches the view at the moment you change it.

## Options are remembered

Each tool's settings are stored when you change them, so the brush width, tolerance, and font you set last time are still there when you come back - including after restarting Impasto. To restore everything, use **Edit > Settings... > Backup > Export Settings...** to keep a copy first, then delete the settings folder listed under [The interface](/docs/interface/#where-the-settings-live).

## Tool-specific keys

Tools respond to keys on the canvas beyond the one that selects them: <kbd>[</kbd> and <kbd>]</kbd> change brush width, arrow keys nudge a selection or a shape control point, <kbd>Enter</kbd> finishes a shape or lasso outline, and <kbd>Backspace</kbd> removes the last lasso point. The full list is in [Keyboard shortcuts](/docs/keyboard-shortcuts/#tool-specific-keys).