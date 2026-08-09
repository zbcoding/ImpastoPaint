# Feature status

What has been built in the fork, and how far each item has actually been verified.
"Build only" means it compiles and the app starts; it has never been exercised on screen.

| Feature | Where | Verified |
|---|---|---|
| Rename to Impasto: app ID, icons, desktop entry, metainfo, window title, settings dir | 6 files + 6 icon renames | yes, on screen |
| Menu bar by default (File/Edit/View/… instead of hamburger) | `MainWindow.cs` `IsUsingMenuBar()` | yes, on screen |
| Toolbox split into 6 sections with separators | `ToolBoxWidget.cs` | yes, on screen |
| Toolbox fixed at 2 columns | `ToolBoxWidget.cs` | partially — see Known issues |
| Shape tools collapsed into one stacked button with flyout | `ToolBoxWidget.cs` | button collapse yes; **flyout popover never clicked** |
| Right-click to pin a stacked tool into a Pinned section | `ToolBoxWidget.cs` | rendering + persistence yes;  |
| Colors palette docked in the status bar, View → Float Colors pops it out | `MainWindow.cs` | build only, **never run** |
| Inline HSV colour wheel, shown when floating | `ColorWheelWidget.cs` (new) | build only, **never run** |
| Dock tooltips: "Minimize to icon" / "Maximize to side menu" | `DockItem.cs` | build only, **never run** |
| Border around the toolbox | `MainWindow.cs` `HasFrame` | build only, **never run** |
| "More >>" opens the full colour picker | `MainWindow.cs` | **build only, never clicked** |
| Canvas surround colour preference in Edit → Settings | `PreferencesDialog.cs`, `CanvasWindow.cs` | build only, **never run** |
| Optional darker third row in the palette (48 colors), Edit → Settings → UI | `PaletteHelper.cs`, `PreferencesDialog.cs`, `PaletteWidget.cs` | build only, **never run** |
| Triangle shape tool, added as a 5th entry to the existing shape-type list (Open/Closed Line-Curve Series, Ellipse, Rounded Line Series). A "Type:" dropdown (monochrome right/equilateral icons) picks the default, and holding the switch key (default Shift, rebindable) draws the other one; the triangle grows up or down from the first point | `TriangleTool.cs`, `TriangleEditEngine.cs` (new), `BaseEditEngine.cs`, `ShapeEngineCollection.cs`, `SettingNames.cs`, `KeyboardShortcutManager.cs` | build + unit tests, app starts; **not interactively hand-verified** |
| **Text as re-editable objects** — multiple per layer, ctrl-click to re-edit, right-drag to move, hover tooltip "Ctrl+Click to edit", saved in `.ora` sidecar | `TextTool.cs`, `TextObject.cs`, `TextHistoryItem.cs`, `OraFormat.cs`, `UserLayer.cs` | build + unit tests, app starts; **interactive text editing not hand-verified**. A code review (`docs-private/commit-review.md`) found and fixed 4 bugs: fill/outline/join style was read live from the toolbar instead of stored per object (so editing one object's style bled onto every other object on the layer, and `.ora` round-trips only kept one style per layer); switching layers mid-edit skipped committing, leaking empty objects and dropping unsaved edits from undo history; Ctrl+click re-editing an object left the toolbar showing stale font/style instead of the object's own. |
| **Shapes as re-editable objects** — shape state is owned by each `UserLayer`, remains editable after switching tools, and round-trips through an Impasto `.ora` XML/raster sidecar | `ShapeObject.cs`, `UserLayer.cs`, `ShapeEngineCollection.cs`, `BaseEditEngine.cs`, `OraFormat.cs` | build + focused model test; interactive editing and ORA round-trip still need hand verification |
| Responsive status bar / colors bar folding as the window narrows: cursor-position and image-size labels vanish instantly (no fade) when they'd collide with the color area, then quick/recent-colors swatches and finally primary/secondary swatches fold out of the docked palette bar into the color-wheel popover. Folding uses hysteresis (a section stays folded until the bar regrows past a wider threshold) so it doesn't flicker back, and the color-wheel/float buttons stay drawn on top of the sliding bars behind an opaque rounded pill | `ActionManager.cs`, `StatusBarColorPaletteWidget.cs`, `MainWindow.cs` | build + unit tests; **narrow-window folding not hand-verified on screen** |

The extra palette row restores the 14 dark "127,x" hues that upstream's status-bar
palette had (Pinta PR #154, issue #812) as a user setting, off by default. When enabled
the default palette grows from 34 to 48 colors, the palette/recent-color widgets (status
bar, floating colours, colour-picker dialog) lay out in three rows, and the status bar
grows taller. The recent-colors list holds 12 instead of 10 so the third row reads as a
full 4×3 grid.
