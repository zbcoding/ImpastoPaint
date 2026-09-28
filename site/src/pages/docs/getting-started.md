---
layout: ../../layouts/DocsLayout.astro
---

Impasto is an open-source painting and image editor for Linux, Windows, and macOS. It handles quick edits, crops and resizes, but its focus is painting and layered image work: brush and shape tools, editable text, selections, masks, blend modes, a full undo history, and effects.

## Install it

Pick your platform:

- [Linux](/docs/install-linux/) — Flatpak, AppImage, or a zip that uses your system GTK.
- [Windows](/docs/install-windows/) — an installer for x64 and arm64.
- [macOS](/docs/install-macos/) — a disk image for Apple Silicon and Intel.

Windows and macOS builds are not code-signed, so both systems show a warning the first time you open the app. The install pages show how to get past it.

## Open your first image

Choose **File > Open**, or press <kbd>Ctrl</kbd>+<kbd>O</kbd>, and pick a file. Every file opens in its own tab, so you can keep several images side by side and switch between them with <kbd>Ctrl</kbd>+<kbd>Tab</kbd>.

To start from nothing instead, choose **File > New** (<kbd>Ctrl</kbd>+<kbd>N</kbd>) and set the size, orientation, and background color. The default size for new images is configurable in **Edit > Settings > Canvas**.

![The Impasto window showing a document, the toolbox, and the docked pads](/assets/screenshot-text-tool.png)

Drop a file onto the window to open it. Dragging an image from another application onto the canvas pastes it as a new layer, which is the usual way to combine two images.

## Find your way around

The window is made of a few regions, described in full under [The interface](/docs/interface/):

| Region | What it is for |
|---|---|
| Menu bar | Every command, grouped by File, Edit, View, Image, Layers, Adjustments, Effects, and Help. |
| Quick access toolbar | New, Open, Save, Undo, Redo, and the clipboard commands. |
| Toolbox | The column of tools on the left, divided into sections. |
| Tool options bar | The settings for the tool that is currently selected. It changes as you change tools. |
| Canvas | The image itself, with rulers when you turn them on. |
| Side dock | The **Layers**, **History**, and **Canvas** pads. Each can be minimized, maximized, or floated. |
| Status bar | The palette, cursor position, image size, and zoom controls. |

Almost every control has a tooltip. Hover over a button or field to see what it does, and which key selects it if it has one.

## The basic loop

1. **Pick a tool** from the toolbox, or press its key: <kbd>B</kbd> for the paintbrush, <kbd>P</kbd> for the pencil, <kbd>E</kbd> for the eraser, <kbd>T</kbd> for text, <kbd>S</kbd> for the selection tools.
2. **Set its options** in the tool options bar. Brush width, blend mode, fill style, and so on are per tool, and Impasto remembers them between sessions.
3. **Work on the canvas.** Left click uses the primary color; right click uses the secondary color wherever a tool distinguishes them.
4. **Undo with <kbd>Ctrl</kbd>+<kbd>Z</kbd>** if it goes wrong. The [History pad](/docs/history/) lists every step and lets you jump back several at once.
5. **Save.** <kbd>Ctrl</kbd>+<kbd>S</kbd> saves in the file's own format. <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> (Save As) lets you choose another one.

## Paint versus objects

Impasto has two kinds of artwork, and knowing which you are working with explains most of its behaviour:

- **Pixels** — brush strokes, fills, pasted images, anything painted directly onto a layer. They are permanent: once painted, a stroke can only be changed by painting over it or undoing it.
- **Objects** — text and shapes stay editable after you draw them. Their words, colors, fonts, and control points can be changed at any time, and each object carries its own history so you can undo an edit to one object without disturbing anything else.

All the painting tools work on pixels. Text and the shape tools create objects. Objects live inside a layer, and anything that needs flat pixels — cut, erase, crop, resize, rotate, flip, flatten — bakes them down permanently first; see [Layers and objects](/docs/layers/).

## Next steps

- [The toolbox](/docs/tools/) — what every tool does.
- [Layers and objects](/docs/layers/) — the core of layered editing.
- [Keyboard shortcuts](/docs/keyboard-shortcuts/) — the defaults, and how to change them.