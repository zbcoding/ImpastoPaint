# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Impasto** is a fork of [Pinta](https://github.com/PintaProject/Pinta), adopting Paint.NET's interface while maintaining a GTK-based implementation. It's a raster painting application with a .NET 10+ backend.

**Key constraint:** The fork must remain rebasable against upstream Pinta (`git fetch upstream && git rebase upstream/main`). This drives several architectural decisions documented in `IMPASTO.md`.

## Quick Build & Run

```bash
# Development build and run
dotnet build
dotnet run --project Pinta

# Alternative: using Make
make build
make run

# Run tests
dotnet test Pinta.sln

# Run a single test
dotnet test Pinta.sln --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Format check (CI requirement)
dotnet format --no-restore --verify-no-changes

# Format and fix issues
dotnet format
```

On **macOS**, set `DYLD_LIBRARY_PATH` before running:
- Apple Silicon: `DYLD_LIBRARY_PATH=/opt/homebrew/lib dotnet run --project Pinta`
- Intel: `DYLD_LIBRARY_PATH=/usr/local/lib dotnet run --project Pinta`

## Architecture

The solution is organized into logical layers:

- **Pinta** — Main application shell, UI bootstrap, and main window
- **Pinta.Core** — Rendering engine, pixel manipulation, actions, managers (history, undo/redo)
- **Pinta.Tools** — Tool implementations (brush, eraser, selection tools, etc.)
- **Pinta.Effects** — Built-in raster effects (blur, sharpen, etc.)
- **Pinta.Gui.Widgets** — Custom GTK widgets (color picker, dock widget helpers)
- **Pinta.Docking** — Dockable window system (ported from Paint.NET)
- **Pinta.Resources** — Icons, translations, theme assets
- **tests/** — Unit tests (NUnit framework)

### Design Pattern: Tool System

Tools inherit from `BaseTool` in `Pinta.Tools` and are registered via `CoreToolsExtension`. The toolbox layout (6 sections with separators) is driven by tool `Priority` values, not hardcoded lists, so addins automatically slot into the right section.

### Design Pattern: Actions

Painting operations use the Action pattern (`Pinta.Core.Actions`) for undo/redo support. Most user-facing operations should record an action rather than directly mutate the canvas.

## Rebase Strategy & Constraints

Impasto must stay rebasable against upstream Pinta: `git fetch upstream && git rebase upstream/main`. This drives several hard constraints.

**Golden Rule: Never rename** namespaces, assemblies, or project files—they stay `Pinta.*`. Only user-visible branding (app ID, window title, settings directory, desktop entry, metainfo) changes. Renaming touches every file and makes every rebase a conflict.

**Minimize changes in high-churn areas**:
- `Pinta.Core` — Only three one-line edits so far; keep it that way. Changes touch deep engine logic.
- `Pinta.Tools` — Currently untouched; tool subclasses are expensive rebase conflict hotspots.

**Prefer existing settings over new features**. Example: The menu bar was already fully built for macOS—it only needed a one-line default change, not a rewrite.

**Mark simplifications with `ponytail:` comments** that name the cost ceiling and upgrade path. This signals intentional shortcuts, not ignorance.

**Language in commit messages**: Avoid naming the trademarked reference editor by name. Use generic phrasing like "reference implementation," "major raster editors," "Impasto," or "Pinta" instead. (Descriptive text in `IMPASTO.md` and docs can mention the trademark for context, but commit logs should not.)

## Coding Conventions

- **Language:** C# (.NET 10+)
- **GTK version:** GTK 4 (via GirCore bindings)
- **Formatting:** C# conventions; enforced by `dotnet format` (CI requirement)
- **NUnit** for tests, with `[TestFixture]` and `[Test]` attributes
- **Undo/Redo:** Implement `IHistoryItem` for canvas-mutating operations
- **No nullable reference type strictness relaxation** — use the `#nullable` directives already in place

## Key Files & Folders

- `IMPASTO.md` — Development log, known issues, deferred work, and rebase constraints
- `patch-guidelines.md` — How to contribute patches to this fork
- `Pinta.sln` — Solution file (open in VS or build via dotnet CLI)
- `Makefile.am` — Autotools build recipe (used for distribution tarballs)
- `tests/Pinta.Core.Tests/` — Core engine unit tests
- `.github/workflows/build.yml` — CI/CD pipeline (Linux, macOS, Windows)

## Before Submitting Code

1. **Run tests**: `dotnet test Pinta.sln`
2. **Check formatting**: `dotnet format --no-restore --verify-no-changes` (or run `dotnet format` to fix)
3. **Keep diffs rebase-friendly**: Small, focused changes; don't touch naming or file structures
4. **Avoid renaming** — If a refactor needs naming changes, coordinate with upstream first or defer it
5. **Test the app** — Start with `dotnet run --project Pinta` and verify the feature in the UI
6. **Update IMPASTO.md** if adding user-visible features, known issues, or deferred work
7. **Keep commit messages generic** — Use "reference implementation" rather than trademarked names

## Translation & Localization

- Translatable strings use `Translations.GetString("key")`
- Translation files are in `po/` (gettext format)
- New strings must be added to `po/POTFILES.in` via `make updatepotfiles`
- Run `make updatepot` to regenerate the translation template

## Completed Features (This Fork)

Based on `IMPASTO.md`, these are verified working:

- Renamed to Impasto: app ID (`com.github.zbcoding.Impasto`), window title, settings directory
- Menu bar by default (File/Edit/View/…) instead of hamburger
- Toolbox split into 6 priority-based sections with separators
- Toolbox fixed at 2 columns (mostly)
- Shape tools collapsed into stacked button with right-click pin menu
- Tool right-click to pin into Pinned section (persistence + rendering verified)

These are built but not fully verified in UI:
- Colors palette docked in status bar, View → Float Colors pops it out
- Inline HSV color wheel in floating colors
- Dock tooltips: "Minimize to icon" / "Maximize to side menu"

## Deferred (Big Rocks)

These are real projects, not quick tasks. Don't add them to scope casually:

1. **Numeric color entry inline** — `ColorWheelWidget` covers hue/sat/value; alpha, RGB fields, hex still in `ColorPickerDialog` (a 1,013-line dialog; worst rebase target in the tree)
2. **Merging shape tools** — Impasto has four tools sharing a button (looks similar, cheap); real merge needs refactoring `ShapeTool` subclasses in `Pinta.Tools`
3. **Paint.NET plugin compatibility** — The actual moat; multi-phase project involving `BitmapEffect`, `IndirectUI`, classic `Effect` adapter, and `GpuImageEffect`

## Known Issues

- **Toolbox column count** under wide/maximized windows may show a single column with stray buttons instead of clean 2-column grid (unconfirmed; reproduce before changing)
- Binary still named `pinta`; `.desktop` `Exec` matches it (rename binary + autotools packaging together or not at all)
- Icon artwork still Pinta's (MIT-licensed, fine to ship; swap before release)
- Bug/support URLs still point at PintaProject; fix once this repo stabilizes

## Debugging Tips

- The app logs to stderr; run with `2>&1 | grep -E "(Exception|Error|warning)"` to filter
- GTK debug output can be verbose; set `G_MESSAGES_DEBUG=all` to see all GTK messages
- Use Visual Studio debugger or `lldb` on macOS for line-level debugging
- History/undo state is in `IHistoryManager`; check it if operations don't persist correctly

Unless noted, focus more on feature addition, less on time consuming verification and checking, because I'm checking the software as features are added by running dotnet run --project Pinta 