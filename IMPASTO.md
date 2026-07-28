# Impasto — development log

A fork of [Pinta](https://github.com/PintaProject/Pinta) (MIT) that adopts Paint.NET's
interface. Upstream stays the source of truth for the engine; this fork changes the shell.

Named Impasto because `Paint.NET` is a registered trademark of dotPDN LLC. Layout and
interaction patterns are free to reimplement — Pinta has done exactly that for 16 years —
but the name, logo and icon artwork are not. `Paint.Linux`, `PaintDot` and `SimplePaint`
were all rejected: the first two read as official ports, the third is unprotectable and
unsearchable (1,663 GitHub repos).

## Ground rules

The whole strategy depends on staying rebasable against upstream:

```
git fetch upstream && git rebase upstream/main
```

- **Do not rename namespaces, assemblies or project files.** They stay `Pinta.*`. Only
  user-visible branding changes. Renaming them touches every file and makes every rebase
  a conflict.
- **Avoid `Pinta.Core` and `Pinta.Tools`.** Changes there are the expensive ones. So far
  `Pinta.Tools` is untouched; `Pinta.Core` has three one-line edits.
- Mark deliberate simplifications with a `ponytail:` comment naming the ceiling.
- Prefer flipping an existing upstream setting over writing a feature. The menu bar was
  already fully built for macOS — it needed a one-line default change, not a rewrite.

## Status

### Done

| Feature | Where | Verified |
|---|---|---|
| Rename to Impasto: app ID, icons, desktop entry, metainfo, window title, settings dir | 6 files + 6 icon renames | yes, on screen |
| Menu bar by default (File/Edit/View/… instead of hamburger) | `MainWindow.cs` `IsUsingMenuBar()` | yes, on screen |
| Toolbox split into 6 sections with separators | `ToolBoxWidget.cs` | yes, on screen |
| Toolbox fixed at 2 columns | `ToolBoxWidget.cs` | partially — see Known issues |
| Shape tools collapsed into one stacked button with flyout | `ToolBoxWidget.cs` | button collapse yes; **flyout popover never clicked** |
| Right-click to pin a stacked tool into a Pinned section | `ToolBoxWidget.cs` | rendering + persistence yes; **pin menu never clicked** |
| Colors palette in a floating window | `MainWindow.cs` | yes, on screen |
| "More >>" opens the full colour picker | `MainWindow.cs` | **build only, never clicked** |

App ID is `com.github.zbcoding.Impasto`. Settings live in `~/.config/Impasto/settings.xml`
so Impasto installs alongside Pinta rather than replacing it — the metainfo deliberately
drops upstream's `<replaces>pinta.desktop</replaces>`.

### In progress — Colors docking

Half-built. **The menu item exists but does nothing yet.**

Done:
- `ViewActions.ColorsFloating` toggle command, registered and in View → menu
- `SettingNames.COLORS_FLOATING`

To do:
1. Add a `colors_dock` box to the status bar; dock the palette there **by default**
2. `SetColorsFloating(bool)` reparents the palette between dock and window, flipping
   orientation horizontal (docked) ↔ vertical (floating)
3. Give the floating window a close-only header bar
4. Persist `colors-floating`

Step 3 is the actual bug fix. The floating window is currently a plain resizable
top-level, so maximising it produces a huge empty window with tiny swatches in the
corner, and minimising sends it somewhere unreachable. Removing the maximise and
minimise buttons removes both states at the source.

Open question: the ask was "minimise returns it to docked". Removing the minimise button
reaches the same outcome without watching `Gdk.Toplevel` state. Literal minimise-to-dock
is possible, just more code for the same result.

## Known issues

- **Toolbox column count under wide/maximised windows.** One capture showed a single
  column with a stray pair at the top instead of the clean 2-column grid. May be the
  fixed `MinChildrenPerLine = 2` misbehaving under a different allocation, may be a
  screenshot artifact. Unconfirmed — reproduce before changing anything.
- **Pinned tools and the toggle group.** Every toolbox button shares one toggle group, so
  exactly one can be lit. A pinned tool has two buttons and the pinned one wins, so
  picking that tool from inside its stack flyout won't light the stack button. Fixing it
  means a second toggle group; the wart is cheaper.
- Bug and support URLs still point at PintaProject — `AboutDialogAction.cs:74-75`,
  `HelpActions.cs:113`. Fix once this repo exists.
- Installed binary is still named `pinta`; the `.desktop` `Exec` matches it. Rename the
  binary and the autotools packaging together or not at all.
- Icon artwork is still Pinta's. MIT, fine to ship, but swap it before any release.

## Next up

Roughly in order of value per unit of pain:

1. **Commit.** Six features, zero commits. Do this first.
2. Finish Colors docking (above).
3. Click through the three unverified paths — stack flyout, pin menu, More >>.
4. Confirm or dismiss the toolbox column issue.

## Deferred — the big rocks

These are real projects, not tasks. Don't let them into scope casually.

- **Inline colour wheel.** Paint.NET shows the wheel in the Colors window itself, with
  More >> expanding numeric fields. `ColorPickerDialog` already has a wheel, HSV/RGB
  sliders and hex entry — but it's a 1,013-line `Gtk.Dialog` and `DrawColorSurface` is a
  private instance method reading instance state. Extracting it means refactoring the
  worst possible rebase target; the alternative is a fresh Cairo wheel widget. Currently
  the wheel is reachable only through the modal dialog.
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

## Running it

```sh
dotnet build Pinta.sln
dotnet run --project Pinta          # add --no-build if nothing changed
```

Needs .NET 10 and GTK 4. Verified on GTK 4.22, Wayland/KDE.

## Toolbox layout reference

Sections are assigned by tool `Priority`, so addin tools land in a bucket without
knowing about `ToolBoxWidget`:

| Section | Priorities | Tools |
|---|---|---|
| Pinned | n/a | user-chosen, persisted as type names |
| Move | ≤ 8 | Move Selected, Move Selection |
| View | ≤ 12 | Zoom, Pan |
| Select | ≤ 20 | Rectangle, Ellipse, Lasso, Magic Wand |
| Paint | ≤ 36 | Paintbrush, Pencil, Eraser, Bucket, Gradient, Colour Picker, Text |
| Shapes | ≤ 46 | Line/Curve, and the stack |
| Retouch | rest | Clone Stamp, Recolor |

Stacks are declared in `stack_definitions` as priority groups. Adding the selection stack
(rectangle + ellipse marquee) is one array entry, no new code.
