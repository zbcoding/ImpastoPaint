# Impasto

[🇬🇧 English](readme.md) · Traduzione automatica — le correzioni sono benvenute tramite pull request.

Impasto è un'applicazione di disegno e fotoritocco per Linux, Windows e macOS.

Impasto è ideale per disegnare rapidamente, modificare immagini, ritagliare, ridimensionare e lavorare su livelli.
Usa gli strumenti di testo per scrivere, gli strumenti forma per tracciare linee a mano libera e gli strumenti di selezione per copiare e incollare porzioni di un livello dell'immagine.
Guarda le schermate qui sotto.

![Forme e testo come oggetti modificabili, con una cronologia per ogni oggetto](docs/screenshots/impasto-object-layers.png)

![Lo strumento testo, con la finestra di dialogo delle preferenze dell'interfaccia aperta](docs/screenshots/impasto-text-tool.png)

![Un punto di controllo di una forma mantenuto sulla linea centrale dell'area di disegno, con la guida tracciata durante il trascinamento](docs/screenshots/impasto-snap-to-grid.png)
Immagine in evidenza: ciò che sposti e ciò che disegni si agganciano alla griglia dell'area di disegno, alle unità del righello oppure ai bordi e alle linee centrali dell'area di disegno.

## Download

Le versioni compilate per Linux, Windows e macOS si trovano nella
[pagina dei rilasci](https://github.com/zbcoding/ImpastoPaint/releases/latest). Le versioni per Windows e
macOS non sono firmate, quindi SmartScreen e Gatekeeper mostreranno un avviso.

Impasto richiede GTK 4.18 o versioni successive, e questo determina cosa funziona su Linux:

- **Flatpak**, da [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/) oppure dalla
  pagina dei rilasci. Include la propria copia di GTK e funziona dove funziona flatpak, comprese Ubuntu 22.04
  e 24.04. È la scelta da preferire in caso di dubbi.
- **AppImage** (`Impasto-x86_64.AppImage`). Include GTK, quindi non serve il GTK di sistema, ma il
  pacchetto è compilato con **glibc 2.43** e non si avvia su versioni più vecchie. Ciò significa che
  Ubuntu 26.04, Fedora 44 e Arch funzionano; Ubuntu 24.04, Ubuntu 22.04, Fedora 43 e Debian 13
  no - in questi casi scegli il Flatpak.
- **Zip** (`Impasto-linux-dotnet-*.zip`). Usa il GTK fornito dalla tua distribuzione, quindi richiede
  che GTK 4.18+ sia già installato.

Impasto è distribuito con licenza MIT (vedi `license-mit.txt`). Le attribuzioni,
le note e i testi di licenza di terze parti sono in `THIRD-PARTY-NOTICES.md`.

## Rapporto con Pinta

Impasto è un progetto nuovo e indipendente. È nato dal codice sorgente sotto licenza MIT di
[Pinta](https://github.com/PintaProject/Pinta) — un'applicazione GTK a sua volta ispirata a
[Paint.NET™](https://www.getpaint.net/)
I contributori di Pinta sono elencati nell'applicazione Impasto.
Impasto non è mantenuto dal progetto Pinta né dagli stessi contributori.

<details>
<summary><h2>Compilazione su Windows</h2></summary>

Per prima cosa, installa le dipendenze necessarie legate a GTK:
- Installa [MSYS2](https://www.msys2.org)
- Dal terminale CLANG64, esegui `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader`.
  - Per Windows su ARM64, usa il terminale `CLANGARM64` e sostituisci `clang-x86_64` con `clang-aarch64`.

L'applicazione può poi essere compilata aprendo `Pinta.sln` in [Visual Studio](https://visualstudio.microsoft.com/).
Assicurati che .NET 10 sia installato tramite il programma di installazione di Visual Studio.

Per compilare da riga di comando:
- [Installa l'SDK di .NET 10](https://dotnet.microsoft.com/).
- Compilazione:
  - `dotnet build`
- Esecuzione:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Compilazione su macOS</h2></summary>

- Installa .NET 10 e GTK4
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - Su Apple Silicon, imposta `DYLD_LIBRARY_PATH=/opt/homebrew/lib` nell'ambiente affinché l'applicazione possa caricare le librerie GTK
  - Su Intel, imposta `DYLD_LIBRARY_PATH=/usr/local/lib` nell'ambiente affinché l'applicazione possa caricare le librerie GTK
- Compilazione:
  - `dotnet build`
- Esecuzione:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Compilazione su Linux</h2></summary>

- Installa [.NET 10](https://dotnet.microsoft.com/) seguendo le istruzioni per la tua distribuzione Linux.
- Installa le altre dipendenze (le istruzioni si riferiscono a Ubuntu 22.10, ma dovrebbero essere simili per le altre distribuzioni):
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - Versioni minime delle librerie: `gtk` >= 4.18 e `libadwaita` >= 1.8
  - Dipendenze opzionali: `webp-pixbuf-loader`
- Compilazione (opzione 1, per sviluppo e test):
  - `dotnet build`
  - `dotnet run --project Impasto`
- Compilazione (opzione 2, per l'installazione):
  - `./autogen.sh`
    - Se compili da un tarball, esegui `./configure` al suo posto.
    - Aggiungi l'argomento `--prefix=<install directory>` per installare in una directory diversa da `/usr/local`.
  - `make install`

</details>

## Ottenere aiuto / contribuire:

I contributi sono benvenuti. In sintesi:

- **Codice** — il modo più rapido per contribuire è aprire una issue descrivendo il tuo suggerimento
  o la tua richiesta. Includi frammenti di codice, nomi dei file e contesto.
  Puoi anche inviare una PR. I contributi ricevuti come PR per l'edizione gratuita
  possono essere usati anche nell'edizione Premium di Impasto o in qualsiasi altro software con licenza MIT.
- **Strumenti di programmazione basati su IA** — sono benvenuti. Devi essere in grado di spiegare il codice che invii e
  di adattarlo all'architettura del progetto; il codice scritto con l'aiuto dell'IA può richiedere una
  revisione più approfondita prima di essere integrato.
- **Traduzioni** — si contribuiscono tramite pull request: una prima stesura generata dall'IA in nuovi file
  `.po`, poi rivista e corretta da chi parla la lingua.

Prima di contribuire, leggi la guida completa, compreso il flusso di lavoro git/PR, in `CONTRIBUTING.md`.

- Puoi segnalare [bug/problemi](https://github.com/zbcoding/ImpastoPaint/issues).
- Le modifiche rilevanti di ogni rilascio sono riportate in `CHANGELOG.md`.
