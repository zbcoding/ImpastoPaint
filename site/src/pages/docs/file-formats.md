---
layout: ../../layouts/DocsLayout.astro
---

Impasto opens and saves the common image formats, plus layered OpenRaster files. OpenRaster (`.ora`) is Impasto's project format and the only one that keeps your layers, masks, text, shapes and effects editable. Every other format that Impasto saves stores a flattened image.

## Summary

| Format | Extensions | Open | Save | Layers |
|---|---|---|---|---|
| OpenRaster (Impasto project) | `.ora` | Yes | Yes | Yes, fully editable |
| PDN | `.pdn` | Yes | No | Yes, on open |
| Photoshop | `.psd` | Yes | No | Yes, on open |
| JPEG | `.jpg`, `.jpeg` | Yes | Yes, with a quality setting | No |
| WebP | `.webp` | Yes | Yes, with a quality setting or lossless | No |
| AVIF | `.avif` | Yes | Yes, with a quality setting, when libavif is available | No |
| Netpbm Portable Pixmap | `.ppm` | Yes | Yes | No |
| TGA | `.tga` | If your system can read it | Yes | No |
| PNG, BMP, GIF, TIFF, ICO and others | various | If your system can read them | If your system can write them | No |

Besides the formats Impasto handles itself, it uses the image loaders installed on your system, so the exact list of other formats you can open or save depends on your platform. If you try to open a file Impasto can't read, the error message lists every format it supports on your system.

## Saving

- **File > Save** (<kbd>Ctrl</kbd>+<kbd>S</kbd>) writes back to the file you opened, in the same format.
- **File > Save As...** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>) lets you pick a new name and format. Choose the format from the file type list, or type a name with the extension you want. If the name has no extension, or one Impasto doesn't recognize, the dialog comes back with the selected format's extension filled in. Save As remembers the last format you used.
- **File > Save as Impasto project...** saves as OpenRaster. The dialog offers only the project format and fills in the `.ora` extension for you. It has no default shortcut.

Shortcuts can be changed in the Keyboard Shortcuts dialog; see [Keyboard shortcuts](/docs/keyboard-shortcuts/).

### Saving a layered image to a flat format

If your image has more than one layer and you save it in a format without layer support, Impasto asks "This format does not support layers. Flatten image?" Click **Flatten** to merge all layers into one and save, or cancel to choose another format. To keep your layers, save a copy as an Impasto project as well.

### Files Impasto can open but not save

When you open a PDN or Photoshop file, or any file in a format your system can read but not write, Impasto tells you straight away that it can't save your changes back to that file. Use **Save As** to save them in another format; an Impasto project keeps the layers.

### Quality settings

The first time you save a document as JPEG, WebP or AVIF in a session, Impasto shows the **Image Quality** dialog with a **Quality** slider from 1 to 100. Higher values give better-looking but larger files. Impasto remembers the value you pick and uses it for later saves without asking again.

| Format | Default quality | Notes |
|---|---|---|
| JPEG | 85 | JPEG has no transparency. |
| WebP | 80 | Check **Lossless** to save without any quality loss. |
| AVIF | 80 | |

## OpenRaster (.ora): Impasto projects

OpenRaster is an open, layered format that other painting programs can also read. An Impasto project stores:

- each layer with its name, opacity, visibility and blend mode;
- layer masks, including whether each mask is hidden;
- text and shape objects, which reopen as editable objects;
- applied adjustments and effects, with their settings, strength, blend mode, name and the selection they were applied to, so you can still change them after reopening;
- layer transforms, which reopen editable.

The file also contains a flattened copy of the whole image and a thumbnail.

Effects that come from add-ins can't be saved as editable, because the add-in may not be installed when the file is opened again. Before saving, Impasto warns you and lists them; those effects are saved as part of the layer's pixels instead. They stay editable in the open window.

### Using .ora files with other programs

- Other OpenRaster programs can open Impasto projects. They see the layers and blend modes, but not the editable objects and effects. A program that builds the image from the layers alone shows the raster content without the text and shape objects; the flattened copy still includes them.
- Impasto opens OpenRaster files from other programs. Without Impasto's extra data, every layer is plain pixels.
- If you edit and save an Impasto project in another program, the text and shape objects are lost. Rasterize them first if you need to hand a file to another program.

## PDN files

Impasto opens `.pdn` files with their layers. Each layer keeps its name, visibility, opacity and blend mode. Impasto can't save `.pdn` files; save your work as an Impasto project or another format instead.

## Photoshop (.psd) files

Impasto opens Photoshop documents with their layers on every platform. It can't save them.

Each layer keeps its name, position, opacity, fill opacity, visibility and blend mode, and a layer mask becomes an editable Impasto mask. Groups are flattened into their layers: a hidden group's layers come in hidden, and a group's opacity and mask are applied to each layer inside it. A file saved without layers opens as its merged image.

Limitations:

- Only 8-bit RGB and grayscale documents open. 16-bit and 32-bit documents, CMYK, Lab and indexed color, and large-document `.psb` files are refused with a message.
- Blend modes Impasto doesn't have, such as Linear Burn, Vivid Light and Pin Light, import as Normal.
- Clipping masks, vector masks, adjustment and fill layers, layer effects and editable text come in as their stored pixels only.

## AVIF

Impasto can open AVIF images, and saves them using the libavif library. The Windows and macOS builds include it. On Linux, AVIF saving is available when libavif is installed on your system; without it, AVIF is open-only.

## ICO

Icon files can't be larger than 256 × 256 pixels. Impasto shows an "Image too large" message if you try to save a bigger image as ICO; resize it first (see [Canvas and zoom](/docs/canvas/)).

## Importing a file as a layer

**Layers > Import from File** adds an image file to the current document as a new layer. It reads every format the file picker offers, including OpenRaster, PDN and AVIF. See [Layers and objects](/docs/layers/).
