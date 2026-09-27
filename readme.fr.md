# Impasto

[🇬🇧 English](readme.md) · Traduction automatique — les corrections sont les bienvenues sous forme de pull request.

Impasto est une application de peinture et de retouche d'images pour Linux, Windows et macOS.

Impasto est idéal pour peindre rapidement, retoucher des images, recadrer, redimensionner et travailler par calques.
Utilisez les outils de texte pour écrire, les outils de forme pour tracer des lignes à main levée, et les outils de sélection pour copier et coller des parties d'un calque.
Voir les captures d'écran ci-dessous.

![Formes et textes en tant qu'objets modifiables, avec un historique par objet](docs/screenshots/impasto-object-layers.png)

![L'outil texte, avec la boîte de dialogue des préférences d'interface ouverte](docs/screenshots/impasto-text-tool.png)

![Un point de contrôle de forme maintenu sur l'axe central du canevas, avec le guide affiché pendant le déplacement](docs/screenshots/impasto-snap-to-grid.png)
Image de présentation : ce que vous déplacez et ce que vous dessinez s'aligne sur la grille du canevas, sur les unités de la règle, ou sur les bords et les axes centraux du canevas.

## Téléchargement

Les versions compilées pour Linux, Windows et macOS sont disponibles sur la
[page des versions](https://github.com/zbcoding/ImpastoPaint/releases/latest). Les versions Windows et
macOS ne sont pas signées, SmartScreen et Gatekeeper afficheront donc un avertissement.

Impasto nécessite GTK 4.18 ou plus récent, ce qui détermine ce qui fonctionne sous Linux :

- **Flatpak**, depuis [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/) ou la
  page des versions. Il embarque sa propre version de GTK et fonctionne partout où flatpak est
  disponible, y compris sur Ubuntu 22.04 et 24.04. C'est l'option à choisir en cas de doute.
- **AppImage** (`Impasto-x86_64.AppImage`). Embarque GTK, aucun GTK système n'est donc nécessaire,
  mais le paquet est compilé avec la **glibc 2.43** et ne démarrera pas sur une version plus
  ancienne. Autrement dit, Ubuntu 26.04, Fedora 44 et Arch fonctionnent ; Ubuntu 24.04,
  Ubuntu 22.04, Fedora 43 et Debian 13 non - préférez le Flatpak.
- **Zip** (`Impasto-linux-dotnet-*.zip`). Utilise le GTK fourni par votre distribution, il faut
  donc que GTK 4.18+ soit déjà installé.

Impasto est distribué sous licence MIT (voir `license-mit.txt`). Les attributions,
mentions et textes de licence de tiers se trouvent dans `THIRD-PARTY-NOTICES.md`.

## Relation avec Pinta

Impasto est un projet nouveau et distinct. Il est parti du code source sous licence MIT de
[Pinta](https://github.com/PintaProject/Pinta) — une application GTK elle-même inspirée de
[Paint.NET™](https://www.getpaint.net/)
Les contributeurs de Pinta sont listés dans l'application Impasto.
Impasto n'est pas maintenu par le projet Pinta ni par les mêmes contributeurs.

<details>
<summary><h2>Compilation sous Windows</h2></summary>

Commencez par installer les dépendances liées à GTK :
- Installez [MSYS2](https://www.msys2.org)
- Depuis le terminal CLANG64, exécutez `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader`.
  - Pour Windows ARM64, utilisez le terminal `CLANGARM64` et remplacez `clang-x86_64` par `clang-aarch64`.

L'application peut ensuite être compilée en ouvrant `Pinta.sln` dans [Visual Studio](https://visualstudio.microsoft.com/).
Assurez-vous que .NET 10 est installé via l'installateur de Visual Studio.

Pour compiler en ligne de commande :
- [Installez le SDK .NET 10](https://dotnet.microsoft.com/).
- Compiler :
  - `dotnet build`
- Exécuter :
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Compilation sous macOS</h2></summary>

- Installez .NET 10 et GTK4
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - Pour Apple Silicon, définissez `DYLD_LIBRARY_PATH=/opt/homebrew/lib` dans l'environnement afin que l'application puisse charger les bibliothèques GTK
  - Pour Intel, définissez `DYLD_LIBRARY_PATH=/usr/local/lib` dans l'environnement afin que l'application puisse charger les bibliothèques GTK
- Compiler :
  - `dotnet build`
- Exécuter :
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Compilation sous Linux</h2></summary>

- Installez [.NET 10](https://dotnet.microsoft.com/) en suivant les instructions correspondant à votre distribution Linux.
- Installez les autres dépendances (les instructions valent pour Ubuntu 22.10, mais devraient être similaires sur les autres distributions) :
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - Versions minimales des bibliothèques : `gtk` >= 4.18 et `libadwaita` >= 1.8
  - Dépendances optionnelles : `webp-pixbuf-loader`
- Compilation (option 1, pour le développement et les tests) :
  - `dotnet build`
  - `dotnet run --project Impasto`
- Compilation (option 2, pour l'installation) :
  - `./autogen.sh`
    - Si vous compilez depuis une archive tarball, exécutez `./configure` à la place.
    - Ajoutez l'argument `--prefix=<répertoire d'installation>` pour installer dans un répertoire autre que `/usr/local`.
  - `make install`

</details>

## Obtenir de l'aide / contribuer :

Les contributions sont les bienvenues. En résumé :

- **Code** — le moyen le plus rapide de contribuer est d'ouvrir un ticket décrivant votre suggestion
  ou votre demande. Incluez des extraits de code, des noms de fichiers et du contexte.
  Vous pouvez aussi proposer une PR. Les contributions reçues sous forme de PR pour l'édition gratuite
  peuvent également être utilisées dans l'édition Premium d'Impasto ou dans tout autre logiciel sous licence MIT.
- **Outils de codage par IA** — bienvenus. Soyez en mesure d'expliquer le code que vous soumettez et
  de l'adapter à l'architecture du projet ; le code assisté par IA peut faire l'objet d'une
  relecture supplémentaire avant d'être fusionné.
- **Traductions** — proposées sous forme de pull requests : ébauchées par IA dans de nouveaux
  fichiers `.po`, puis corrigées et relues par une personne parlant la langue.

Avant de contribuer, lisez le guide complet, y compris le flux de travail git/PR, dans `CONTRIBUTING.md`.

- Vous pouvez signaler des [bogues/problèmes](https://github.com/zbcoding/ImpastoPaint/issues).
- Les changements notables de chaque version sont consignés dans `CHANGELOG.md`.
