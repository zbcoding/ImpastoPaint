---
layout: ../../layouts/DocsLayout.astro
---

Download the disk image for your Mac from the [releases page](https://github.com/zbcoding/ImpastoPaint/releases/latest):

| File | For |
|---|---|
| `Impasto-osx-arm64-unsigned.dmg` | Apple Silicon: M1, M2, M3, M4 and later. |
| `Impasto-osx-x64-unsigned.dmg` | Intel Macs. |

Not sure which you have? **Apple menu > About This Mac** shows the chip. If it says *Apple M…*, take the arm64 image.

Open the `.dmg` and drag **Impasto** onto the **Applications** shortcut in the same window. Eject the disk image afterwards.

The package bundles the GTK and libadwaita libraries it needs, so you do not have to install GTK yourself. It does need the .NET 10 runtime, which the package also carries.

## Getting past the Gatekeeper warning

The macOS builds are not signed with an Apple Developer ID, so macOS refuses to open them normally the first time. You get one of these:

- *"Impasto" cannot be opened because the developer cannot be verified.*
- *"Impasto" is damaged and can't be opened. You should move it to the Trash.* — for an unsigned build that arrived through a quarantine flag this is usually just Gatekeeper's wording, not a corrupted download; the quarantine fix below is what clears it.

To allow it, use one of these.

**macOS 14 and earlier — Control-click Open:**

1. Open **Finder** and go to the **Applications** folder.
2. Hold <kbd>Control</kbd> and click the Impasto icon, then choose **Open**. (Or right-click and choose **Open**.)
3. Click **Open** in the dialog, which now offers an Open button instead of only Cancel.

This exemption is remembered, so Impasto opens normally from then on.

**macOS 15 and later — Privacy & Security.** Sequoia removed the Control-click bypass for apps that are unsigned or not notarized, so that route no longer offers **Open**:

1. Double-click Impasto once, and dismiss the warning.
2. Open **System Settings > Privacy & Security** and scroll to the **Security** section.
3. Next to the message about Impasto being blocked, click **Open Anyway**.
4. Authenticate, then confirm with **Open**.

The **Open Anyway** button only appears for about an hour after the launch attempt, so do steps 1 and 2 close together. If it is missing, move the application out of Downloads into Applications, dismiss the warning again, and reopen the pane.

**Either version — clear the quarantine flag.** This is the equivalent of the Control-click bypass and works on both:

```bash
xattr -dr com.apple.quarantine /Applications/Impasto.app
```

Then open the app normally. You only need this once per copy, and it does not require changing any system setting.

One caveat: if the dialog says *"Impasto" will damage your computer* rather than *cannot be opened*, stop. That wording means the download is corrupted or has been modified, and the fix is to download the image again from the releases page. Do not disable Gatekeeper system-wide to run Impasto.

## Uninstalling

Drag **Impasto** from Applications to the Trash. Settings, keyboard shortcuts, and the recent-file list stay behind in `~/.config/Impasto`; delete that folder to remove them too.

## Building from source

Install .NET 10 and the GTK dependencies from Homebrew:

```bash
brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader libavif
```

Then set the library path so the GTK libraries can be found, or the application fails at startup with a library-loading error:

```bash
# Apple Silicon
export DYLD_LIBRARY_PATH=/opt/homebrew/lib
# Intel
export DYLD_LIBRARY_PATH=/usr/local/lib

dotnet build
dotnet run --project Impasto
```

Note that `DYLD_LIBRARY_PATH` has to be set in the same shell that runs the application. As an alternative, `installer/macos/build_installer.sh` patches the search path into the packaged application itself, which is how the release images work without the environment variable.