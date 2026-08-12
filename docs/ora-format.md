# Impasto's OpenRaster files

`.ora` is Impasto's only lossless format: it is what Save produces for a layered
document, and what [autosave](../Pinta.Core/Managers/AutosaveManager.cs) writes. It is a
ZIP archive following the [OpenRaster spec](https://www.openraster.org/), plus two extra
entries that carry the re-editable objects the spec has no place for.

Reader and writer both live in `Pinta.Core/ImageFormats/OraFormat.cs`.

## Archive layout

| Entry | Written by | Notes |
|---|---|---|
| `mimetype` | Impasto and every ORA writer | `image/openraster`, stored uncompressed. Must be first |
| `stack.xml` | Impasto and every ORA writer | Layer stack, top layer first |
| `data/layer<i>.png` | Impasto and every ORA writer | One per user layer, in bottom-to-top index order. **Raster pixels only** |
| `mergedimage.png` | Impasto and every ORA writer | The whole document flattened, objects included |
| `Thumbnails/thumbnail.png` | Impasto and every ORA writer | Longest side 256px |
| `data/impasto-text.xml` | Impasto only | Re-editable text objects. Omitted when the document has none |
| `data/impasto-shapes.xml` | Impasto only | Re-editable shape objects. Omitted when the document has none |
| `data/impasto-shapes-layer<i>.png` | Impasto only | Rendered shape overlay for layer `i`, one per layer that has shapes |

`stack.xml` is plain ORA: `<image w h version="0.0.5"><stack><layer opacity name
composite-op src visibility>`. Blend modes are translated to and from the standard
`composite-op` names, so a layer stack survives a round trip through any other ORA
application.

## What the object entries add

An Impasto layer is raster pixels plus an ordered list of objects (text, shapes) that stay
editable after they are drawn. The pixels go in `data/layer<i>.png`; the objects go in the
two Impasto entries, keyed back to their layer by index:

```xml
<impasto-shapes version="1">
  <layer index="0">
    <shape type="2" name="Ellipse 1" hidden="0" object-opacity="1" object-blend="0"
           outline="#000000ff" fill="#ffffffff" width="2" …>
      <arrow1 show="0" … /><arrow2 show="0" … />
      <point x="10" y="12" tension="0" />
      <clip><poly><pt x="0" y="0" />…</poly></clip>
    </shape>
  </layer>
</impasto-shapes>
```

`data/impasto-text.xml` has the same shape, with `<text>` elements holding the font,
colors, alignment, bounds, wrap width and one `<line>` element per line of the text engine.
`name`, `hidden`, `object-opacity` and `object-blend` are common to both kinds and are read
and written in one place (`WriteObjectCommon`/`ReadObjectCommon`), so the two can't drift.

`<clip>` is the frozen draw-time selection: without it, a shape drawn inside a selection
comes back unclipped on reopen.

The `data/impasto-shapes-layer<i>.png` overlays are the objects as pixels. Impasto restores
them into the layer's object surface on load, so a document opens looking right even before
any object is re-edited.

## Compatibility

- **Other ORA applications read Impasto files correctly.** Unknown entries are ignored, and
  objects still appear in `mergedimage.png` and the thumbnail. What they lose is
  editability, and objects will be missing from the individual `data/layer<i>.png` — an
  application that rebuilds the image from the layer stack alone shows the raster content
  without the objects.
- **Impasto reads other applications' ORA files correctly.** The Impasto entries are
  optional; without them a document is simply all raster.
- **Round-tripping through another editor drops the objects**, since it will not rewrite the
  Impasto entries. Rasterize before handing a file off if that matters.

## Adding to the format

Bump the `version` attribute on `impasto-text`/`impasto-shapes` only when old Impasto builds
would misread a new file. Adding an attribute doesn't need it: every attribute is read
through `GetAttribute (element, name, default)`, so an older file missing it, or an older
build ignoring it, both keep working.

Every new attribute needs its write, its read and its default. A value written but not read
back is silently lost on reopen, and the round trip is the only thing that catches it.
