---
layout: ../../layouts/DocsLayout.astro
---

An image is a stack of layers. Each layer has its own pixels, its own blend mode and opacity, and - optionally - a mask and a set of editable objects (shapes, text, and live effect nodes). The **Layers** pad, in the right-hand dock, is where the stack is managed.

## The Layers pad

Rows are listed top-first: the row at the top of the pad is drawn in front. Each layer row has a checkbox that shows or hides the layer, a thumbnail, and the layer's name.

A layer that holds editable objects shows an expander. Expanding it lists the objects as indented sub-rows, also top-first, each with its own visibility checkbox and name.

The pad's header has a menu button with the layer operations - **Flip Horizontal**, **Flip Vertical**, **Rotate / Zoom Layer...**, and **Import from File...** - plus a slider for the thumbnail size. At the smallest setting the thumbnails are hidden altogether.

Right-click a row for the operations that apply to it, which differ by row kind:

| Row | Right-click menu |
|---|---|
| Layer | Show or hide, Solo Layer, Delete, Duplicate, Merge Layer Down, Move Layer Up/Down, the flip and rotate commands, Rasterize All Objects (when the layer holds any), Add Layer Mask (when it does not have one yet), Rename Layer..., Layer Properties... |
| Object | Blend mode, opacity, and Rasterize, Properties..., Delete |
| Mask | Delete Mask |

Drag rows to reorder them. Layer rows move among layers and object rows move among their own layer's objects; dragging an object row onto a *different* layer moves it there. Shapes and text carry their own position, so they land looking exactly as they did and only the stack compositing them changes, while a live effect node grades its own layer's stack and cannot be moved.

**Move Layer Up** and **Move Layer Down** reorder whichever row is selected, so with an object selected they move that object within its layer instead of the layer itself. Every reorder is undoable.

Double-clicking a layer row opens **Layer Properties**, which is also where the layer is renamed.

## Layer commands

| Command | Shortcut | Effect |
|---|---|---|
| Add New Layer | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>N</kbd> | Adds an empty transparent layer above the current one. |
| Duplicate Layer | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>D</kbd> | Copies the current layer, including its mask and its objects. |
| Delete Layer | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Delete</kbd> | Removes the current layer. |
| Merge Layer Down | <kbd>Ctrl</kbd>+<kbd>M</kbd> | Merges the current layer into the one below it. |
| Flatten | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> | Merges every layer into one, compositing what is visible. |
| Move Layer Up / Down | | Reorders the selected layer, or the selected object. |
| Import from File... | | Adds an image file to the stack as a new layer. |
| Layer Properties... | <kbd>F2</kbd> | Name, visibility, opacity, and blend mode. |
| Rotate / Zoom Layer... | | Rotates or resizes just this layer within the canvas. |
| Flip Horizontal / Vertical | | Flips the current layer. |
| Add Layer Mask | | Adds a mask to the current layer; see below. |
| Rasterize All Objects | | Bakes every object in the image into its layer's pixels. |
| Solo Layer 1 to 5 | <kbd>Ctrl</kbd>+<kbd>1</kbd>-<kbd>5</kbd> | Shows only the numbered layer, counting from the bottom. |

**Solo Layer** counts up from the bottom layer, so <kbd>Ctrl</kbd>+<kbd>1</kbd> isolates the bottom-most layer. Pressing the same key again shows everything.

Flattening and merging both bake objects into pixels, because a merged layer has no object list of its own. Impasto warns you before an operation rasterizes objects; **Edit > Settings... > UI > Skip the "Rasterize Objects?" confirmation** turns the warning off.

## Layer properties

**Layer Properties...** (<kbd>F2</kbd>) sets:

- **Name** — what the row in the pad shows.
- **Visible** — the same as the checkbox on the row. A hidden layer still exists and still contributes its objects to the file, but is not drawn.
- **Opacity** — from 0% to 100%, with both a slider and a spin button.
- **Blend mode** — how the layer composites with everything below it.

The blend modes Impasto offers:

| | | | |
|---|---|---|---|
| Normal | Multiply | Color Burn | Color Dodge |
| Overlay | Difference | Lighten | Darken |
| Screen | Xor | Hard Light | Soft Light |
| Color | Luminosity | Hue | Saturation |

**Normal** at 100% opacity is a plain stack: what is on this layer paints over what is below. The rest combine the layer's colors with the layers beneath - **Multiply** and **Darken** for shadows, **Screen** and **Lighten** for highlights, the **Color** family for tinting without changing brightness, and so on.

## Objects in a layer

Shapes, text, and live effect nodes are objects. They live in their own surfaces inside the layer, above its pixels, and are listed under the layer in the pad.

- Clicking a shape's row selects that shape for editing and puts its control points on the canvas.
- Clicking a text object's row activates the Text tool and starts editing it.
- Selecting any object row makes that layer the current one, and sends painting back to the layer's pixels rather than into the object.
- Selecting a mask or layer row drops the editing chrome that was drawn for whatever object was selected before.

An object's right-click menu sets its **Opacity** and **Blend Mode**, reopens the settings of a live effect, and offers **Rasterize**, **Properties...**, and **Delete**. Objects blend against the other objects on their layer using the same blend modes as layers, and each of those settings is a single undo step. The **Properties...** dialog carries the object's name, visibility, opacity, and blend mode.

Everything that needs flat pixels - cutting, erasing, cropping, resizing the image, rotating, flipping, flattening, and applying an effect across the picture - bakes the objects it touches into the layer's pixels permanently. Objects the operation does not touch stay editable, and growing the canvas in **Resize Canvas** is the one resize that leaves shapes and text alone, shifting them along with the picture.

## Masks

**Add Layer Mask** adds a mask slot to the current layer. The mask starts fully transparent, which means it hides the layer completely until you paint something into it: a mask's alpha decides how much of the layer shows through, so an opaque brush stroke reveals the layer and erasing hides it again.

Once a layer has a mask, the mask appears as its own row in the pad, and selecting that row makes it the paint target. From then on the paint tools edit the mask rather than the layer's colors, and the editing commands - Cut, Erase Selection, Fill Selection - follow the same target. Clicking back onto the layer row sends painting back to the pixels.

Right-clicking the mask row offers **Delete Mask**, which removes the mask and leaves the layer rendered unmasked. A mask can be hidden (disabled) from its row's checkbox.


## Saving layers

Only [OpenRaster (.ora)](/docs/file-formats/) keeps the stack as a stack. Every other writable format flattens the image, so layers, masks, objects, and live effect nodes are gone from the file you save.