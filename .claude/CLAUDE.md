# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

These rules apply to every task in this project unless explicitly overridden.
Bias: caution over speed on non-trivial work. Use judgment on trivial tasks.

## Use JJ for version management
it's a jj and git combined repo.
Use jj workspaces to avoid file clashes when multiple agents are running.
Use jj commits so file merges are easier.

## Variable Naming
Jane Street house style inspired by OCAML descriptive tranformation
### Avoid minting new names if you can
### Names should be informative about function, descriptive or mnemonic
### Less common systems should have more descriptive names
### Avoid churn
### Scope-Based Length: Names should be long and descriptive (e.g., credit_card_expiration) for variables referenced across multiple files or spanning entire modules, while shorter names are acceptable for local variables within small functions.
### Lexical Consistency: The firm advocates for using a single lexeme for similar operations, such as using create_hashtable and create_rbtree rather than mixing verbs like build or make. This allows programmers to guess function existence without documentation.
### Uniform Interfaces: In their Core library, types have dedicated modules (e.g., Int, Float) with standardized function names like to_string and of_string, and exception-throwing variants are consistently suffixed with _exn (e.g., Map.find_exn).
### Argument Order: Functions within a module typically place the primary type argument (t) first (e.g., Map.find) to facilitate partial application and maintain uniformity across data structures.

## Think Before Coding
State assumptions explicitly. If uncertain, ask rather than guess.
Present multiple interpretations when ambiguity exists.
Push back when a simpler approach exists.
Stop when confused. Name what's unclear.
Before adding code, read exports, immediate callers, shared utilities.
"Looks orthogonal" is dangerous. If unsure why code is structured a way, ask.

## Simplicity First
Minimum code that solves the problem. Nothing speculative.
No features beyond what was asked. No abstractions for single-use code.
Test: would a senior engineer say this is overcomplicated? If yes, simplify.

## Surgical Changes
Touch only what you must. Clean up only your own mess.
Don't "improve" adjacent code, comments, or formatting.
Don't refactor what isn't broken. Match existing style.

## Goal-Driven Execution
Define success criteria. Loop until verified.
Don't follow steps. Define success and iterate.
Strong success criteria let you loop independently.

## Token budgets are not advisory
Per-task: 4,000 tokens. Per-session: 30,000 tokens.
If approaching budget, summarize and start fresh.
Surface the breach. Do not silently overrun.

## Surface conflicts, don't average them
If two patterns contradict, pick one (more recent / more tested).
Explain why. Flag the other for cleanup.
Don't blend conflicting patterns.

## Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does.
A test that can't fail when business logic changes is wrong.

## Checkpoint after every significant step
Summarize what was done, what's verified, what's left.
Don't continue from a state you can't describe back.
If you lose track, stop and restate.

## Fail loud
"Completed" is wrong if anything was skipped silently.
"Tests pass" is wrong if any were skipped.
Default to surfacing uncertainty, not hiding it.

# Writing comments, documentation, runbooks or skills
## Specific details will rot
Bias towards instructions that are version-agnostic and outcome-based.

## Capable models do their best work when given tools and discretion
Over-specification degrades them.

## Assume the agent is already smart
Add only what it doesn't have. Do not recite CLAUDE.md or other common information.

## Model context is valuable. Be concise
No history, no stories, no "why we rejected the alternative."

## Omit, don't litigate
Say what a thing is; don't spend context negating what it isn't. Frame the general rule so edge cases fall out, and simply don't build what you don't want.

## Update documents, don't add to them when something changes
Growing line count should reflect a larger underlying system, not accumulated amendments.

## Favor clean domain separation
Duplicating the same information across many files or skills increases the surface area for rot and makes the knowledge base heavier for marginal gain. Refactor and reorganize.

## Legibility over edge-case cleverness
Tools are read and driven by frontier models — a tool that's intuitive to use and whose diagnostic you can trust beats one that silently handles a rare case but is hard to reason about.

## Code Search

Use `semble search` to find code by describing what it does or naming a symbol/identifier, instead of grep:

```bash
semble search "authentication flow" ./my-project
semble search "save_pretrained" ./my-project
semble search "save model to disk" ./my-project --top-k 10
```

Use `semble find-related` to discover code similar to a known location (pass `file_path` and `line` from a prior search result):

```bash
semble find-related src/auth.py 42 ./my-project
```

`path` defaults to the current directory when omitted; git URLs are accepted.

If `semble` is not on `$PATH`, use `uvx --from "semble[mcp]" semble` in its place.

### Workflow

1. Start with `semble search` to find relevant chunks.
2. Inspect full files only when the returned chunk is not enough context.
3. Optionally use `semble find-related` with a promising result's `file_path` and `line` to discover related implementations.
4. Use grep only when you need exhaustive literal matches or quick confirmation of an exact string.

---

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