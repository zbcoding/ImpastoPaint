# Add-ins

Packaging, discovery and installation are Mono.Addins': `.mpack` archives, a manifest, a
registry under `~/.config/Impasto/addins/`. Add-ins built for upstream Pinta load here
unchanged, and three separate concerns are easy to confuse, so keep them apart:

| Layer | Owner | Notes |
|---|---|---|
| Packaging and discovery | Mono.Addins | One mechanism for every add-in. There is no second loader |
| Which API the binary was compiled against | manifest dependency | `AddinDependency ("Pinta", …)`; host assembly versions are deliberately ignored (`Impasto/AddinAssemblyResolver.cs`) |
| What a loaded add-in may reach | this document | Where the work is |

## The contract

One entry point: implement `IExtension`, mark it `[Mono.Addins.Extension]`, register from
`Initialize`, undo it in `Uninitialize`. `Pinta.Core` carries `[TypeExtensionPoint]` on seven
other types; only `IExtension` is wired, so an extension node for any of the others is never
read.

Manifest, minimally:

```csharp
[assembly: Mono.Addins.Addin ("MyAddin", "0.1", Category = "Effects")]
[assembly: Mono.Addins.AddinName ("My Add-in")]
[assembly: Mono.Addins.AddinDependency ("Pinta", PintaCore.PintaCompatVersion)]
```

`PintaCore.PintaCompatVersion` is what this fork offers add-ins; `PintaAddinCompatVersion` is
the oldest it accepts, and 3.0 is the GTK3 → GTK4 boundary below which nothing can work.

Working example, with build and install steps: `samples/ImpastoSampleAddin`.

## Menu placement is decided by provenance

An add-in's effects and adjustments are grouped under an **Add-ins** container in the menu they
belong to, by the add-in's name, then by whatever category it asked for:

```
Effects ▸ Add-ins ▸ My Add-in ▸ Distort ▸ My Effect
```

The add-in does not opt in — placement is decided from the assembly a contribution was declared
in, so add-ins written for upstream Pinta land in the container too. `EffectMenuCategory` is a
qualifier under the add-in's name, not a location, and the default `"General"` is left off.

The container is created when the first contribution arrives and removed when the last one
leaves, so a menu no add-in touched shows nothing extra. It is a section, which pins it below
the menu's own items in every locale. Two levels below the container is the depth ceiling;
a deeper path is folded into the last label. `AddinMenu` owns all of this.

`AddinActions.AddMenuItem` / `RemoveMenuItem` add commands to the top-level Add-ins menu.

## Icons

Ship icons the way the application does — `icons/hicolor/scalable/actions/<name>-symbolic.svg`
beside the assembly — and return `<name>-symbolic` from `Icon`. Each installed add-in's `icons`
directory is added to the theme's search path at startup.

An icon name that does not resolve draws a stand-in in the toolbox, not a broken-image glyph.
Effects already default to `Resources.Icons.EffectsDefault`.

## Tools

`Priority` decides the toolbox section (`docs/toolbox-layout.md`). Use a priority past every
built-in bound — 1000 and up — so the tool lands in the trailing section. Reusing a priority
that belongs to a tool stack silently shares that stack's button.

## Not available yet

- **Keyboard bindings.** `KeyboardShortcutManager.ToolBindings` is a closed list, so an add-in
  tool's keys cannot be rebound by the user. Overrides are also keyed on the bare type name,
  which two add-ins can collide on.
- **Preferences.** Settings persist through `ISettingsService`, but there is no extension point
  in the Preferences dialog and setting keys are not namespaced per add-in.
- **File formats.** `ImageConverterManager.RegisterFormat` exists, but registration does not
  dedupe by extension and `UnregisterFormatByExtension` removes every format claiming that
  extension — including a built-in one. Both need fixing before add-in formats are safe.
