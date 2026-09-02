import type { Lang } from './languages';

export interface Dict {
  metaDescription: string;
  navScreenshots: string;
  navAbout: string;
  navGet: string;
  navDocs: string;
  heroH1: string;
  heroLede: string;
  ctaDownload: string;
  ctaSource: string;
  heroFine: string;
  screenshotsH2: string;
  shot1Alt: string;
  shot1Caption: string;
  shot2Alt: string;
  shot2Caption: string;
  shot3Alt: string;
  shot3Caption: string;
  aboutH2: string;
  aboutFeaturesP: string;
  aboutOriginPre: string;
  aboutPintaLink: string;
  aboutOriginMid: string;
  aboutPdn: string;
  aboutOriginPost: string;
  platformsH2: string;
  linuxH3: string;
  linuxPPre: string;
  linuxCode: string;
  linuxPPost: string;
  linuxCta: string;
  windowsH3: string;
  windowsP: string;
  windowsCta: string;
  macosH3: string;
  macosP: string;
  macosCta: string;
  docsH2: string;
  docsIntro: string;
  docsReadme: string;
  docsMid: string;
  docsFolder: string;
  docsMid2: string;
  docsChangelog: string;
  docsOutro: string;
  docsTooltip: string;
  footerLicense: string;
  footerGithub: string;
  footerReleases: string;
  footerIssues: string;
  aiTranslatedLabel: string;
  closeLightbox: string;
}

