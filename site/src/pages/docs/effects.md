---
layout: ../../layouts/DocsLayout.astro
---

Effects are in the **Effects** menu: in the menu bar, or behind the Effects button in the title bar when Impasto uses a header bar instead of a menu bar. They are grouped into submenus by category. Like [adjustments](/docs/adjustments/), an effect works on the current layer, and only inside the selection if one is active.

## Using an effect dialog

Most effects open a settings dialog when you choose them. The dialog does not block the main window, and the canvas shows a live preview of the result that updates as you change a setting.

- **Sliders** have a number box beside them for typing an exact value.
- **Angle** settings have a dial you can drag, plus a number box.
- **Offset** and **Center Offset** settings show a thumbnail of the image. Click or drag on it to place the effect's center, or type coordinates. Each coordinate has its own reset button.
- **Color** settings open a color chooser.
- Some settings only appear when another setting makes them relevant. For example, **Color Scheme** is shown only when **Color Scheme Source** is set to **Preset Gradient**.

Click **OK** to apply the effect or **Cancel** to discard it. A progress window titled "Rendering Effect" appears while a slow effect finishes; cancelling it also discards the effect. Most effects remember your last settings for the rest of the session, so the dialog opens where you left it.

### Random seeds

Effects with random patterns (noise, clouds, cells and others) have a seed setting. The same seed always gives the same pattern, so you can reproduce a result exactly. Click **Reseed** to try a new random pattern, or type a seed number.

### Applied effects stay editable

An applied effect is added to the layer as an editable node, shown as a row marked **Fx** under the layer in the Layers pad. Right-click the row to open **Effect Settings...**, change its **Blend Mode** or **Strength**, **Rasterize** it into the layer's pixels, or **Delete** it. Drag the row to reorder it on the layer. See [Layers and objects](/docs/layers/).

> Effects have no default keyboard shortcuts, but you can assign one to any effect on the Effects tab of the Keyboard Shortcuts dialog. See [Keyboard shortcuts](/docs/keyboard-shortcuts/).

## Common settings

Several effects share these settings.

| Setting | Options |
|---|---|
| Edge Behavior | What to use for pixels that would come from outside the image: Clamp, Wrap, Reflect, Primary, Secondary, Transparent or Original. |
| Color Scheme Source | Preset Gradient, Selected Colors (your primary and secondary colors) or Random. |
| Color Scheme | Preset gradients: Beautiful Italy, Black and White, Bonfire, Cherry Blossom, Cotton Candy, Electric, Lime Lemon, Martian Lava, Piña Colada. |
| Reverse Color Scheme | Runs the color scheme in the opposite direction. |
| Quality | Higher values smooth the result but take longer to render. |

## Artistic

| Effect | What it does | Settings |
|---|---|---|
| Ink Sketch | Turns the image into an inked drawing with color filled in. | Ink Outline (0–99, default 50), Coloring (0–100, default 50) |
| Oil Painting | Makes the image look painted with a brush. | Brush Size (1–8, default 3), Coarseness (3–255, default 50) |
| Pencil Sketch | Turns the image into a pencil drawing. | Pencil Tip Size (1–20, default 2), Color Range (-20–20, default 0) |

## Blurs

| Effect | What it does | Settings |
|---|---|---|
| Fragment | Overlays several offset copies of the image. | Fragments (2–50, default 4), Distance (0–100, default 8), Rotation |
| Gaussian Blur | Smooth, even blur. | Radius (0–200, default 2) |
| Motion Blur | Blurs along a direction, as if the subject were moving. | Angle (default 25°), Distance (1–200, default 10), Centered |
| Radial Blur | Spins the image around a center point. | Angle (default 2°), Offset, Quality (1–5, default 2) |
| Unfocus | Out-of-focus lens blur. | Radius (1–200, default 4) |
| Zoom Blur | Streaks the image outward from a center point. | Amount (0–100, default 10), Offset |

For Radial Blur, use low quality for previews, small images and small angles, and high quality for the final result, large images and large angles.

## Color

| Effect | What it does | Settings |
|---|---|---|
| Dithering | Reduces the image to a limited palette, scattering the color error so gradients still read as smooth. | Error Diffusion Method (default Floyd-Steinberg), Palette Source, Palette |

**Error Diffusion Method** is one of Sierra, Two-Row Sierra, Sierra Lite, Burkes, Atkinson, Stucki, Jarvis-Judice-Ninke, Floyd-Steinberg or Floyd-Steinberg Lite. **Palette Source** is **Preset Palettes**, **Current Palette** (the palette in the palette pad) or **Recently Used Colors**; **Palette** picks the preset when Preset Palettes is chosen.

## Distort

