---
layout: ../../layouts/DocsLayout.astro
---

Adjustments change the colors and tones of the current layer. They live in the **Adjustments** menu: in the menu bar, or behind the Adjustments button in the title bar when Impasto uses a header bar instead of a menu bar. An adjustment affects the current layer only, and if a selection is active it only changes the selected area.

## How adjustments work

Adjustments whose name ends in "..." open a dialog. While the dialog is open the canvas shows a live preview of the result, updated as you change the settings. Click **OK** to apply it or **Cancel** to leave the layer as it was. A progress window titled "Rendering Effect" appears while a slow adjustment finishes, and you can cancel it there.

Adjustments without "..." (Auto Level, Black and White, Invert Colors) apply straight away.

An applied adjustment is not burned into the layer's pixels. It is added to the layer as an editable node, shown as a row marked **Fx** under the layer in the Layers pad. Right-click that row to:

- **Effect Settings...**: reopen the adjustment's dialog and change its values. The layer re-renders with the new values.
- **Blend Mode** and **Strength**: choose how the result is mixed back into the image, and how much of it is applied. At a strength of 0 the layer looks as it did before.
- **Rasterize**: bake the layer's effects and objects into its pixels, after which they stop being editable.
- **Delete**: remove the adjustment.

You can also drag the row to reorder it among the layer's other nodes and objects. See [Layers and objects](/docs/layers/) for more on nodes.

> Saving as an Impasto project (`.ora`) keeps the nodes and their settings, so you can still change them after reopening the file. Other formats save the finished pixels. See [File formats](/docs/file-formats/).

## Keyboard shortcuts

| Adjustment | Default shortcut |
|---|---|
| Auto Level | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd> |
| Black and White | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd> |
| Brightness / Contrast | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>B</kbd> |
| Curves | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>M</kbd> |
| Hue / Saturation | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>U</kbd> |
| Invert Colors | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>I</kbd> |
| Levels | <kbd>Ctrl</kbd>+<kbd>L</kbd> |
| Posterize | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd> |
| Sepia | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>E</kbd> |

On macOS, <kbd>Cmd</kbd> takes the place of <kbd>Ctrl</kbd>. Every shortcut can be changed on the Adjustments tab of the Keyboard Shortcuts dialog; see [Keyboard shortcuts](/docs/keyboard-shortcuts/).

## The adjustments

### Auto Level

Stretches the layer's tonal range automatically, the same correction the **Auto** button in Levels makes. It has no settings.

### Black and White

Removes all color, leaving a grayscale image. It has no settings.

### Brightness / Contrast

| Setting | Range | Default | What it does |
|---|---|---|---|
| Brightness | -100 to 100 | 0 | Lightens or darkens the image. |
| Contrast | -100 to 100 | 0 | Increases or reduces the difference between light and dark areas. |

### Curves

Remaps tones by reshaping a transfer curve.

- **Transfer Map** chooses what the curve controls: **Luminosity** (overall brightness) or **RGB** (the color channels).
- In RGB mode, the **Red**, **Green** and **Blue** checkboxes pick which channels you are editing.
- Click on the curve to add a control point, and drag a point to move it. Right-click a point to remove it.
- **Reset** returns the curve to a straight line.

### Hue / Saturation

| Setting | Range | Default | What it does |
|---|---|---|---|
| Hue | -180 to 180 | 0 | Rotates every color around the color wheel. |
| Saturation | 0 to 200 | 100 | Below 100 mutes colors, above 100 intensifies them. |
| Lightness | -100 to 100 | 0 | Lightens or darkens the image. |

### Invert Colors

Replaces every color with its opposite, like a photographic negative. It has no settings.

### Levels

The **Levels Adjustment** dialog sets the black point, white point and midtones.

- **Input Histogram** and **Output Histogram** show the tone distribution before and after the adjustment.
- **Input** sets the input values that become black and white.
- **Output** sets the darkest and lightest output values and the gamma (midtone) value.
- The **Red**, **Green** and **Blue** checkboxes choose which channels the changes apply to.
- **Auto** picks levels from the image, and **Reset** restores the defaults.

### Posterize

Reduces the number of tones in each color channel, giving flat bands of color.

| Setting | Range | Default |
|---|---|---|
| Red | 2 to 64 | 16 |
| Green | 2 to 64 | 16 |
| Blue | 2 to 64 | 16 |

With **Linked** checked, moving one channel moves all three together.

### Sepia

Converts the image to warm brown tones, like an old photograph.

| Setting | Range | Default | What it does |
|---|---|---|---|
| Strength | 0 to 100 | 100 | How strongly the sepia tone is applied. |

## Adjustments from add-ins

Adjustments installed by add-ins appear in an **Add-ins** submenu of the Adjustments menu. See [Add-ins](/docs/addins/).
