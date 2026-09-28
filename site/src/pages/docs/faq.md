---
layout: ../../layouts/DocsLayout.astro
---

## Is Impasto free?

Yes. Impasto is free and open source under the MIT License, which means you can use it commercially, modify it, and redistribute it, provided the license text travels with it. The full text is in `license-mit.txt` in the repository, third-party attributions are in `THIRD-PARTY-NOTICES.md`, and the translation catalogues in `po/` are covered by their own notices.

## How is Impasto related to Pinta?

Impasto is a fork. It started from the MIT-licensed source of [Pinta](https://github.com/PintaProject/Pinta) and is now a separate project with its own interface, tools, and release cycle. Pinta's contributors are credited in the Impasto application.

Impasto is not maintained by the Pinta project or by Pinta's contributors, and bug reports about Impasto do not belong in Pinta's tracker. In practice the fork deliberately stays close to upstream so that it can pick up Pinta's fixes - the project keeps its `Pinta.*` project and namespace names for exactly that reason, even though the application is called Impasto.

Because the add-in system is shared, add-ins written for Pinta 3.0 or later also work in Impasto.

## How is Impasto related to Paint.NET?

Paint.NET is a separate, proprietary Windows application. Pinta was inspired by its interface, and Impasto follows the same conventions - layers, adjustments, effects, an undo history, and a toolbox with tool options - but it is not affiliated with Paint.NET and shares no code with it.

## Does Impasto work offline?

Yes. Drawing, editing, saving, and loading all work with no network at all. Only four things touch the network, and only when they apply:

- An update check once per launch.
- The add-in repository index, when the add-in gallery refreshes.
- Downloading an add-in, when you install one from the gallery.
- Opening a link, such as Help > Website or File a Bug, which hands the URL to your browser.

## What does Impasto send?

Nothing of yours. There is no telemetry, no usage reporting, and no image or settings upload.

| What happens | When | What leaves your machine |
|---|---|---|
| Update check | Once per launch | The request itself. No usage data, no image data, no settings; the only header added is `User-Agent: ImpastoPaint`. |
| Add-in index refresh | When the add-in gallery refreshes | The request itself. |
| Add-in download | Only when you install an add-in | The request itself. |
| Opening a link | Only when you click one | Nothing; your browser handles it. |

The website you are reading is separate from the application and does use analytics. The application does not.

## Why does Windows warn me about the download?

The releases are not code-signed. Windows SmartScreen therefore shows *Windows protected your PC* the first time you run the installer, and macOS Gatekeeper refuses to open the disk image until you allow it. Both are expected for unsigned open-source software, and neither means anything is wrong with the file - the [Windows](/docs/install-windows/) and [macOS](/docs/install-macos/) install pages walk through the dialogs. Only download Impasto from the project's own GitHub releases.

## Which systems does Impasto support?

- **Linux**: x86-64, with GTK 4.18 and libadwaita 1.8. The Flatpak and AppImage bundle their own; the zip uses your system's.
- **Windows**: x64, and arm64 on Windows on ARM.
- **macOS**: Apple Silicon and Intel.

Builds need the .NET 10 runtime, which the installers and packages carry. There is no mobile, tablet, or web version.

## Where are my settings, and how do I reset them?

Settings, keyboard shortcuts, recent files, and crash-recovery data live in one folder:

| System | Folder |
|---|---|
| Linux | `~/.config/Impasto/` |
| Linux (Flatpak) | `~/.var/app/com.github.zbcoding.Impasto/config/Impasto/` |
| Windows | `%APPDATA%\Impasto\` |
| macOS | `~/.config/Impasto/` |

Delete that folder with Impasto closed and it starts as if freshly installed. It is also the folder to consult when a setting seems stuck, since the files are plain text and readable JSON.

Rather than deleting, **Edit > Settings... > Backup > Export Settings...** writes everything - including shortcuts - to one file you can carry to another machine.

## How do I report a bug?

Open an issue at [github.com/zbcoding/ImpastoPaint/issues](https://github.com/zbcoding/ImpastoPaint/issues). A useful report includes:

- What you did, what you expected, and what happened instead.
- The Impasto version. On macOS it is in **Impasto > About Impasto**; anywhere, running the application with `--version` prints it.
- Your system and how you installed Impasto (Flatpak, AppImage, zip, installer, or built from source).
- Any error output. Launching Impasto from a terminal shows its log on standard error, and the messages around a crash name the operation that failed.

Feature requests are welcome in the same tracker, and so are pull requests - see `CONTRIBUTING.md` for the workflow.

## Why does Impasto look different from other image editors?

Because it follows Paint.NET's interface conventions rather than the free-desktop conventions most Linux image editors use: a docked layers pad, a history pad, adjustments and effects as first-class menus, an object system where shapes and text stay editable, and tool options in a bar rather than in a dialog.

If something does not look the way you expect, check **Edit > Settings...** first - the UI tab alone adds a dark palette row, changes the toolbox layout, wraps tool settings, and swaps the toolbox for a dropdown. Then check **View > Show/Hide**, which governs the menu bar, toolbar, toolbox, status bar, rulers, tabs, and pads.

## Where can I learn what a control does?

Hover over it. Almost every button, field, and chip has a tooltip that names what it does, and tool buttons and toolbar items also show the keyboard shortcut currently bound to them. Tool-specific keys, such as the ones the Shape and Text tools use, are also listed in the status bar at the bottom of the window while the tool is active.

Some hints are popovers rather than tooltips: the toolbox buttons, the hints that appear over a text or shape object on the canvas, and the palette swatch captions. **Edit > Settings... > UI > Popover hints** turns those down - either to the toolbox buttons only, or off entirely - without affecting ordinary tooltips.