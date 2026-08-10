# Toolbox layout reference

Sections are assigned by tool `Priority`, so addin tools land in a bucket without
knowing about `ToolBoxWidget`:

| Section | Priorities | Tools |
|---|---|---|
| Pinned | n/a | user-chosen, persisted as type names |
| Move | ≤ 8 | Move Selected, Move Selection |
| View | ≤ 12 | Zoom, Pan |
| Select | ≤ 20 | Rectangle, Ellipse, Lasso, Magic Wand |
| Paint | ≤ 36 | Paintbrush, Pencil, Eraser, Bucket, Gradient, Colour Picker, Text |
| Shapes | ≤ 46 | Line/Curve, and the stack |
| Retouch | rest | Clone Stamp, Recolor |

Stacks are declared in `stack_definitions` as priority groups. Adding the selection stack
(rectangle + ellipse marquee) is one array entry, no new code.

## Footer layout reference

The status bar is one row, left to right: color palette (expands to fill), then the chips,
then the zoom controls pinned right. One collapse cascade governs the whole row —
`StatusBarColorPaletteWidget.UpdateLayout` owns it, and `ActionManager.GetFooterChipRoom` /
`SetFooterChipsVisible` are how the chips report their width and take their orders. The
palette's allocation plus the chips' occupied width is a fixed region; everything below is a
function of that budget.

| Element | Optional | Shown by default | Collapse behavior |
|---|---|---|---|
| Selection position / size chip | no — automatic | only while a selection is visible | never collapses; sits outside the shared region, so the palette's allocation simply shrinks and everything else collapses sooner |
| Color swatches section | no | yes | folds into the popover last |
| Recent colors section | no | yes | folds into the popover 2nd |
| Quick colors section | no | yes | folds into the popover 1st |
| Swap / reset action icons | no | yes | never collapse, never overlapped |
| Cursor position chip | yes — Preferences | yes | slides out 2nd |
| Canvas size / aspect chip | yes — Preferences | yes | slides out 1st |
| Zoom controls | no | yes | never collapse |

Collapse order as width shrinks: canvas size chip → cursor position chip → swatch tiles
shrink toward `MIN_SWATCH_SIZE` → quick colors fold → recent colors fold → swatches fold.
Tiles shrink again after each fold, so the surviving grid stays legible. While the colors are
floating (`show_action_icons` false) nothing in the bar can collide, so the chips keep their
room and no cascade runs.

`occupiedByChips` covers only the collapsible chips — the palette adds it to its own
allocation to reconstruct the shared region. A non-collapsing element must stay out of that
sum: the box has already taken its width out of the palette's allocation, and counting it
twice hands the palette room it doesn't have, which draws the swatches and action icons out
underneath it.

Every chip carries a tooltip explaining what it reports; the canvas size chip also opens
Image ▸ Canvas Size on double click.

An addin adding a footer element either takes a chip slot (`CreateChipSlot`, a revealer that
slides right) and joins the cascade, or appends a plain widget and stays out of the cascade
the way the selection chip does.
