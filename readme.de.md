# Impasto

[🇬🇧 English](readme.md) · Maschinelle Übersetzung — Korrekturen sind als Pull Request willkommen.

Impasto ist eine Anwendung zum Malen und Bearbeiten von Bildern für Linux, Windows und macOS.

Impasto eignet sich hervorragend zum schnellen Malen, für Bildbearbeitungen, Zuschnitte, Größenänderungen und ebenenbasiertes Arbeiten.
Mit den Textwerkzeugen schreiben Sie Text, mit den Formwerkzeugen zeichnen Sie freie Linien, und mit den Auswahlwerkzeugen kopieren und fügen Sie Teile einer Bildebene ein.
Sehen Sie sich die Screenshots unten an.

![Formen und Text als bearbeitbare Objekte, mit eigener Verlaufsgeschichte pro Objekt](docs/screenshots/impasto-object-layers.png)

![Das Textwerkzeug bei geöffnetem Dialog für die Oberflächeneinstellungen](docs/screenshots/impasto-text-tool.png)

![Ein Kontrollpunkt einer Form, der auf der Mittellinie der Leinwand gehalten wird, mit der beim Ziehen eingeblendeten Hilfslinie](docs/screenshots/impasto-snap-to-grid.png)
Titelbild: Was Sie verschieben und was Sie zeichnen, rastet am Raster der Leinwand, an den Linealeinheiten oder an den Rändern und Mittellinien der Leinwand ein.

## Download

Builds für Linux, Windows und macOS finden Sie auf der
[Release-Seite](https://github.com/zbcoding/ImpastoPaint/releases/latest). Die Builds für Windows und
macOS sind nicht signiert, daher warnen SmartScreen und Gatekeeper davor.

Impasto benötigt GTK 4.18 oder neuer, was darüber entscheidet, was unter Linux funktioniert:

- **Flatpak**, von [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/) oder von der
  Release-Seite. Es bringt sein eigenes GTK mit und läuft überall dort, wo flatpak läuft, auch unter Ubuntu 22.04
  und 24.04. Das ist die richtige Wahl, wenn Sie unsicher sind.
- **AppImage** (`Impasto-x86_64.AppImage`). Bündelt GTK, ein System-GTK ist also nicht nötig, aber das
  Bündel ist gegen **glibc 2.43** gebaut und startet auf nichts Älterem. Das heißt:
  Ubuntu 26.04, Fedora 44 und Arch funktionieren; Ubuntu 24.04, Ubuntu 22.04, Fedora 43 und Debian 13
  funktionieren nicht – nehmen Sie stattdessen das Flatpak.
- **Zip** (`Impasto-linux-dotnet-*.zip`). Nutzt das GTK Ihrer Distribution und setzt daher
  ein bereits installiertes GTK 4.18+ voraus.

Impasto steht unter der MIT-Lizenz (siehe `license-mit.txt`). Nennungen, Hinweise und
Lizenztexte Dritter stehen in `THIRD-PARTY-NOTICES.md`.

## Verhältnis zu Pinta

Impasto ist ein neues, eigenständiges Projekt. Es ist aus dem MIT-lizenzierten Quellcode von
[Pinta](https://github.com/PintaProject/Pinta) hervorgegangen — einer GTK-Anwendung, die ihrerseits von
[Paint.NET™](https://www.getpaint.net/) inspiriert ist.
Die Mitwirkenden von Pinta sind in der Impasto-Anwendung aufgeführt.
Impasto wird nicht vom Pinta-Projekt oder denselben Mitwirkenden betreut.

<details>
<summary><h2>Erstellen unter Windows</h2></summary>

Installieren Sie zuerst die benötigten GTK-Abhängigkeiten:
- Installieren Sie [MSYS2](https://www.msys2.org)
- Führen Sie im CLANG64-Terminal `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader` aus.
  - Für Windows auf ARM64 verwenden Sie das `CLANGARM64`-Terminal und ersetzen `clang-x86_64` durch `clang-aarch64`.

Anschließend lässt sich die Anwendung erstellen, indem Sie `Pinta.sln` in [Visual Studio](https://visualstudio.microsoft.com/) öffnen.
Stellen Sie sicher, dass .NET 10 über den Visual Studio Installer installiert ist.

Zum Erstellen auf der Kommandozeile:
- [Installieren Sie das .NET 10 SDK](https://dotnet.microsoft.com/).
- Erstellen:
  - `dotnet build`
- Ausführen:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Erstellen unter macOS</h2></summary>

- Installieren Sie .NET 10 und GTK4
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - Für Apple Silicon setzen Sie `DYLD_LIBRARY_PATH=/opt/homebrew/lib` in der Umgebung, damit die Anwendung die GTK-Bibliotheken laden kann
  - Für Intel setzen Sie `DYLD_LIBRARY_PATH=/usr/local/lib` in der Umgebung, damit die Anwendung die GTK-Bibliotheken laden kann
- Erstellen:
  - `dotnet build`
- Ausführen:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Erstellen unter Linux</h2></summary>

- Installieren Sie [.NET 10](https://dotnet.microsoft.com/) gemäß der Anleitung für Ihre Linux-Distribution.
- Installieren Sie die weiteren Abhängigkeiten (die Anleitung gilt für Ubuntu 22.10, sollte aber für andere Distributionen ähnlich sein):
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - Mindestversionen der Bibliotheken: `gtk` >= 4.18 und `libadwaita` >= 1.8
  - Optionale Abhängigkeiten: `webp-pixbuf-loader`
- Erstellen (Variante 1, für Entwicklung und Tests):
  - `dotnet build`
  - `dotnet run --project Impasto`
- Erstellen (Variante 2, für die Installation):
  - `./autogen.sh`
    - Wenn Sie aus einem Tarball bauen, führen Sie stattdessen `./configure` aus.
    - Fügen Sie das Argument `--prefix=<Installationsverzeichnis>` hinzu, um in ein anderes Verzeichnis als `/usr/local` zu installieren.
  - `make install`

</details>

## Hilfe erhalten / mitwirken:

Beiträge sind willkommen. Kurz gefasst:

- **Code** — Am schnellsten tragen Sie bei, indem Sie ein Issue eröffnen und Ihren Vorschlag
  oder Wunsch beschreiben. Fügen Sie Codeausschnitte, Dateinamen und Kontext hinzu.
  Sie können auch einen PR einreichen. Beiträge, die als PR zur kostenlosen Edition eingehen,
  können auch in der Premium-Edition von Impasto oder in jeder anderen Software unter der MIT-Lizenz verwendet werden.
- **KI-Programmierwerkzeuge** — willkommen. Sie sollten den eingereichten Code erklären und
  an die Architektur des Projekts anpassen können; KI-gestützter Code kann vor dem Merge
  zusätzlich geprüft werden.
- **Übersetzungen** — werden als Pull Request beigetragen: mit KI in neuen `.po`-Dateien
  entworfen, danach von einem Sprecher der Sprache überarbeitet und Korrektur gelesen.

Lesen Sie vor einem Beitrag den vollständigen Leitfaden einschließlich des git-/PR-Workflows in `CONTRIBUTING.md`.

- Sie können [Fehler/Probleme](https://github.com/zbcoding/ImpastoPaint/issues) melden.
- Bemerkenswerte Änderungen jeder Version werden in `CHANGELOG.md` festgehalten.
