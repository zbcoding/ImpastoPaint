---
layout: ../../layouts/DocsLayout.astro
---

Snapping pulls what you draw and what you move onto exact positions, so shapes line up with the grid, with the ruler, and with the middle of the canvas without you having to aim.

**View > Snap to grid/ruler/center** (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>G</kbd>) turns it on and off. When it is on, a checkmark shows next to the menu item.

## What it snaps to

Snapping picks its target from whatever is visible on the canvas, in this order:

| Visible | What input snaps to |
|---|---|
| The axonometric grid | The lattice lines. |
| The canvas grid | The grid's cell size - one step per cell, in both axes. |
| The rulers | The tick spacing the rulers drew at the current zoom, in the unit they are set to. |
| Nothing, but the canvas is open | The image edges and its two centre lines. |

The last case is a fallback rather than a quantising step: the canvas edges and centre lines are only three lines per axis, so snapping is a magnet rather than a ruler. A point within 8 screen pixels of one of those lines is pulled onto it, and the guide it landed on is drawn while the drag lasts, so you can see which line is holding it. The tolerance is measured in screen pixels, so it stays equally easy to hit at any zoom.

With no grid, no rulers, and no canvas guide within reach, nothing happens: a point is left exactly where you put it rather than being forced onto an invisible grid. If snapping is on but the grid and the rulers are both hidden, the canvas guides are what you are snapping to - which is why turning the rulers on changes snapping's behaviour even though the rulers themselves only draw decorations.

## Ruler units decide the step

When the rulers are the snapping target, **View > Ruler Units** picks the step as well as the labels:

| Unit | Snap step |
|---|---|
| Pixels | 1 pixel |
| Inches | 72 pixels, or the ruler's tick spacing at the current zoom if that is what it drew |
| Centimeters | 28.35 pixels, or the tick spacing |

The rulers choose their tick spacing from the zoom level and from how wide their labels are, so at low zoom a "one inch" step may actually be a coarser round number of pixels. Impasto snaps to the ticks you can see, not to a fixed conversion, which keeps the grid of ticks and the grid of snapping targets the same thing.

## Whole objects, not just the pointer

A dragged shape, a moved text object, and a moved selection snap by their whole bounding box rather than by the corner under the pointer. Each edge of the box and its centre line is offered to the snapping target, and the nearest match within tolerance wins, so a box being dragged onto the canvas centre falls into place by its own centre and a box being dragged toward an edge aligns by that edge.

## Aligning to other objects

**View > Align to objects**, just below the snap toggle, lines a moved object up with the other objects in the image. It is on by default and works whether or not snapping is on.

While you drag a shape, a text object, or a moved selection or pasted image, its left edge, centre line and right edge - and its top, middle and bottom - are pulled onto the same lines of every other visible shape, text object, and image within 8 screen pixels. A solid pink guide shows the line it is held on, drawn from the moved object to the object it lines up with. This is how to centre a caption under an image, or line up the tops of a row of text boxes, by eye.

Text lines up by its letters as well as its box. The box around a text object is padded for its grips, so text also offers the left, centre and right of its glyphs, their top, middle and bottom, and its first line's baseline. Dragging one text near another's baseline puts both on the same line of type, whatever their sizes. Rotated text lines up by its box only.

An "image" here is the painted part of any visible layer: an image pasted onto a layer of its own counts by the box around its pixels. A layer painted edge to edge only lines up with the canvas edges, which belong to the snap toggle instead. Hidden layers and hidden objects are ignored.

When a canvas guide and another object are both within reach, the nearer one wins. With the grid or rulers showing, an object line within reach takes precedence over the grid step on that axis.

## The canvas grid

**View > Show Grid** draws the grid; **View > Edit Canvas Grid** opens a dialog with:

- **Show Grid** - the same toggle as the menu item.
- **Width** and **Height** - the cell size in pixels. These are also the snap step while the grid is visible.
- **Color** - any color and opacity, chosen with Impasto's own color picker, so a bright grid can be made subtle or a dark one made loud.
- **Show Axonometric Grid** - a lattice at a given **Width** and **Angle**, for isometric and other angled work. While it is showing, it takes over both drawing and snapping, and points snap to its lattice lines instead of to rectangular cells.

The grid's settings are saved with your other preferences and are shared by every image, not stored per document. The **Canvas** pad in the right-hand dock holds the same grid and ruler settings, so they are reachable without the menu.

## Other snaps

Snapping is separate from a few tool-specific alignment behaviours that work even when it is off:

- Holding <kbd>Shift</kbd> while dragging a shape's control point snaps the adjacent segment to a 15° angle.
- Holding <kbd>Shift</kbd> while drawing an ellipse constrains it to a circle, and the same key constrains other shape tools as documented on the [shape tools page](/docs/tools/shapes/).
- The **Rotate / Zoom Layer...** dialog takes exact numbers rather than relying on snapping for alignment.

## Where snapping shows up

| Where | What snaps |
|---|---|
| Shape tools | Control points while drawing and while editing an existing shape. |
| Text tool | The object's box while you move it, and its corner grips while you resize it. |
| Move Selected Pixels / Move Selection | The moved bounding box. |
| Selection tools | The selection's corners and edges while you drag them. |

The painting tools - paintbrush, pencil, eraser, bucket, gradient, clone stamp, recolor - are not snapping tools: their input follows the pointer exactly, because a brush stroke pulled onto a grid line would not be what you painted.

Because snapping follows the grid and the rulers rather than replacing them, the way to change how tightly things snap is to change the grid's cell size or the ruler's unit, not to look for a separate tolerance setting.