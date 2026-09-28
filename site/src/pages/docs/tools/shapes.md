---
layout: ../../../layouts/DocsLayout.astro
---

The Shape tools draw figures that stay editable. A rectangle you drew an hour ago is still a rectangle: you can change its fill color, add a corner to it, or move one of its points, and undo any of that on its own.

| Tool | What it draws |
|---|---|
| Rectangle | A rectangle. |
| Rounded Rectangle | A rectangle with rounded corners. |
| Ellipse | An ellipse. |
| Triangle | A triangle. |
| Freeform Shape | A closed shape with an outline you draw by hand. |
| Line/Curve | A line or curve, optionally with arrowheads. |

Rectangle, Rounded Rectangle, Ellipse, Triangle, and Freeform Shape share one button in the toolbox, so <kbd>O</kbd> cycles through them; press and hold the toolbox button, or click its corner marker, to pick one directly. Line/Curve has a button of its own, also on <kbd>O</kbd>.

## Drawing a shape

Choose a shape tool, then click and drag on the canvas to draw it. While you drag, the shape follows the pointer; when you release, its control points appear and it stays selected, ready to adjust. Left drag draws with the primary color, right drag with the secondary.

Hold <kbd>Shift</kbd> while dragging to constrain the shape. With the Triangle tool, <kbd>Shift</kbd> switches between a right triangle and an equilateral one while you draw.

Press <kbd>Enter</kbd> to finalize the shape. Switching to another tool also commits it, as does clicking elsewhere on the canvas to start a new shape at that spot.

## Editing a shape

Click any shape with any shape tool. If the shape belongs to a different tool - you clicked a rectangle while holding the ellipse tool - Impasto switches to the tool that owns it and starts editing it, so you rarely have to think about which tool a shape came from. You can also select a shape by clicking its row in the Layers pad; see [Layers and objects](/docs/layers/).

Once a shape is selected, its control points are drawn. What the mouse does depends on what you grab:

| Gesture | Effect |
|---|---|
| Left drag a control point | Move that point. |
| Left drag the shape itself | Move the whole shape. |
| Right click the outline | Add a control point where you clicked. |
| <kbd>Alt</kbd> + left drag a control point | Rotate the whole shape. |
| <kbd>Ctrl</kbd> + right drag a control point | Change how sharply the line bends through that point (its tension). |
| <kbd>Shift</kbd> + drag | Snap the adjacent segment to a 15° angle. |
| <kbd>Ctrl</kbd> + click a control point | Start a new shape at exactly that spot. |
| <kbd>Ctrl</kbd> held down | Hide the other shapes' control points, so only the shape you are working on is in the way. |

With the pointer over a control point, a tooltip lists these gestures, including your own keys if you have rebound them.

Keyboard equivalents, from the **Tool Specific** tab of the shortcut editor:

| Key | Effect |
|---|---|
| Arrow keys | Move the selected control point one pixel. |
| <kbd>A</kbd> | Add a control point at the pointer, or at the pointer's exact position with <kbd>Ctrl</kbd>+<kbd>Space</kbd>. |
| <kbd>Delete</kbd> | Delete the selected control point. |
| <kbd>S</kbd> / <kbd>D</kbd> | Make the selected point curve / straighten it. |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>←</kbd> / <kbd>→</kbd> | Select the previous / next control point by order. |
| <kbd>Ctrl</kbd>+<kbd>←</kbd> | Create a new shape at the selected control point. |

If pressing arrow keys moves the whole shape rather than one point, no control point is selected; click one first.

## Shape options

The tool options bar carries the settings for the shape you are drawing, and - when a shape is selected - for the shape you are editing:

- **Fill Style** (closed shapes only) — **Outline Shape** draws just the outline in the primary color, **Fill Shape** fills the interior with the secondary color and draws no outline, and **Fill and Outline Shape** does both.
- **Outline width** — the thickness of the outline. <kbd>[</kbd> and <kbd>]</kbd> decrease and increase it.
- **Radius** (Rounded Rectangle) — how far the corners are rounded, in pixels, applied at each point of the shape.
- **Type** (Line/Curve) — **Line** joins each added point to its neighbours with a straight segment; **Curve** bends the line smoothly through each point.
- **Arrow** (Line/Curve) — a toggle for an arrowhead at the start point and another at the end point, with **Size**, **Angle** (how far the wings spread from the line), and **Length** (how long the wings are).
- **Curved Segments** (Line/Curve) — **On** means clicking a segment while editing inserts a control point that curves it; **Off** keeps segments straight.

## Raster or object

The **Mode** dropdown decides what committing the shape does:

| Mode | Result |
|---|---|
| **Raster — fuses to layer** | The shape is painted into the layer's pixels when committed. It can be cut, moved, and erased immediately like any artwork, but never edited again. This is the default. |
| **Object — editable later** | The shape stays a live object inside the layer: re-editable, movable, and each edit is its own history step. |

Objects sit in the layer's own object surfaces, above its pixels, so painting on a layer does not touch them. Anything that needs flat pixels - cutting, erasing, cropping, resizing, rotating, flipping, flattening, or running an effect across one - bakes them down permanently first. Impasto normally warns you before that happens and lets you cancel; **Edit > Settings... > UI > Skip the "Rasterize Objects?" confirmation** makes it bake silently instead.

Objects keep their own history. Selecting a shape and moving a control point is an undo step for that shape alone, so <kbd>Ctrl</kbd>+<kbd>Z</kbd> will not disturb strokes you painted elsewhere on the layer. The History pad labels those steps with what changed - *Point Added*, *Point Deleted*, *Rotated*, *Modified*, *Finalized*.

## Shapes in the layer stack

Every object is listed under its layer in the Layers pad, as an indented sub-row, in the order they were added. Clicking a sub-row selects that object for editing, which is often quicker than hunting for it on the canvas. Objects can be moved between layers, and rasterizing them merges them into the layer's pixels; see [Layers and objects](/docs/layers/).