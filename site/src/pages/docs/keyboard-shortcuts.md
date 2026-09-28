---
layout: ../../layouts/DocsLayout.astro
---

Every menu command, every tool in the toolbox, and most keys that tools respond to on the canvas can be rebound. This page lists the default shortcuts and explains how to change them. If you have changed a shortcut, the tooltips on toolbar buttons and tool buttons show your key, not the default.

> On macOS, shortcuts listed here with <kbd>Ctrl</kbd> use <kbd>⌘ Command</kbd> instead. A few alternate shortcuts keep the Control key on macOS: <kbd>Ctrl</kbd>+<kbd>Y</kbd> (Redo), <kbd>Ctrl</kbd>+<kbd>D</kbd> (Deselect All), <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>X</kbd> (Auto Crop) and <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>A</kbd> (Save All).

## Changing a shortcut

Open the Keyboard Shortcuts window in one of these ways:

- **Help > Keyboard Shortcuts**
- <kbd>Ctrl</kbd>+<kbd>,</kbd>
- **Edit > Settings...**, then the **Keyboard** tab, then **Keyboard Shortcuts...**

The window has one tab per group: **Tools**, **Tool Specific**, **Layers**, **File**, **Edit**, **View**, **Image**, **Adjustments**, **Effects**, **Window** and **Help**. The search box at the top searches every tab at once. Type part of a command name, or put a key combination in quotes, such as `"ctrl-A"`, to find what a key is bound to.

To rebind a command:

1. Click the shortcut button next to the command. It changes to **Press keys…**.
2. Press the new key combination.
3. Click **OK** to apply your changes, or **Cancel** to discard them.

While a button shows **Press keys…**:

- <kbd>Esc</kbd> cancels and keeps the old key.
- Clicking another shortcut button, or **OK**, without pressing a key sets the shortcut to **None**. This is how you remove a shortcut.

The **Reset to default** button (an undo arrow) at the end of each row puts that shortcut back to its default. **Reset All to Defaults**, in the window's title bar, resets every command, tool, and tool-specific key after asking you to confirm.

Some commands have more than one default shortcut. Each extra shortcut has its own row, labelled **(Alternate)**, so you can change one without losing the other. For example, **Deselect All** and **Deselect All (Alternate)** are <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>A</kbd> and <kbd>Ctrl</kbd>+<kbd>D</kbd>.

### Duplicate shortcuts

If two commands share a key, both rows turn red and get a `*` marker. Hover over either one to see **Duplicated**. Rebind one of them so the key does one thing.

Duplicates are allowed on the **Tools** and **Tool Specific** tabs. Several tools can share a key, and pressing the key again moves to the next of them. Tool-specific keys only apply while their tool is active, so the same key can mean different things in different tools. If you give two keys in the same tool group the same binding, the other one goes back to its default.

> By default, <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd> is assigned to both **View > Show Grid** and the **Black and White** adjustment, so both rows show as duplicated when you first open the window. Rebind one of them if you use both.

## Where custom shortcuts are saved

When you click **OK**, Impasto saves your changed shortcuts to `keyboard-shortcuts.json` in its settings folder:

| System | File |
|---|---|
| Linux | `~/.config/Impasto/keyboard-shortcuts.json` (inside `$XDG_CONFIG_HOME` if you have set it) |
| Linux (Flatpak) | `~/.var/app/com.github.zbcoding.Impasto/config/Impasto/keyboard-shortcuts.json` |
| Windows | `%APPDATA%\Impasto\keyboard-shortcuts.json` |
| macOS | `~/.config/Impasto/keyboard-shortcuts.json` |

The file only lists the shortcuts you have changed. It is plain JSON, so you can copy it to another computer or edit it by hand while Impasto is closed. If the file is missing or can't be read, Impasto starts with the default shortcuts. Deleting the file and restarting Impasto restores every default.

Your shortcuts are also included when you use **Edit > Settings... > Backup > Export Settings...**. When you import a settings file that contains shortcuts, Impasto asks whether to import them or to **Keep Mine**, because importing replaces your current `keyboard-shortcuts.json`.

## Tools

Press a tool's key to switch to it. Keys typed into a text box, or into text you are editing with the Text tool, go there instead. Where several tools share a key, press it again to cycle through them. See [the toolbox overview](/docs/tools/) for what each tool does.

| Key | Tools |
|---|---|
| <kbd>M</kbd> | Move Selected Pixels, Move Selection |
| <kbd>Z</kbd> | Zoom |
| <kbd>H</kbd> | Pan |
| <kbd>S</kbd> | Rectangle Select, Ellipse Select, Lasso Select, Magic Wand Select |
| <kbd>B</kbd> | Paintbrush |
| <kbd>P</kbd> | Pencil |
| <kbd>E</kbd> | Eraser |
| <kbd>F</kbd> | Paint Bucket |
| <kbd>G</kbd> | Gradient |
| <kbd>K</kbd> | Color Picker |
| <kbd>T</kbd> | Text |
| <kbd>O</kbd> | Line/Curve, Rectangle, Rounded Rectangle, Ellipse, Triangle, Freeform Shape |
| <kbd>L</kbd> | Clone Stamp |
| <kbd>R</kbd> | Recolor |