| Effect | What it does | Settings |
|---|---|---|
| Bulge | Pushes the image outward from a point, or pinches it inward with a negative amount. | Amount (-200–100, default 45), Offset, Radius Percentage (10–100, default 100) |
| Dents | Warps the image with small random ripples. | Scale (1–200, default 25), Refraction (0–200, default 50), Roughness (0–100, default 10), Turbulence (0–100, default 10), Random Noise Seed, Quality (1–5, default 2), Center Offset, Edge Behavior (default Wrap) |
| Frosted Glass | Scatters pixels, like looking through frosted glass. | Amount (1–10, default 1), Random Noise Seed |
| Pixelate | Turns the image into large square cells. | Cell Size (1–100, default 2) |
| Polar Inversion | Turns the image inside out around a center point. | Amount (-4–4, default 0), Quality (1–5, default 2), Center Offset, Edge Behavior (default Reflect) |
| Tile Reflection | Makes the image look seen through a grid of glass tiles. | Rotation (-45–45°, default 30°), Tile Size (2–200, default 40), Intensity (-20–20, default 8), Tile Type (Sharp Edges or Curved), Edge Behavior (default Wrap) |
| Twist | Swirls the image around a center point. | Amount (-100–100, default 30), Radius Percentage (0–100, default 100), Antialias (0–5, default 2), Center Offset, Edge Behavior (default Clamp) |

## Noise

| Effect | What it does | Settings |
|---|---|---|
| Add Noise | Adds random grain. | Intensity (0–100, default 64), Color Saturation (0–400, default 100), Coverage (0–100, default 100), Random Noise Seed |
| Median | Replaces each pixel with a value picked from its neighborhood, which removes specks. At 50 percentile it takes the median. | Radius (1–200, default 10), Percentile (0–100, default 50) |
| Reduce Noise | Smooths out grain while keeping detail. | Radius (1–200, default 6), Strength (0–1, default 0.4) |

## Object

These effects treat what is on the layer as an object on a background, inside the selection or across the whole layer when nothing is selected. They work best on a layer where the object sits on transparency.

| Effect | What it does | Settings |
|---|---|---|
| Align Object | Moves the object to a side, corner or the center of the selection (or of the image). | Position: pick one of nine buttons from Top Left to Bottom Right. |
| Feather Object | Fades the object's edges to transparent. | Radius (1–100, default 6), Tolerance (0–255, default 20), Feather Canvas Edge |
| Outline Object | Draws an outline around the object in your primary color. | Radius (0–100, default 6), Tolerance (0–255, default 20), Alpha Gradient, Color Gradient, Outline Border, Fill Object Background |

With **Color Gradient** on, Outline Object blends the outline from the primary color toward the secondary color.

## Photo

| Effect | What it does | Settings |
|---|---|---|
| Glow | Adds a soft, bright glow. | Radius (1–20, default 6), Brightness (-100–100, default 10), Contrast (-100–100, default 10) |
| Red Eye Removal | Removes red eye from flash photos. | Tolerance (0–100, default 70), Saturation Percentage (0–100, default 90) |
| Sharpen | Makes edges crisper. | Amount (1–20, default 2) |
| Soften Portrait | Softens skin while keeping the image sharp overall. | Softness (0–10, default 5), Lighting (-20–20, default 0), Warmth (0–20, default 10) |
| Vignette | Darkens the image toward its edges around a clear center. | Offset, Radius Percentage (10–400, default 50), Strength (0–1, default 1) |

For Red Eye Removal, select each eye with a selection tool first for the best results.

## Render

Render effects draw new content onto the layer.

| Effect | What it does | Settings |
|---|---|---|
| Cells | Fills the area with a pattern of cells around scattered points. | Distance Metric, Point Arrangement, Random Point Locations, Show Points, Point Size, Point Color, Number of Cells (1–1024, default 100), Cell Radius (4–100, default 32), color scheme settings (default Black and White), Color Scheme Edge Behavior, Quality (1–4, default 3) |
| Clouds | Renders soft cloud-like noise. | Scale (2–1000, default 250), Power (0–100, default 50), Random Noise Seed, color scheme settings (default Selected Colors) |
| Julia Fractal | Renders a Julia set fractal. | Factor (1–10, default 4), Quality (1–5, default 2), Zoom (0–50, default 1), color scheme settings (default Bonfire), Angle |
| Mandelbrot Fractal | Renders a Mandelbrot set fractal. | Factor (1–10, default 1), Quality (1–5, default 2), Zoom (0–100, default 10), Angle, color scheme settings (default Electric), Invert Colors |
| Voronoi Diagram | Fills the area with flat-colored cells around scattered points. | Distance Metric, Number of Cells (1–1024, default 100), Color Sorting, Reverse Color Sorting, Random Colors, Point Arrangement, Random Point Locations, Show Points, Point Size, Point Color, Quality (1–4, default 3) |

**Distance Metric** is Euclidean, Manhattan or Chebyshev. **Point Arrangement** is Random, Circular or Phyllotaxis. Voronoi's **Color Sorting** is Random, or horizontal or vertical sorting led by the blue, green or red channel.

## Stylize

| Effect | What it does | Settings |
|---|---|---|
| Edge Detect | Highlights edges and flattens everything else to gray. | Angle (default 45°) |
| Emboss | Makes the image look pressed into metal. | Angle (default 0°) |
| Outline Edge | Traces the edges in the image with thick outlines. | Thickness (1–200, default 3), Intensity (0–100, default 50) |
| Relief | Gives the image a raised, carved look while keeping its colors. | Angle (default 45°) |

## Effects from add-ins

Effects installed by add-ins appear in an **Add-ins** submenu of the Effects menu, grouped by add-in. They stay editable while the document is open, but they are baked into the pixels when you save an Impasto project, because the add-in may not be installed when the file is opened again. Impasto warns you and lists the affected effects before saving. See [Add-ins](/docs/addins/).
