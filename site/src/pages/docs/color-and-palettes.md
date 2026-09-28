---
layout: ../../layouts/DocsLayout.astro
---

Impasto paints with two colors at once: a **primary** color and a **secondary** color. They start as black and white. Wherever a tool distinguishes the two, left click uses the primary and right click uses the secondary - painting, erasing toward a background, the two ends of a gradient, the fill and outline of a shape, and so on.

## Choosing colors

There are four ways to set the primary or secondary color:

- The **palette** in the status bar, which holds your swatches plus quick and recent colors.
- The **color wheel** button next to those swatches, which opens a popover with a live picker on top of whichever palette sections have folded away.
- The **floating colors window**, a full picker you can leave open while you work.
- The **Color Picker** tool (<kbd>K</kbd>), which reads a color out of the image. See [Paint tools](/docs/tools/paint/#color-picker).

<kbd>X</kbd> swaps the primary and secondary colors; the two small arrows next to the swatches do the same. The reset button beside them restores black and white.

Clicking either swatch in the status bar opens the floating window with that swatch selected, so the fast path from "I like this color" to "let me adjust it" is one click.

## The status bar palette

From left to right the palette holds the current primary and secondary swatches, the swap and reset buttons, quick colors, recent colors, and the swatch grid. Recent colors record what you have actually painted with, and how many are kept is set under **Edit > Settings... > UI > Recently picked colors** (0 disables them).

Turning on **Show a third row of darker colors in the palette** adds a darker shade beneath each standard swatch, so every color has a ready-made shadow. The palette grid's own size is set with **Palette size** on the same settings page.

When the window is narrow, the palette gives up its space in a fixed order: the swatch tiles shrink first, then the quick colors fold away, then the recent colors, and finally the swatches themselves fold in the popover the color wheel button opens. Nothing is lost when it folds - the folded sections are all inside that popover, rebuilt each time it opens. The palette always keeps its swap and reset buttons.

## The floating colors window

**View > Show/Hide > Float Colors** opens a detached picker window, and the color wheel button in the status bar opens the same window when you want to edit the color you just clicked. Unlike a dialog it has no OK or Cancel: every change it makes is written to the palette immediately, so the canvas updates as you drag.

It contains:

- A color surface, available as **Hue & Sat** or **Sat & Value**. Two options change how it is drawn: **Show selection brightness in preview** draws the surface at your current color's brightness rather than at full brightness, and **Show selection opacity in preview** shows it at the current opacity, letting the checkerboard behind it through. Both matter when you are choosing a dark or a translucent color, where a full-brightness, fully opaque surface would mislead you.
- Sliders for **Hue**, **Sat**, **Value**, **Red**, **Green**, **Blue**, and **Alpha**.
- A **Code** field that reads and writes CSS color syntax: `#ff5733`, `rgb(255 87 51)`, `rgb(255 87 51 / 50%)`, `hsl(11 100% 60%)`, `hwb(11 20% 0%)`, `oklch(65% 0.2 35)`, and CSS color names such as `rebeccapurple`, `transparent`, or `currentColor`. Commas are optional, and typing the name of a format with empty parentheses - `oklch()` - rewrites the current color into it.
- The primary/secondary swatch display, which switches between them when clicked.
- Recent and quick swatch rows.
- An eyedropper button, which switches to the Color Picker tool and samples from the image.

The window can be moved anywhere, including onto another monitor. Right-click its float button for **Reset window**, which brings it back to its default size and position on the application window.

## Palette files

**Edit > Palette** holds the palette operations:

| Command | Effect |
|---|---|
| Open... | Replaces the current palette with one loaded from a file. |
| Save As... | Writes the current palette to a file. |
| Reset to Default | Restores the palette Impasto ships with. |
| Set Number of Colors | Changes how many colors the palette holds. |

Three palette formats are supported, identified by their file extension:

| Format | Extension |
|---|---|
| Paint.NET palette | `.txt` |
| GIMP palette | `.gpl` |
| PaintShop Pro palette | `.pal` |

**Set Number of Colors** takes effect immediately: enlarging the palette appends empty swatches you can fill in, and shrinking it drops the ones at the end.

## How the two colors behave in tools

The primary and secondary colors are read and written by the tools in ways worth knowing:

| Tool | Primary | Secondary |
|---|---|---|
| Paintbrush, Pencil, Clone Stamp | Left click paints with it. | Right click paints with it. |
| Eraser | Left click erases to transparent. | Right click erases to it. |
| Paint Bucket | Left click fills with it. | Right click fills with it. |
| Gradient | The start of the transition, in Color Mode. | The end of the transition. |
| Color Picker | Left click sets it. | Right click sets it. |
| Text | Fills the text. | Outlines it, or fills its background. |
| Shapes | Outlines the shape. | Fills the interior, in **Fill Shape** and **Fill and Outline Shape** modes. |
| Recolor | The color painted in: left click replaces the secondary color on the canvas with it. | The color replaced by a normal left-drag stroke; the color painted in by <kbd>Alt</kbd>+drag or right drag. |
| Fill Selection | Fills the selection. | |

Both colors are global rather than per document, so they stay put as you switch between tabs.