/**
 * The documentation table of contents. This is the single source of truth for the docs
 * sidebar, the docs index cards, the previous/next links, and the breadcrumb trail, so a
 * new page only has to be listed here once.
 *
 * Docs are English-only; the localized pages under /es, /fr, ... are the marketing page.
 */
export interface DocsPage {
  /** Path relative to the site base, always with a trailing slash. */
  path: string;
  /** Sidebar label — short. */
  label: string;
  /** <h1> and <title> subject — the full, search-facing name. */
  title: string;
  /** Meta description and index-card blurb. */
  blurb: string;
}

export interface DocsSection {
  label: string;
  pages: DocsPage[];
}

export const docsRoot = '/docs/';

export const docsSections: DocsSection[] = [
  {
    label: 'Start here',
    pages: [
      {
        path: '/docs/getting-started/',
        label: 'Getting started',
        title: 'Getting started with Impasto',
        blurb:
          'Install Impasto on Linux, Windows, or macOS, open your first image, and learn the ' +
          'parts of the window: canvas, toolbox, tool options, and the docked pads.',
      },
      {
        path: '/docs/install-linux/',
        label: 'Install on Linux',
        title: 'Install Impasto on Linux',
        blurb:
          'Flatpak, AppImage, and portable zip builds of the Impasto paint app for Linux, with ' +
          'the GTK 4.18 and glibc requirements that decide which one you want.',
      },
      {
        path: '/docs/install-windows/',
        label: 'Install on Windows',
        title: 'Install Impasto on Windows',
        blurb:
          'Download and run the Windows build of Impasto, including how to get past the ' +
          'SmartScreen warning on the unsigned release.',
      },
      {
        path: '/docs/install-macos/',
        label: 'Install on macOS',
        title: 'Install Impasto on macOS',
        blurb:
          'Install the macOS build of Impasto on Apple Silicon or Intel, and clear the ' +
          'Gatekeeper warning that unsigned builds trigger.',
      },
      {
        path: '/docs/interface/',
        label: 'The interface',
        title: 'The Impasto interface: pads, docking, and preferences',
        blurb:
          'How the Impasto window is laid out — the toolbox, tool options bar, and the layers, ' +
          'history, palette, and images pads — and which parts you can move, hide, or restyle.',
      },
    ],
  },
  {
    label: 'Tools',
    pages: [
      {
        path: '/docs/tools/',
        label: 'Toolbox overview',
        title: 'Impasto toolbox: every tool and what it does',
        blurb:
          'A reference for every tool in the Impasto toolbox, the shortcut key that selects it, ' +
          'and the options each one puts on the tool options bar.',
      },
      {
        path: '/docs/tools/selection/',
        label: 'Selection tools',
        title: 'Selection tools in Impasto',
        blurb:
          'Rectangle, ellipse, lasso, and magic wand selections, the union/exclude/xor/intersect ' +
          'combine modes, and how to move a selection outline versus the pixels inside it.',
      },
      {
        path: '/docs/tools/paint/',
        label: 'Paint tools',
        title: 'Paintbrush, pencil, eraser, and fill tools',
        blurb:
          'The freehand painting tools in Impasto: brush width and blend modes, the pixel ' +
          'pencil, eraser modes, paint bucket tolerance, and the gradient tool.',
      },
      {
        path: '/docs/tools/shapes/',
        label: 'Shape tools',
        title: 'Editable shape tools in Impasto',
        blurb:
          'Draw rectangles, ellipses, freeform shapes, and lines or curves that stay editable — ' +
          'move their control points, restyle the outline, and re-edit after you come back.',
      },
      {
        path: '/docs/tools/text/',
        label: 'Text tool',
        title: 'Adding and editing text in Impasto',
        blurb:
          'Place text on the canvas, change the font, size, alignment, and outline, and come ' +
          'back later to re-edit the words because text stays an editable object.',
      },
    ],
  },
  {
    label: 'Working with images',
    pages: [
      {
        path: '/docs/layers/',
        label: 'Layers and objects',
        title: 'Layers, objects, and blend modes in Impasto',
        blurb:
          'Add, reorder, merge, and blend layers; keep shapes and text as editable objects ' +
          'inside a layer; and move an object from one layer to another.',
      },
      {
        path: '/docs/history/',
        label: 'Undo history',
        title: 'Undo, redo, and the history pad',
        blurb:
          'Every edit in Impasto is a history step you can walk back and forward, including ' +
          'edits to individual shape and text objects.',
      },
      {
        path: '/docs/canvas/',
        label: 'Canvas and zoom',
        title: 'Resize, crop, rotate, and zoom the canvas',
        blurb:
          'Resize the image or the canvas, crop to a selection, auto-crop, flip and rotate, and ' +
          'control zoom, rulers, and units.',
      },
      {
        path: '/docs/snapping/',
        label: 'Snapping and guides',
        title: 'Grid snapping, guides, and alignment',
        blurb:
          'Snap what you draw and what you move to the canvas grid, to ruler units, and to the ' +
          'canvas edges and centre lines, with a guide drawn while you drag.',
      },
      {
        path: '/docs/color-and-palettes/',
        label: 'Color and palettes',
        title: 'Colors, the color picker, and palettes',
        blurb:
          'Pick primary and secondary colors, work with the palette pad, and load or save ' +
          'palette files in the formats Impasto supports.',
      },
    ],
  },
  {
    label: 'Effects and files',
    pages: [
      {
        path: '/docs/adjustments/',
        label: 'Adjustments',
        title: 'Image adjustments in Impasto',
        blurb:
          'Brightness and contrast, curves, levels, hue and saturation, black and white, sepia, ' +
          'posterize, and the other adjustments, with what each control does.',
      },
      {
        path: '/docs/effects/',
        label: 'Effects',
        title: 'Effects reference: blurs, distort, render, stylize',
        blurb:
          'Every built-in effect in Impasto grouped by menu, with its parameters and the live ' +
          'preview, repeat-last-effect, and random-seed behavior of the effect dialogs.',
      },
      {
        path: '/docs/file-formats/',
        label: 'File formats',
        title: 'File formats Impasto can open and save',
        blurb:
          'Which image formats Impasto reads and writes — including layered OpenRaster (.ora) ' +
          'and PDN files — and the options each format offers when you save.',
      },
    ],
  },
  {
    label: 'Customizing',
    pages: [
      {
        path: '/docs/keyboard-shortcuts/',
        label: 'Keyboard shortcuts',
        title: 'Keyboard shortcuts and how to remap them',
        blurb:
          'The default keyboard shortcuts in Impasto and how to rebind any of them, including ' +
          'where the custom keybindings file is stored.',
      },
      {
        path: '/docs/addins/',
        label: 'Add-ins',
        title: 'Installing and writing Impasto add-ins',
        blurb:
          'Extend Impasto with add-ins that add tools, effects, or file formats: install them ' +
          'from the add-in manager, or build your own from the sample project.',
      },
      {
        path: '/docs/faq/',
        label: 'FAQ',
        title: 'Impasto FAQ',
        blurb:
          'Is Impasto free, how does it relate to Pinta and to Paint.NET, does it work offline, ' +
          'and what does it send over the network?',
      },
    ],
  },
];

export const docsPages: DocsPage[] = docsSections.flatMap((section) => section.pages);

export function docsPageAt(path: string): DocsPage {
  const page = docsPages.find((candidate) => candidate.path === path);
  if (!page) throw new Error(`No docs page registered for ${path} — add it to src/docs/nav.ts`);
  return page;
}

/** Previous/next in reading order, used for the sequential links at the foot of each page. */
export function docsNeighbors(path: string): { prev?: DocsPage; next?: DocsPage } {
  const index = docsPages.findIndex((candidate) => candidate.path === path);
  return { prev: docsPages[index - 1], next: docsPages[index + 1] };
}