## File

| Command | Default shortcut |
|---|---|
| New... | <kbd>Ctrl</kbd>+<kbd>N</kbd> |
| Open... | <kbd>Ctrl</kbd>+<kbd>O</kbd> |
| Close | <kbd>Ctrl</kbd>+<kbd>W</kbd> |
| Save | <kbd>Ctrl</kbd>+<kbd>S</kbd> |
| Save As... | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> |
| Quit | <kbd>Ctrl</kbd>+<kbd>Q</kbd> |

Quit and the Keyboard Shortcuts window's own shortcut, <kbd>Ctrl</kbd>+<kbd>,</kbd>, are not listed in the Keyboard Shortcuts window, so you can't change them there.

## Edit

| Command | Default shortcut |
|---|---|
| Undo | <kbd>Ctrl</kbd>+<kbd>Z</kbd> |
| Redo | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Z</kbd> or <kbd>Ctrl</kbd>+<kbd>Y</kbd> |
| Cut | <kbd>Ctrl</kbd>+<kbd>X</kbd> |
| Copy | <kbd>Ctrl</kbd>+<kbd>C</kbd> |
| Copy Merged | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd> |
| Paste | <kbd>Ctrl</kbd>+<kbd>V</kbd> |
| Paste Alternate | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> |
| Paste Into New Image | <kbd>Shift</kbd>+<kbd>V</kbd> or <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>V</kbd> |
| Erase Selection | <kbd>Delete</kbd> |
| Fill Selection | <kbd>Backspace</kbd> |
| Invert Selection | <kbd>Ctrl</kbd>+<kbd>I</kbd> |
| Offset Selection | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd> |
| Select All | <kbd>Ctrl</kbd>+<kbd>A</kbd> |
| Deselect All | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>A</kbd> or <kbd>Ctrl</kbd>+<kbd>D</kbd> |
| Deselect All (Quick) | <kbd>Esc</kbd> |

Paste Alternate pastes onto a new layer. If you turn on **Paste external images onto a new layer by default** in **Edit > Settings... > Keyboard**, the two swap: Paste goes to a new layer and Paste Alternate pastes onto the current layer.

<kbd>Esc</kbd> finishes any text or shape you are editing before it deselects. With the pointer over the canvas, the Text and Lasso Select tools use the first press to finish typing or cancel the outline. If you deselect by accident, <kbd>Ctrl</kbd>+<kbd>Z</kbd> brings the selection back.

## View

| Command | Default shortcut |
|---|---|
| Zoom In | <kbd>Ctrl</kbd>+<kbd>+</kbd>, <kbd>Ctrl</kbd>+<kbd>=</kbd>, <kbd>=</kbd>, or <kbd>+</kbd> on the numeric keypad, with or without <kbd>Ctrl</kbd> |
| Zoom Out | <kbd>Ctrl</kbd>+<kbd>-</kbd>, <kbd>Ctrl</kbd>+<kbd>_</kbd>, <kbd>-</kbd>, or <kbd>-</kbd> on the numeric keypad, with or without <kbd>Ctrl</kbd> |
| Best Fit | <kbd>Ctrl</kbd>+<kbd>B</kbd> |
| Normal Size | <kbd>Ctrl</kbd>+<kbd>0</kbd> |
| Show Grid | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd> |
| Snap to grid/ruler/center | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>G</kbd> |
| Tool Windows | <kbd>F12</kbd> |
| Fullscreen | <kbd>F11</kbd> |

See [Canvas and zoom](/docs/canvas/) and [Snapping and guides](/docs/snapping/).

## Image

| Command | Default shortcut |
|---|---|
| Crop to Selection | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd> |
| Auto Crop | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>X</kbd> |
| Resize Image... | <kbd>Ctrl</kbd>+<kbd>R</kbd> |
| Resize Canvas... | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd> |
| Rotate 90° Clockwise | <kbd>Ctrl</kbd>+<kbd>H</kbd> |
| Rotate 90° Counter-Clockwise | <kbd>Ctrl</kbd>+<kbd>G</kbd> |
| Rotate 180° | <kbd>Ctrl</kbd>+<kbd>J</kbd> |
| Flatten | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> |

## Layers

| Command | Default shortcut |
|---|---|
| Add New Layer | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>N</kbd> |
| Delete Layer | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Delete</kbd> |
| Duplicate Layer | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>D</kbd> |
| Merge Layer Down | <kbd>Ctrl</kbd>+<kbd>M</kbd> |
| Layer Properties... | <kbd>F2</kbd> |
| Solo Layer 1 to Solo Layer 5 | <kbd>Ctrl</kbd>+<kbd>1</kbd> to <kbd>Ctrl</kbd>+<kbd>5</kbd> |

