---
layout: ../../layouts/DocsLayout.astro
---

Download the installer from the [releases page](https://github.com/zbcoding/ImpastoPaint/releases/latest):

| File | For |
|---|---|
| `Impasto-win-x64.exe` | Intel and AMD 64-bit Windows. |
| `Impasto-win-arm64.exe` | Windows on ARM, such as Snapdragon X laptops. Needs Windows 11 ARM. |

Both are Inno Setup installers. The wizard asks whether to install for all users or only for you, installs into Program Files or your own profile accordingly, and adds a Start menu entry. It does not register file associations: to open an image with Impasto, use **Open with** or launch Impasto and choose **File > Open**.

Impasto needs .NET 10. The installer bundles the runtime it needs, so you do not have to install .NET separately.

## Getting past the SmartScreen warning

The Windows builds are not code-signed, so the first time you run the installer Windows shows **Windows protected your PC** with an *Unknown publisher* note. This is normal for an unsigned open-source build and does not mean the file is damaged.

1. Click **More info** in the dialog. The publisher line and a **Run anyway** button appear.
2. Click **Run anyway**.

Windows may also scan the downloaded file before it will run it, which shows as a delay rather than a dialog. The same warning appears the first time you launch the installed application, not just the installer, so expect it twice.

If your browser or antivirus blocks the download outright, choose **Keep** in the browser's download list, and add an exclusion in the antivirus for the installer. Impasto is published only through the project's own GitHub releases; do not download it from anywhere else.

## Uninstalling

**Settings > Apps > Installed apps**, find Impasto, and choose **Uninstall**. Windows removes the application but leaves your settings behind.

Your settings, keyboard shortcuts, and recent-file list live in `%APPDATA%\Impasto`. Delete that folder as well if you want to remove every trace, including your custom keyboard shortcuts. Note that unsaved work recovered after a crash is in the same folder, so check it before deleting.

## Building from source

Install [MSYS2](https://www.msys2.org), then from the **CLANG64** terminal:

```bash
pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader
```

On ARM64 Windows, use the **CLANGARM64** terminal and replace `clang-x86_64` with `clang-aarch64`.

Then install the [.NET 10 SDK](https://dotnet.microsoft.com/) and build:

```bash
dotnet build
dotnet run --project Impasto
```

You can also open `Pinta.sln` in Visual Studio; install .NET 10 through the Visual Studio installer so the project's target framework is available.