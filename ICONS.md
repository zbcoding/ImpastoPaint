# Icon Notes

How symbolic tool/action icons are authored in this repo, learned the hard way
while redoing `tool-select-lasso-scissors-symbolic.svg`.

## Where they live

`Pinta.Resources/icons/hicolor/scalable/actions/*-symbolic.svg`

Sizes below `scalable/` (`16x16`, `22x22`, `24x24`, `32x32`, `96x96`) hold
raster/fixed variants for icons that have them; most tool icons only need the
scalable one. GTK resolves icon names against this tree at runtime via
`IconTheme.AddSearchPath` (`Pinta/Main.cs:104`), pointing at
`SystemManager.GetDataRootDirectory()/icons` — there is no gresource bundling
step, so editing the `.svg` file directly is enough, no separate build step
regenerates or embeds it.

## Format that actually works

Look at any existing one before writing a new one, e.g.
`tool-select-lasso-symbolic.svg` or `tool-select-rectangle-symbolic.svg`:

- `viewBox="0 0 24 24"`, `width="24" height="24"`.
- **One color, filled paths only** — `fill="#bebebe"`, no `stroke`. GTK
  recolors `-symbolic` icons for the active theme (light/dark, selected
  state, etc.) by treating the fill as the recolorable foreground. Hardcoded
  `#ffffff` or stroked line art does **not** recolor the same way and looks
  wrong/inconsistent against the toolbox background — this is what went
  wrong the first two times.
- Rings/holes (e.g. scissor handles) are done as a single path with two
  subpaths of opposite winding (nonzero fill-rule), not a stroked circle.
  See the donut circles in the Material `content-cut` glyph used in the
  current scissors icon.
- Keep shapes **bold and edge-to-edge** (roughly x/y 1–23), not confined to
  a small corner of the canvas. The toolbox renders these at ~32px; thin
  strokes or a glyph occupying half the canvas turns into visual mush at
  that size. Prefer solid filled shapes over multiple thin outlined ones.

## Composite icons (e.g. "scissors select")

When a tool icon needs to combine two concepts (a scissors glyph + a
selection-outline concept, in this case), don't cram both at full size —
scale one down and stack them so the whole composition still reads as
bold shapes filling the 24×24 canvas:

```xml
<path fill="#bebebe" transform="translate(2,1) scale(0.6)" d="..."/>
<path fill="#bebebe" d="...second element, native scale..."/>
```

## Workflow

1. Edit/write the `.svg` directly under `scalable/actions/`.
2. `dotnet build` — no gresource/pack step needed, the file is read from disk
   at runtime.
3. `dotnet run --project Pinta` and actually look at it in the toolbox at
   real size. A `Read`-tool view of the SVG source tells you nothing about
   how it reads at 32px — check the real render before calling it done.
4. Commit the icon change as its own `jj` commit (`jj split -r @ <path> -m
   "..."`) rather than letting it ride along inside an unrelated in-progress
   commit — see the scissors-icon incident where redesign work never got
   committed and silently vanished from the working copy.