Solo Layer shows only one layer, counting up from the bottom layer. Press the same shortcut again to show all layers. See [Layers and objects](/docs/layers/).

## Adjustments

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

Effects have no default shortcuts. You can give any effect one from the **Effects** tab.

## Window and Help

| Command | Default shortcut |
|---|---|
| Save All | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>A</kbd> |
| Close All | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> |
| Switch to document 1 to 9 | <kbd>Alt</kbd>+<kbd>1</kbd> to <kbd>Alt</kbd>+<kbd>9</kbd> |
| Help > Contents | <kbd>F1</kbd> |

The document-switching keys are on the **Tool Specific** tab, under **Window**.

## Tool-specific keys

These keys only work while the matching tool is active. They are on the **Tool Specific** tab, grouped by the names shown in the first column.

### General and brushes

| Group | Action | Default |
|---|---|---|
| General | Swap primary and secondary colors | <kbd>X</kbd> |
| Brush Tools | Decrease brush width | <kbd>[</kbd> |
| Brush Tools | Increase brush width | <kbd>]</kbd> |
| Recolor Tool | Recolor in reverse (replace the primary color with the secondary) | <kbd>Alt</kbd>+left drag |

### Selection and moving

| Group | Action | Default |
|---|---|---|
| Lasso / Scissors Select | Finish selection | <kbd>Enter</kbd> |
| Lasso / Scissors Select | Undo last point | <kbd>Backspace</kbd> |
| Lasso / Scissors Select | Cancel selection | <kbd>Esc</kbd> |
| Transform Tools | Nudge selection 1 pixel | Arrow keys |
| Transform Tools | Nudge selection 10 pixels | <kbd>Shift</kbd>+arrow keys |
| Transform Tools | Nudge selection 5% of the canvas | <kbd>Ctrl</kbd>+arrow keys |
| Transform Tools | Nudge selection 20% of the canvas | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+arrow keys |

Each nudge direction is a separate binding, so you can change them one by one.

### Text tool

| Action | Default |
|---|---|
| Stop editing / Finalize text | <kbd>Esc</kbd> |
| Insert new line | <kbd>Enter</kbd> |
| Delete character left of cursor | <kbd>Backspace</kbd> |
| Delete character right of cursor | <kbd>Delete</kbd> |
| Move cursor left, right, up, down | Arrow keys |
| Move cursor to line start | <kbd>Home</kbd> |
| Move cursor to line end | <kbd>End</kbd> |
| Undo last text edit | <kbd>Ctrl</kbd>+<kbd>Z</kbd> |
| Toggle bold | <kbd>Ctrl</kbd>+<kbd>B</kbd> |
| Toggle italic | <kbd>Ctrl</kbd>+<kbd>I</kbd> |
| Toggle underline | <kbd>Ctrl</kbd>+<kbd>U</kbd> |
| Select all text | <kbd>Ctrl</kbd>+<kbd>A</kbd> |
| Copy text | <kbd>Ctrl</kbd>+<kbd>Insert</kbd> |
| Paste text | <kbd>Shift</kbd>+<kbd>Insert</kbd> |
| Decrease font size | <kbd>[</kbd> |
| Increase font size | <kbd>]</kbd> |
| Re-edit existing text | <kbd>Ctrl</kbd>+click |
| Open text properties | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+click |
| Resize text (drag corner, changes font size) | Left drag |
| Rotate text | <kbd>Alt</kbd>+left drag |

See [the Text tool](/docs/tools/text/).

### Gradient and shape tools

| Group | Action | Default |
|---|---|---|
| Gradient Tool | Finalize gradient | <kbd>Enter</kbd> |
| Shape Tools | Finalize shape | <kbd>Enter</kbd> |
| Shape Tools | Add control point at mouse position | <kbd>A</kbd> |
| Shape Tools | Add control point at exact same position | <kbd>Ctrl</kbd>+<kbd>Space</kbd> |
| Shape Tools | Delete selected control point | <kbd>Delete</kbd> |
| Shape Tools | Set selected control point to curve | <kbd>S</kbd> |
| Shape Tools | Set selected control point to line | <kbd>D</kbd> |
| Shape Tools | Move selected control point | Arrow keys |
| Shape Tools | Select previous control point | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>←</kbd> |
| Shape Tools | Select next control point | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>→</kbd> |
| Shape Tools | Create new shape at selected control point | <kbd>Ctrl</kbd>+<kbd>←</kbd> |
| Shape Tools | Change control point tension while dragging | Hold <kbd>Ctrl</kbd> while right-dragging a control point |
| Shape Tools | Rotate shape (drag a control point) | <kbd>Alt</kbd>+left drag |
| Triangle Tool | Switch between right and equilateral triangle while drawing | Hold <kbd>Shift</kbd> |

See [Shape tools](/docs/tools/shapes/).
