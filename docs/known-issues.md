# Known issues

- **`.ora` per-layer previews drop text.** Editable text objects are kept in a
  `data/impasto-text.xml` sidecar and rendered on the overlay layer, so Impasto
  round-trips them, and `mergedimage.png` shows them, but third-party ORA viewers
  that render per-layer PNGs won't see text in individual layers. Baking text into
  per-layer PNGs would double-render on re-import.
- **Toolbox column count under wide/maximised windows.** One capture showed a single
  column with a stray pair at the top instead of the clean 2-column grid. May be the
  fixed `MinChildrenPerLine = 2` misbehaving under a different allocation, may be a
  screenshot artifact. Unconfirmed — reproduce before changing anything.
- **Fixed: pin menu dismissed by hover flyout.** Right-clicking a flyout entry anchored
  the pin menu on the group button, but the cursor hovering that button re-opened the
  flyout after 350ms, whose grab dismissed the pin menu before it could be clicked.
  `ShowFlyout` now bails while a pin menu is open (`open_pin_menu` field). The pin menu
  still appears next to the group button by design — it pins the tool you right-clicked.
- **Pinned tools and the toggle group.** Every toolbox button shares one toggle group, so
  exactly one can be lit. A pinned tool has two buttons and the pinned one wins, so
  picking that tool from inside its stack flyout won't light the stack button. Fixing it
  means a second toggle group; the wart is cheaper.
- Translations are detached from the upstream Weblate instance: the in-app Help→Translate
  item is removed, and `.po` headers no longer point at Pinta's hosted-weblate team URLs
  (the gettext `.po` format itself is kept). Re-add a Translate entry and repoint
  `Language-Team` once a local/Impasto hosting decision is made.
- Installed binary is still named `pinta`; the `.desktop` `Exec` matches it. Rename the
  binary and the autotools packaging together or not at all.
- The `.ico`/`.icns` files are renamed to `Impasto.*` and the icon artwork is now
  Impasto's own (a palette + impasto "I"), replacing Pinta's. The hicolor app icons
  resolve under dotnet run from `build/bin/icons`; the window icon only refreshes after a
  rebuild.
