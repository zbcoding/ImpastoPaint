# Roadmap

## Next up

Roughly in order of value per unit of pain:


TODO: pin buttons on flyout by hovering grouped icons needs fixing

1. **Run it.** Colors docking and the toolbox border are build-verified only.
2. Click through the unverified paths — More >>, Float Colors. (Stack flyout and pin
   menu have now been exercised; the hover-dismissal bug found that way is fixed.)
3. Confirm or dismiss the toolbox column issue.

## Deferred — the big rocks

These are real projects, not tasks. Don't let them into scope casually.

- **Numeric colour entry inline.** `ColorWheelWidget` covers hue/sat/value; alpha, RGB
  fields and hex still live only in `ColorPickerDialog` behind "More >>". Extracting them
  means refactoring a 1,013-line dialog — the worst rebase target in the tree. Not worth
  it unless the round trip through the modal proves annoying in practice.
- **Merging the shape tools.** Paint.NET has *one* Shapes tool with a picker in the
  options bar. Impasto has four tools sharing a button, which looks similar and is far
  cheaper. Real merging means refactoring `ShapeTool` subclasses in `Pinta.Tools` — the
  one place rebases get expensive.
- **Paint.NET plugin compatibility.** The actual moat, and a separate project. Phase 1
  would be `BitmapEffect` + IndirectUI (current API, CPU, portable). Phase 2, classic
  `Effect` (deprecated but a huge catalogue, thin adapter over phase 1). Phase 3,
  `GpuImageEffect` — needs Direct2D effect-graph semantics; a Wine-hosted subprocess
  works for applying effects but is too slow for the live preview that is the whole
  point. Phase 3 may never be worth it.
