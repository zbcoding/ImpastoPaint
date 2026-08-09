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
