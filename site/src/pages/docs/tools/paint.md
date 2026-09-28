---
layout: ../../../layouts/DocsLayout.astro
---

The Paint section of the toolbox holds the freehand tools, the fill tools, and the color picker. Except where noted, left click paints with the primary color and right click with the secondary color.

## Paintbrush

Freehand painting. Its options are:

- **Brush width** — the size of the stroke. <kbd>[</kbd> and <kbd>]</kbd> decrease and increase it while the canvas has focus.
- **Brush type** — six brushes that behave differently:
  - **Normal** — a plain round brush of the set width.
  - **Circles** — a chain of overlapping circles.
  - **Grid** — a square grid pattern.
  - **Splatter** — a scattered spray of dots with a random size range.
  - **Slash** — short diagonal strokes at a fixed angle.
  - **Squares** — a chain of squares.
- **Antialiasing** — on by default; turn it off for a hard, aliased edge.

The Paintbrush paints with normal blending and has no Alpha Blending control; the Pencil and the Gradient are the two tools that do.

A brush stroke is painted onto a scratch surface and only merged into the layer when you release the button. A stroke stays one undo step, however long it is.

![A shape control point held on the canvas centre line, with the guide drawn while dragging](/assets/screenshot-snap-to-grid.png)

## Pencil

Draws freehand one-pixel-wide lines in the primary color, with the secondary color on right click. It has no width setting - for a hard-edged line of a given size, use the Paintbrush with antialiasing off. The pencil does have the **Alpha Blending** dropdown, which decides whether it paints over the alpha of what is underneath.

## Eraser

Erases with a round brush of the set width:

- **Normal** — a hard-edged eraser. Left click erases to transparent; right click erases to the secondary color.
- **Smooth** — a soft-edged eraser that fades the area it passes over toward the secondary color through a lookup-table falloff, so adjacent pixels are pulled toward the background gradually instead of being cut out. Left click erases toward transparent, right click toward the secondary color.

The **Type** dropdown switches between them, and **Brush width** sets the size, with <kbd>[</kbd> and <kbd>]</kbd> as shortcuts. A right-click erase is a paint operation, not a transparency one, so it is the way to blend an edge back into a background color.

## Paint Bucket

Fills an area with color: left click fills with the primary color, right click with the secondary color.

- **Tolerance** — how far a pixel's color may differ from the one you clicked and still be filled. 0 fills only pixels exactly matching the clicked color; higher values spread further.
- **Fill Mode** — **Contiguous** fills only the region connected to the click point; **Global** fills every matching pixel in the image, even disconnected ones. Hold <kbd>Shift</kbd> while clicking for a one-off global fill.

The Paint Bucket has no Alpha Blending or Antialiasing control: it writes whole pixels.

The fill is confined to the current selection if there is one, and it writes into the layer's pixels. Like the other painting tools it does not reach into shapes or text objects that are still in object mode; those sit in their own surfaces, above the pixels, and the bucket fills what is underneath them.

## Gradient

Drag from one point to another to draw a gradient from the primary to the secondary color. Right click drags in reverse. Gradient type options:

| Type | Transition |
|---|---|
| Linear | A straight line between the two endpoints. |
| Linear Reflected | Mirrored on both sides of the start point. |
| Linear Diamond | Outward from the start point in a diamond shape. |
| Radial | Outward from the start point in a circle. |
| Conical | A sweep around the start point. |

**Color Mode** decides what the gradient goes between:

- **Color Mode** — from the primary color to the secondary color.
- **Transparency Mode** — from the primary color to transparent, which is how to fade an image out without painting over it.

**Alpha Blending** sits at the end of the options bar, with the same **Normal Blending** / **Overwrite** choice the Pencil has.

A gradient stays live after you draw it: its control points remain on the canvas, and dragging them adjusts the gradient on the layer underneath. Click a control point and drag to move it, and press <kbd>Enter</kbd> (or pick a different tool) to finalize. Each adjustment is its own history step, so you can undo a reposition without losing the gradient.

## Color Picker

Reads a color out of the image. Left click sets the primary color, right click the secondary.

- **Sample size** — **Single Pixel**, or a **3 × 3**, **5 × 5**, **7 × 7**, or **9 × 9** region whose colors are averaged. The larger sizes are for picking a representative color out of a dithered or noisy area.
- **Sampling** — **Layer** reads only the layer you are editing, ignoring what is above or below it; **Image** reads the composited image as you see it on the canvas.
- **After select** — **Do not switch tool** keeps the Color Picker active so you can keep sampling (the default), **Switch to previous tool** returns to whatever you were using before, and **Switch to Pencil tool** hands you the pencil ready to draw with the color you just picked.

## Clone Stamp

Copies part of the image onto another part. Hold <kbd>Ctrl</kbd> and left click to set the origin point - the source - then left click and drag elsewhere to paint a copy of it. The first stroke after setting the origin starts at the origin, and the offset between the origin and where you started painting is kept for the rest of the stroke, so a dragged stroke reproduces the source area rather than repeating one spot.

The origin is marked on the canvas with a handle you can move, so you can adjust the source without resetting it. The tool has **Brush width** and **Antialiasing**.

## Recolor

Replaces one color with another. Left click and drag replaces the secondary color on the canvas with the primary; <kbd>Alt</kbd>+left drag or right drag does the reverse, replacing the primary with the secondary. **Tolerance** decides how close a pixel must be to the color being replaced for it to change, and **Brush width** sets the size of the area affected per pass.

Unlike the Paint Bucket, Recolor only affects pixels the brush touches, so it is the tool for changing a stray color in a photograph without filling everything connected to it.