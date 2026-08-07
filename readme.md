# Impasto

[![Build Status](https://github.com/zbcoding/Impasto/workflows/Build/badge.svg)](https://github.com/zbcoding/Impasto/actions)

Impasto is a fork of [Pinta](https://github.com/PintaProject/Pinta) — a GTK clone of
[Paint.NET](https://www.getpaint.net/) — that adopts Paint.NET's interface. It runs on
Linux, Windows, and macOS.

Impasto is licensed under the MIT License (see `license-mit.txt`). Third-party
attributions, notices, and license texts are in `THIRD-PARTY-NOTICES.md`.

## Building on Windows

First, install the required GTK-related dependencies:
- Install [MSYS2](https://www.msys2.org)
- From the CLANG64 terminal, run `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader`.
  - For ARM64 Windows, use the `CLANGARM64` terminal and replace `clang-x86_64` with `clang-aarch64`.

The application can then be built by opening `Pinta.sln` in [Visual Studio](https://visualstudio.microsoft.com/).
Ensure that .NET 10 is installed via the Visual Studio installer.

For building on the command line:
- [Install the .NET 10 SDK](https://dotnet.microsoft.com/).
- Build:
  - `dotnet build`
- Run:
  - `dotnet run --project Pinta`

## Building on macOS

- Install .NET 10 and GTK4
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - For Apple Silicon, set `DYLD_LIBRARY_PATH=/opt/homebrew/lib` in the environment so that the application can load the GTK libraries
  - For Intel, set `DYLD_LIBRARY_PATH=/usr/local/lib` in the environment so that the application can load the GTK libraries
- Build:
  - `dotnet build`
- Run:
  - `dotnet run --project Pinta`

## Building on Linux

- Install [.NET 10](https://dotnet.microsoft.com/) following the instructions for your Linux distribution.
- Install other dependencies (instructions are for Ubuntu 22.10, but should be similar for other distros):
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - Minimum library versions: `gtk` >= 4.18 and `libadwaita` >= 1.8
  - Optional dependencies: `webp-pixbuf-loader`
- Build (option 1, for development and testing):
  - `dotnet build`
  - `dotnet run --project Pinta`
- Build (option 2, for installation):
  - `./autogen.sh`
    - If building from a tarball, run `./configure` instead.
    - Add the `--prefix=<install directory>` argument to install to a directory other than `/usr/local`.
  - `make install`

## Getting help / contributing:

Contributions are welcome. In short:

- **Code** — Contributing to the free edition (MIT-licensed) is encouraged; a
  contribution received as a PR to the free edition may also be used in the
  Premium edition of Impasto.
- **AI coding tools** — welcome. Be able to explain the code you submit and
  adapt it to the project's architecture; AI-assisted code may draw extra
  review before it's merged.
- **Translations** — contributed as pull requests: AI-drafted in new `.po`
  files, then edited and proofread by a language speaker.

The full guide, including the git/PR workflow, is in `CONTRIBUTING.md`.

- You can report [bugs/issues](https://github.com/zbcoding/Impasto/issues).
- You can make [suggestions](https://github.com/zbcoding/Impasto/discussions).
- You can fork the project on [GitHub](https://github.com/zbcoding/Impasto).
- Notable changes of each release are recorded in `CHANGELOG.md`.

## Privacy policy

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it.
