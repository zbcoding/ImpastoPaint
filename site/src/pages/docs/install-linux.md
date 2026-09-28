---
layout: ../../layouts/DocsLayout.astro
---

Impasto needs GTK 4.18 or newer, which is what decides which of the three Linux builds will start on your distribution. All three are on the [releases page](https://github.com/zbcoding/ImpastoPaint/releases/latest).

| Build | Bundles GTK | Runs on |
|---|---|---|
| Flatpak (`Impasto-x86_64.flatpak`) | Yes | Anything that runs Flatpak, including Ubuntu 22.04 and 24.04. |
| AppImage (`Impasto-x86_64.AppImage`) | Yes | Distributions with glibc 2.43 or newer: Ubuntu 26.04, Fedora 44, Arch. |
| Zip (`Impasto-linux-dotnet-10.0.x.zip`) | No, uses your system's | Distributions that already have GTK 4.18+ installed. |

If you are unsure which to take, take the Flatpak. It carries its own GTK and therefore works on the widest range of distributions.

## Flatpak

The Flatpak is published under the app id `com.github.zbcoding.Impasto` and distributed through [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/), not Flathub. Open the FlatPark page and use its install instructions.

To install the release file directly instead, download `Impasto-x86_64.flatpak` from the releases page and run:

```bash
flatpak install ./Impasto-x86_64.flatpak
```

Then start Impasto from your application menu, or with `flatpak run com.github.zbcoding.Impasto`. Updates via FlatPark arrive with `flatpak update`.

The Flatpak is fully self-contained: it bundles the .NET runtime it needs, so no extra runtime extension has to be installed.

## AppImage

The AppImage is a single file you run directly. It bundles GTK, so it does not need GTK installed on your system - but it does inherit the glibc version of the machine that built it, which is reported as **glibc 2.43** on every release. That is the floor for the whole image:

- Works: Ubuntu 26.04, Fedora 44, Arch, and anything newer.
- Does not work: Ubuntu 24.04, Ubuntu 22.04, Fedora 43, Debian 13. Take the Flatpak on those.

Download `Impasto-x86_64.AppImage`, then:

```bash
chmod +x Impasto-x86_64.AppImage
./Impasto-x86_64.AppImage
```

If it fails to start with an error about FUSE, your system has no `libfuse2`. Either install it, or run the image without mounting it:

```bash
./Impasto-x86_64.AppImage --appimage-extract-and-run
```

The AppImage does not install desktop entries or icons; it is the one to use when you want a single file to try or to carry around.

## Zip

`Impasto-linux-dotnet-10.0.x.zip` is the plain build. It does not bundle GTK, so it uses the GTK your distribution provides and needs **GTK 4.18 or newer** with **libadwaita 1.8 or newer** already installed.

```bash
unzip Impasto-linux-dotnet-10.0.x.zip -d impasto
cd impasto
./Impasto
```

On a distribution older than that, or one whose GTK is older, the application fails at startup with a missing-symbol error. Either update GTK, or use the Flatpak or AppImage instead, which package their own.

## Building from source

Impasto builds with the .NET 10 SDK. On a Debian-based distribution:

```bash
sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev
git clone https://github.com/zbcoding/ImpastoPaint
cd ImpastoPaint
dotnet build
dotnet run --project Impasto
```

Optional dependency: `webp-pixbuf-loader`, which lets the system image loader decode WebP files.

To install it system-wide through the Autotools recipe:

```bash
./autogen.sh          # ./configure instead, if you are building from a release tarball
make install          # add --prefix=<dir> to ./configure to install somewhere other than /usr/local
```

## Uninstalling

- Flatpak: `flatpak uninstall com.github.zbcoding.Impasto`
- AppImage or zip: delete the file or the folder you unpacked it into. Settings and recent files stay in `~/.config/Impasto`; delete that too if you want a clean slate.