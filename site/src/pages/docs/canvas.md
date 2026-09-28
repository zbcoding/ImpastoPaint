---
layout: ../../layouts/DocsLayout.astro
---

Canvas covers everything about the picture's shape and how you look at it: the size of the image, the size of the frame around it, cropping and rotation, and the zoom and pan controls.

## The image and the canvas

Two different sizes are easy to confuse:

- **Image size** is the number of pixels in the picture. **Image > Resize Image...** (<kbd>Ctrl</kbd>+<kbd>R</kbd>) resamples the pixels, so a smaller image really holds less detail.
- **Canvas size** is the frame the layers sit in. **Image > Resize Canvas...** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd>) makes the frame bigger or smaller without resampling anything: growing it adds transparent space, shrinking it cuts off whatever hangs outside.

## Resize Image

**Resize Image...** offers:

- **By percentage** or **By absolute size** - type a percentage of the current size, or a width and height in pixels.
- **Maintain aspect ratio** - changing the width changes the height to match.
- **Resampling** - **Nearest Neighbor** keeps hard pixel edges, which is what you want for pixel art, icons, and screenshots of interfaces; **Bilinear** smooths, which is what you want for photographs.

There is a reset button next to the size fields that puts them back to the image's current size.

## Resize Canvas

**Resize Canvas...** takes a width, a height, the same percentage/absolute choice, and an **Anchor**: a nine-way grid of positions that decides where the existing picture sits inside the new frame. Anchoring to the middle grows the canvas evenly on all sides; anchoring to the top left adds all the new space to the right and bottom.

Growing the canvas shifts the picture and keeps its objects editable: shapes and text move with it, and only the layer's live effect and transform nodes are baked, because those render at a fixed canvas size. Shrinking the canvas can cut content away, which a coordinate shift cannot express, so everything is baked to pixels first.

## Cropping

| Command | Shortcut | Effect |
|---|---|---|
| Crop to Selection | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd> | Crops the image to the current selection. |
| Auto Crop | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>X</kbd> | Crops away the uniform border around the artwork. |

**Auto Crop** looks at the flattened image and trims everything that is fully transparent, which is the usual way to remove the extra space around a pasted or rotated graphic. It ignores the current selection rather than cropping to it.

Cropping bakes objects into pixels, so a shape that was editable before a crop is not afterwards. The bake and the crop are bundled into one history entry, so a single undo brings back both the crop and the objects.

## Flipping and rotating

| Command | Shortcut |
|---|---|
| Rotate 90° Clockwise | <kbd>Ctrl</kbd>+<kbd>H</kbd> |
| Rotate 90° Counter-Clockwise | <kbd>Ctrl</kbd>+<kbd>G</kbd> |
| Rotate 180° | <kbd>Ctrl</kbd>+<kbd>J</kbd> |
| Flip Horizontal | |
| Flip Vertical | |

The 90° rotations turn the whole canvas, so a landscape image becomes portrait, and its dimensions swap with it. The flips mirror the image in place without changing its size.

**Layers > Rotate / Zoom Layer...** does something different: it adds a live **Transform** node to the current layer, drawn as its own row in the Layers pad, and the dialog it opens carries angle, horizontal and vertical translation, horizontal and vertical scale, shear, flips, and perspective corners. The layer re-renders with those values, and because the transform is a node rather than a resample, reopening it from the layer row's **Transform Settings...** changes them again without any pixels having been lost.

## Offsetting a selection

**Edit > Offset Selection...** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd>) moves the selection by a number of pixels in each direction, wrapping around the canvas edges: content pushed off the right edge appears on the left. It is the tool for making a seamless tile, where a pattern's edges have to line up with each other.

## Zooming

The zoom control sits at the right-hand end of the toolbar, offering fixed steps from 5% to 3600% plus **Window**, which fits the whole image in the view. You can also type a value into it.

| Command | Shortcut |
|---|---|
| Zoom In | <kbd>Ctrl</kbd>+<kbd>+</kbd> or <kbd>+</kbd> (also <kbd>=</kbd>, and the numeric keypad) |
| Zoom Out | <kbd>Ctrl</kbd>+<kbd>-</kbd> or <kbd>-</kbd> |
| Best Fit | <kbd>Ctrl</kbd>+<kbd>B</kbd> |
| Normal Size | <kbd>Ctrl</kbd>+<kbd>0</kbd> |
| Zoom to Selection | |
| Fullscreen | <kbd>F11</kbd> |

The **Zoom** tool (<kbd>Z</kbd>) zooms where you point: left click zooms in, right click zooms out, and dragging a rectangle zooms in on that rectangle. The middle mouse button pans while the Zoom tool is active.

Menu, toolbar, and keyboard zoom keep the picture under the pointer rather than jumping to the top-left corner, and they have a small magnet at the edges: with an image edge on screen, zooming while the pointer is within 15% of the visible image from that edge holds the edge still, so zooming toward a corner keeps the whole corner in view. From 15% out to 30% the zoom point blends smoothly back to the pointer, and beyond that the zoom is purely at the pointer - which is why zooming over the middle of the image stays centered instead of drifting toward an edge.

## Panning

- **Pan** tool (<kbd>H</kbd>): click and drag.
- **Middle mouse button**: pans with any tool, including while you are painting with another one.

## Rulers, units, and the grid

**View > Show/Hide > Rulers** shows rulers along the top and left edges of the canvas. **View > Ruler Units** sets what they measure: **Pixels**, **Inches**, or **Centimeters**. The units apply to the ruler labels and to the snapping targets they offer.

**View > Show Grid** draws the canvas grid, and **View > Edit Canvas Grid** opens its settings: whether the grid shows, its cell width and height in pixels, its color, and an optional **axonometric grid** - a lattice at a set width and angle, for isometric work. Both are covered in [Snapping and guides](/docs/snapping/).

The status bar reports the cursor position and the canvas size and aspect ratio; both chips can be turned off in **Edit > Settings... > UI**, and double-clicking the canvas size chip opens **Resize Image**.

## Multiple images

Every image opens in its own tab. Switch with <kbd>Ctrl</kbd>+<kbd>Tab</kbd> or <kbd>Alt</kbd>+<kbd>1</kbd> to <kbd>Alt</kbd>+<kbd>9</kbd>; **Window** has Save All (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>A</kbd>) and Close All (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd>). Each tab keeps its own zoom, scroll position, selection, and history.