export const strings: Record<Lang, Dict> = {
  en: {
    metaDescription:
      'Impasto is a free, open-source paint program for Linux, Windows, and macOS. ' +
      'Layers, editable text and shapes, and snapping-aware selection tools. ' +
      'Download the Linux paint app, or grab the Flatpak-ready build.',
    navScreenshots: 'Screenshots',
    navAbout: 'About',
    navGet: 'Get it',
    navDocs: 'Docs',
    heroH1: 'A paint app for Linux, Windows, and macOS',
    heroLede:
      'Impasto is an open-source raster painting and image editor: layers, undo history, ' +
      'editable text and shapes, and grid/edge snapping while you draw.',
    ctaDownload: 'Free download from GitHub',
    ctaSource: 'View source on GitHub',
    heroFine: 'Native builds for Linux, Windows, and macOS · MIT licensed',
    screenshotsH2: 'See it in action',
    shot1Alt: 'Impasto with shapes and text kept as editable objects, each with its own undo history',
    shot1Caption: 'Shapes and text stay editable objects, with per-object history.',
    shot2Alt: 'Impasto text tool with the UI preferences dialog open',
    shot2Caption: 'The text tool, with the UI preferences dialog open.',
    shot3Alt: 'Impasto showing a shape control point snapping to a drawn guide on the canvas',
    shot3Caption: 'Drawing snaps to the grid, ruler units, canvas edges, and centre lines.',
    aboutH2: 'About Impasto',
    aboutFeaturesP:
      'Impasto is an object-based painting app: shapes, text, and effects stay editable after ' +
      'you draw them. It builds on solid layers and tool options, a faster overall workflow, ' +
      'snapping to the grid, and fully remappable keyboard shortcuts.',
    aboutOriginPre: 'It started as a GitHub fork of',
    aboutPintaLink: 'Pinta',
    aboutOriginMid: ', the MIT-licensed painting app, and was also inspired by',
    aboutPdn: 'PDN',
    aboutOriginPost: ', a Windows paint program.',
    platformsH2: 'Get Impasto',
    linuxH3: 'Linux',
    linuxPPre:
      'Prebuilt Linux binaries are on the releases page. A Flatpak manifest ships in the repo ' +
      'for building your own sandboxed',
    linuxCode: '.flatpak',
    linuxPPost: 'bundle; a Flathub listing is on the roadmap but not live yet.',
    linuxCta: 'Linux download',
    windowsH3: 'Windows',
    windowsP: 'A native Windows build is published with every release.',
    windowsCta: 'Windows download',
    macosH3: 'macOS',
    macosP: 'A native macOS build is published with every release.',
    macosCta: 'macOS download',
    docsH2: 'Documentation',
    docsIntro: 'User docs for Impasto are coming to this site soon. Until then, the',
    docsReadme: 'README',
    docsMid: ', the',
    docsFolder: 'docs folder',
    docsMid2: ', and the',
    docsChangelog: 'CHANGELOG.md',
    docsOutro: 'on GitHub cover the current feature set, file formats, and add-ins.',
    docsTooltip:
      'Impasto also has tooltips throughout the interface - hover over a button or field to see what it does.',
    footerLicense: 'Impasto is free and open source, licensed under the MIT License.',
    footerGithub: 'GitHub',
    footerReleases: 'Releases',
    footerIssues: 'Bug Reports & Issues',
    aiTranslatedLabel: 'AI-translated',
    closeLightbox: 'Close',
  },
  es: {
    metaDescription:
      'Impasto es un programa de pintura gratuito y de código abierto para Linux, Windows y macOS. ' +
      'Capas, texto y formas editables, y herramientas de selección con ajuste inteligente. ' +
      'Descarga la aplicación de pintura para Linux o la versión lista para Flatpak.',
    navScreenshots: 'Capturas',
    navAbout: 'Acerca de',
    navGet: 'Descargar',
    navDocs: 'Documentación',
    heroH1: 'Una aplicación de pintura para Linux, Windows y macOS',
    heroLede:
      'Impasto es un editor de imágenes y pintura ráster de código abierto: capas, historial de ' +
      'deshacer, texto y formas editables, y ajuste a la cuadrícula o los bordes mientras dibujas.',
    ctaDownload: 'Descarga gratis desde GitHub',
    ctaSource: 'Ver el código fuente en GitHub',
    heroFine: 'Compilaciones nativas para Linux, Windows y macOS · Licencia MIT',
    screenshotsH2: 'Míralo en acción',
    shot1Alt: 'Impasto con formas y texto mantenidos como objetos editables, cada uno con su propio historial de deshacer',
    shot1Caption: 'Las formas y el texto siguen siendo objetos editables, con historial por objeto.',
    shot2Alt: 'Herramienta de texto de Impasto con el diálogo de preferencias de interfaz abierto',
    shot2Caption: 'La herramienta de texto, con el diálogo de preferencias de interfaz abierto.',
    shot3Alt: 'Impasto mostrando un punto de control de una forma ajustándose a una guía dibujada en el lienzo',
    shot3Caption: 'El dibujo se ajusta a la cuadrícula, las unidades de la regla, los bordes del lienzo y las líneas centrales.',
    aboutH2: 'Acerca de Impasto',
    aboutFeaturesP:
      'Impasto es una aplicación de pintura basada en objetos: las formas, el texto y los efectos ' +
      'siguen siendo editables después de dibujarlos. Se apoya en capas y opciones de herramientas ' +
      'sólidas, un flujo de trabajo más rápido, ajuste a la cuadrícula y atajos de teclado ' +
      'totalmente personalizables.',
    aboutOriginPre: 'Comenzó como una bifurcación en GitHub de',
    aboutPintaLink: 'Pinta',
    aboutOriginMid: ', la aplicación de pintura con licencia MIT, y también se inspiró en',
    aboutPdn: 'PDN',
    aboutOriginPost: ', un programa de pintura de Windows.',
    platformsH2: 'Consigue Impasto',
    linuxH3: 'Linux',
    linuxPPre:
      'Los binarios precompilados para Linux están en la página de versiones. El repositorio ' +
      'incluye un manifiesto de Flatpak para compilar tu propio paquete',
    linuxCode: '.flatpak',
    linuxPPost: 'en sandbox; una publicación en Flathub está en la hoja de ruta, pero aún no está disponible.',
    linuxCta: 'Descargar para Linux',
    windowsH3: 'Windows',
    windowsP: 'Con cada versión se publica una compilación nativa para Windows.',
    windowsCta: 'Descargar para Windows',
    macosH3: 'macOS',
    macosP: 'Con cada versión se publica una compilación nativa para macOS.',
    macosCta: 'Descargar para macOS',
    docsH2: 'Documentación',
    docsIntro: 'La documentación de usuario de Impasto llegará pronto a este sitio. Mientras tanto, el',
    docsReadme: 'README',
    docsMid: ', la',
    docsFolder: 'carpeta de documentación',
    docsMid2: ', y el',
    docsChangelog: 'CHANGELOG.md',
    docsOutro: 'en GitHub describen las funciones actuales, los formatos de archivo y los complementos.',
    docsTooltip:
      'Impasto también tiene información sobre las herramientas en toda la interfaz: pasa el cursor sobre un botón o campo para ver qué hace.',
    footerLicense: 'Impasto es software libre y de código abierto, con licencia MIT.',
    footerGithub: 'GitHub',
    footerReleases: 'Versiones',
    footerIssues: 'Errores e incidencias',
    aiTranslatedLabel: 'Traducción automática',
    closeLightbox: 'Cerrar',
  },
  fr: {
    metaDescription:
      "Impasto est un logiciel de peinture gratuit et open source pour Linux, Windows et macOS. " +
      'Calques, texte et formes modifiables, et outils de sélection avec alignement intelligent. ' +
      "Téléchargez l'application de peinture pour Linux, ou la version prête pour Flatpak.",
    navScreenshots: "Captures d'écran",
    navAbout: 'À propos',
    navGet: 'Télécharger',
    navDocs: 'Docs',
    heroH1: 'Une application de peinture pour Linux, Windows et macOS',
    heroLede:
      "Impasto est un éditeur d'images et de peinture matricielle open source : calques, " +
      "historique d'annulation, texte et formes modifiables, et alignement sur la grille ou les " +
      'bords pendant que vous dessinez.',
    ctaDownload: 'Téléchargement gratuit depuis GitHub',
    ctaSource: 'Voir le code source sur GitHub',
    heroFine: 'Compilations natives pour Linux, Windows et macOS · Sous licence MIT',
    screenshotsH2: 'Voyez-le en action',
    shot1Alt: "Impasto avec des formes et du texte conservés comme des objets modifiables, chacun avec son propre historique d'annulation",
    shot1Caption: "Les formes et le texte restent des objets modifiables, avec un historique par objet.",
    shot2Alt: "Outil texte d'Impasto avec la boîte de dialogue des préférences d'interface ouverte",
    shot2Caption: "L'outil texte, avec la boîte de dialogue des préférences d'interface ouverte.",
    shot3Alt: "Impasto montrant un point de contrôle d'une forme s'alignant sur un repère tracé sur le canevas",
    shot3Caption: "Le dessin s'aligne sur la grille, les unités de la règle, les bords du canevas et les lignes centrales.",
    aboutH2: "À propos d'Impasto",
    aboutFeaturesP:
      "Impasto est un logiciel de peinture basé sur des objets : les formes, le texte et les " +
      "effets restent modifiables après le dessin. Il s'appuie sur des calques et des options " +
      "d'outils solides, un flux de travail plus rapide, un alignement sur la grille et des " +
      'raccourcis clavier entièrement personnalisables.',
    aboutOriginPre: 'Il a démarré comme un fork GitHub de',
    aboutPintaLink: 'Pinta',
    aboutOriginMid: ", le logiciel de peinture sous licence MIT, et s'est aussi inspiré de",
    aboutPdn: 'PDN',
    aboutOriginPost: ', un logiciel de peinture Windows.',
    platformsH2: 'Obtenir Impasto',
    linuxH3: 'Linux',
    linuxPPre:
      'Des binaires précompilés pour Linux sont disponibles sur la page des versions. Un manifeste ' +
      'Flatpak est fourni dans le dépôt pour créer votre propre paquet',
    linuxCode: '.flatpak',
    linuxPPost: 'en bac à sable ; une publication sur Flathub est prévue mais pas encore disponible.',
    linuxCta: 'Télécharger pour Linux',
    windowsH3: 'Windows',
    windowsP: 'Une version native pour Windows est publiée à chaque version.',
    windowsCta: 'Télécharger pour Windows',
    macosH3: 'macOS',
    macosP: 'Une version native pour macOS est publiée à chaque version.',
    macosCta: 'Télécharger pour macOS',
    docsH2: 'Documentation',
    docsIntro: "La documentation utilisateur d'Impasto arrivera bientôt sur ce site. En attendant, le",
    docsReadme: 'README',
    docsMid: ', le',
    docsFolder: 'dossier docs',
    docsMid2: ', et le',
    docsChangelog: 'CHANGELOG.md',
    docsOutro: 'sur GitHub couvrent les fonctionnalités actuelles, les formats de fichiers et les extensions.',
    docsTooltip:
      "Impasto propose aussi des infobulles dans toute l'interface : survolez un bouton ou un champ pour voir sa fonction.",
    footerLicense: 'Impasto est un logiciel libre et open source, sous licence MIT.',
    footerGithub: 'GitHub',
    footerReleases: 'Versions',
    footerIssues: 'Bugs et problèmes',
    aiTranslatedLabel: 'Traduction automatique',
    closeLightbox: 'Fermer',
  },
  de: {
    metaDescription:
      'Impasto ist ein kostenloses, quelloffenes Malprogramm für Linux, Windows und macOS. ' +
      'Ebenen, editierbarer Text und editierbare Formen sowie einrastende Auswahlwerkzeuge. ' +
      'Lade die Linux-Malsoftware herunter oder das Flatpak-fertige Build.',
    navScreenshots: 'Screenshots',
    navAbout: 'Über',
    navGet: 'Herunterladen',
    navDocs: 'Doku',
    heroH1: 'Eine Malsoftware für Linux, Windows und macOS',
    heroLede:
      'Impasto ist ein quelloffener Raster-Mal- und Bildeditor: Ebenen, Verlaufsprotokoll ' +
      '(Rückgängig), editierbarer Text und editierbare Formen sowie Ausrichtung am Raster oder ' +
      'an Kanten beim Zeichnen.',
    ctaDownload: 'Kostenloser Download von GitHub',
    ctaSource: 'Quellcode auf GitHub ansehen',
    heroFine: 'Native Builds für Linux, Windows und macOS · MIT-Lizenz',
    screenshotsH2: 'So sieht es aus',
    shot1Alt: 'Impasto mit Formen und Text als editierbare Objekte, jedes mit eigenem Verlaufsprotokoll',
    shot1Caption: 'Formen und Text bleiben editierbare Objekte, mit eigenem Verlauf pro Objekt.',
    shot2Alt: 'Impasto-Textwerkzeug mit geöffnetem Dialog für Oberflächeneinstellungen',
    shot2Caption: 'Das Textwerkzeug, mit geöffnetem Dialog für Oberflächeneinstellungen.',
    shot3Alt: 'Impasto zeigt, wie ein Kontrollpunkt einer Form an einer gezeichneten Hilfslinie auf der Leinwand einrastet',
    shot3Caption: 'Beim Zeichnen wird am Raster, an den Lineal-Einheiten, an den Leinwandkanten und an Mittellinien eingerastet.',
    aboutH2: 'Über Impasto',
    aboutFeaturesP:
      'Impasto ist eine objektbasierte Malanwendung: Formen, Text und Effekte bleiben nach dem ' +
      'Zeichnen editierbar. Es baut auf soliden Ebenen und Werkzeugoptionen, einem schnelleren ' +
      'Arbeitsablauf, Ausrichtung am Raster und vollständig anpassbaren Tastenkürzeln auf.',
    aboutOriginPre: 'Es begann als GitHub-Fork von',
    aboutPintaLink: 'Pinta',
    aboutOriginMid: ', der Malanwendung unter MIT-Lizenz, und wurde außerdem inspiriert von',
    aboutPdn: 'PDN',
    aboutOriginPost: ', einem Windows-Malprogramm.',
    platformsH2: 'Impasto herunterladen',
    linuxH3: 'Linux',
    linuxPPre:
      'Fertige Linux-Binärdateien gibt es auf der Releases-Seite. Im Repository liegt ein ' +
      'Flatpak-Manifest zum Erstellen eines eigenen, sandboxed',
    linuxCode: '.flatpak',
    linuxPPost: '-Pakets; ein Eintrag auf Flathub ist geplant, aber noch nicht verfügbar.',
    linuxCta: 'Linux-Download',
    windowsH3: 'Windows',
    windowsP: 'Mit jedem Release wird ein natives Windows-Build veröffentlicht.',
    windowsCta: 'Windows-Download',
    macosH3: 'macOS',
    macosP: 'Mit jedem Release wird ein natives macOS-Build veröffentlicht.',
    macosCta: 'macOS-Download',
    docsH2: 'Dokumentation',
    docsIntro: 'Nutzerdokumentation für Impasto folgt bald auf dieser Seite. Bis dahin decken das',
    docsReadme: 'README',
    docsMid: ', der',
    docsFolder: 'Docs-Ordner',
    docsMid2: ' und das',
    docsChangelog: 'CHANGELOG.md',
    docsOutro: 'auf GitHub den aktuellen Funktionsumfang, die Dateiformate und die Add-ins ab.',
    docsTooltip:
      'Impasto zeigt außerdem in der gesamten Oberfläche Tooltips: Cursor über eine Schaltfläche oder ein Feld halten, um ihre Funktion zu sehen.',
    footerLicense: 'Impasto ist freie und quelloffene Software unter der MIT-Lizenz.',
    footerGithub: 'GitHub',
    footerReleases: 'Releases',
    footerIssues: 'Fehler & Probleme',
    aiTranslatedLabel: 'Maschinelle Übersetzung',
    closeLightbox: 'Schließen',
  },
  ja: {
    metaDescription:
      'ImpastoはLinux、Windows、macOS向けの無料・オープンソースのペイントソフトです。' +
      'レイヤー、編集可能なテキストと図形、スナップ対応の選択ツールを備えています。' +
      'Linux版ペイントアプリ、またはFlatpak対応ビルドをダウンロードできます。',
    navScreenshots: 'スクリーンショット',
    navAbout: '概要',
    navGet: 'ダウンロード',
    navDocs: 'ドキュメント',
    heroH1: 'Linux、Windows、macOS向けのペイントアプリ',
    heroLede:
      'Impastoはオープンソースのラスターペイント・画像編集ソフトです。レイヤー、undo履歴、' +
      '編集可能なテキストと図形、描画中のグリッド/エッジへのスナップに対応しています。',
    ctaDownload: 'GitHubから無料ダウンロード',
    ctaSource: 'GitHubでソースコードを見る',
    heroFine: 'Linux、Windows、macOS向けのネイティブビルド ・ MITライセンス',
    screenshotsH2: '実際の画面',
    shot1Alt: 'Impastoで図形とテキストが編集可能なオブジェクトとして保持され、それぞれ独自のundo履歴を持つ様子',
    shot1Caption: '図形とテキストは編集可能なオブジェクトのままで、オブジェクトごとに履歴を持ちます。',
    shot2Alt: 'UI設定ダイアログを開いた状態のImpastoのテキストツール',
    shot2Caption: 'UI設定ダイアログを開いた状態のテキストツール。',
    shot3Alt: 'Impastoで図形のコントロールポイントがキャンバス上に描かれたガイドにスナップする様子',
    shot3Caption: '描画はグリッド、定規の単位、キャンバスの端、中心線にスナップします。',
    aboutH2: 'Impastoについて',
    aboutFeaturesP:
      'Impastoはオブジェクトベースのペイントアプリで、図形・テキスト・エフェクトは描画後も' +
      '編集可能なままです。しっかりしたレイヤーとツールオプション、より高速な全体的なワークフロー、' +
      'グリッドへのスナップ、そして完全にカスタマイズ可能なキーボードショートカットを備えています。',
    aboutOriginPre: 'GitHub上で',
    aboutPintaLink: 'Pinta',
    aboutOriginMid: '(MITライセンスのペイントアプリ)のフォークとして始まり、',
    aboutPdn: 'PDN',
    aboutOriginPost: '(Windows向けペイントソフト)からも着想を得ています。',
    platformsH2: 'Impastoを入手する',
    linuxH3: 'Linux',
    linuxPPre:
      'ビルド済みのLinuxバイナリはリリースページにあります。リポジトリにはFlatpakマニフェストが含ま' +
      'れており、サンドボックス化された',
    linuxCode: '.flatpak',
    linuxPPost: 'バンドルを自分でビルドできます。Flathubへの登録は予定していますが、まだ公開されていません。',
    linuxCta: 'Linux版をダウンロード',
    windowsH3: 'Windows',
    windowsP: 'リリースごとにネイティブのWindowsビルドが公開されます。',
    windowsCta: 'Windows版をダウンロード',
    macosH3: 'macOS',
    macosP: 'リリースごとにネイティブのmacOSビルドが公開されます。',
    macosCta: 'macOS版をダウンロード',
    docsH2: 'ドキュメント',
    docsIntro: 'Impastoのユーザードキュメントは近日中にこのサイトに追加予定です。それまでは、',
    docsReadme: 'README',
    docsMid: '、',
    docsFolder: 'docsフォルダ',
    docsMid2: '、',
    docsChangelog: 'CHANGELOG.md',
    docsOutro: '(GitHub上)に、現在の機能一覧、ファイル形式、アドインについての説明があります。',
    docsTooltip:
      'Impastoの各所にはツールチップも用意されており、ボタンや項目にカーソルを合わせると機能の説明が表示されます。',
    footerLicense: 'ImpastoはMITライセンスの下で提供される、無料でオープンソースのソフトウェアです。',
    footerGithub: 'GitHub',
    footerReleases: 'リリース',
    footerIssues: 'バグ報告・Issue',
    aiTranslatedLabel: 'AI翻訳',
    closeLightbox: '閉じる',
  },
  'zh-cn': {
    metaDescription:
      'Impasto 是一款适用于 Linux、Windows 和 macOS 的免费开源绘图软件。' +
      '支持图层、可编辑文字和形状,以及智能吸附的选择工具。' +
      '立即下载 Linux 版绘图软件,或获取 Flatpak 就绪构建版本。',
    navScreenshots: '截图',
    navAbout: '关于',
    navGet: '下载',
    navDocs: '文档',
    heroH1: '适用于 Linux、Windows 和 macOS 的绘图软件',
    heroLede:
      'Impasto 是一款开源的位图绘画与图像编辑软件:支持图层、撤销历史、可编辑的文字和形状,' +
      '并在绘制时自动对齐网格与边缘。',
    ctaDownload: '从 GitHub 免费下载',
    ctaSource: '在 GitHub 上查看源代码',
    heroFine: '提供 Linux、Windows 和 macOS 原生构建版本 · MIT 许可证',
    screenshotsH2: '实际效果',
    shot1Alt: 'Impasto 中形状和文字保持为可编辑对象,各自拥有独立的撤销历史',
    shot1Caption: '形状和文字始终是可编辑对象,并按对象单独记录历史。',
    shot2Alt: '打开界面偏好设置对话框时的 Impasto 文字工具',
    shot2Caption: '文字工具,及其打开的界面偏好设置对话框。',
    shot3Alt: 'Impasto 中形状的控制点吸附到画布上绘制的参考线',
    shot3Caption: '绘制内容会自动吸附到网格、标尺单位、画布边缘和中心线。',
    aboutH2: '关于 Impasto',
    aboutFeaturesP:
      'Impasto 是一款基于对象的绘画软件:形状、文字和效果在绘制后仍可编辑。它拥有扎实的图层与' +
      '工具选项、更快的整体工作流程、网格吸附功能,以及可完全自定义的键盘快捷键。',
    aboutOriginPre: '它最初是在 GitHub 上作为',
    aboutPintaLink: 'Pinta',
    aboutOriginMid: '(一款 MIT 许可的绘画软件)的分支起步,同时也受到',
    aboutPdn: 'PDN',
    aboutOriginPost: '(一款 Windows 绘图软件)的启发。',
    platformsH2: '获取 Impasto',
    linuxH3: 'Linux',
    linuxPPre: '预编译的 Linux 二进制文件可在发布页面获取。仓库中附带 Flatpak 清单文件,供你自行构建沙盒化的',
    linuxCode: '.flatpak',
    linuxPPost: '软件包;Flathub 上架计划中,但目前尚未上线。',
    linuxCta: '下载 Linux 版',
    windowsH3: 'Windows',
    windowsP: '每次发布都会提供原生 Windows 构建版本。',
    windowsCta: '下载 Windows 版',
    macosH3: 'macOS',
    macosP: '每次发布都会提供原生 macOS 构建版本。',
    macosCta: '下载 macOS 版',
    docsH2: '文档',
    docsIntro: 'Impasto 的用户文档即将上线本站。在此之前,可参阅',
    docsReadme: 'README',
    docsMid: '、',
    docsFolder: 'docs 文件夹',
    docsMid2: '和',
    docsChangelog: 'CHANGELOG.md',
    docsOutro: '(位于 GitHub),其中介绍了当前的功能集、文件格式和插件。',
    docsTooltip: 'Impasto 界面中还有大量工具提示,将鼠标悬停在按钮或输入框上即可查看其作用。',
    footerLicense: 'Impasto 是遵循 MIT 许可证的免费开源软件。',
    footerGithub: 'GitHub',
    footerReleases: '发布版本',
    footerIssues: 'Bug 反馈与 Issues',
    aiTranslatedLabel: 'AI 翻译',
    closeLightbox: '关闭',
  },
};
