# Sample add-in

A fixture for the add-in contract: one effect, one tool, one extension entry point. It is
deliberately outside `Pinta.sln`, so building the application does not build it, and it is not
shipped.

Build it and drop it into the add-in registry:

```
dotnet build samples/ImpastoSampleAddin
mkdir -p ~/.config/Impasto/addins/addins/ImpastoSampleAddin.0.1
cp samples/ImpastoSampleAddin/bin/Debug/net10.0/ImpastoSampleAddin.dll \
   ~/.config/Impasto/addins/addins/ImpastoSampleAddin.0.1/
```

The registry is rescanned at startup, so the add-in appears on the next launch. Remove the
directory to uninstall it. The Add-in Manager lists it like any other add-in.

## What it demonstrates

- **One entry point.** `IExtension.Initialize` registers, `Uninitialize` unregisters. Nothing
  else is wired up, and the seven other `TypeExtensionPoint` attributes in `Pinta.Core` are
  not read.
- **Menu placement is decided for you.** The effect lands at
  `Effects ▸ Add-ins ▸ Impasto Sample Add-in ▸ Fixtures`, because the host groups add-in
  contributions under that container by their add-in name. An add-in does not opt in, and one
  written against upstream Pinta gets the same treatment.
- **A missing icon is survivable.** The tool asks for an icon it does not ship, so the toolbox
  draws a stand-in rather than a broken-image glyph. To ship a real one, put it at
  `icons/hicolor/scalable/actions/<name>-symbolic.svg` beside the assembly - the host adds
  that directory to the icon theme's search path - and return `<name>-symbolic` from `Icon`.
- **The tool gets its own toolbox section.** Every add-in tool does, behind a divider below the
  built-in sections, so `Priority` only decides the order within it. The sample keeps a high
  priority anyway, as a reminder that a built-in's priority no longer buys placement.

## What the contract does not cover yet

- Keyboard bindings. `KeyboardShortcutManager.ToolBindings` is a closed list, so an add-in
  tool cannot register a rebindable key.
- Preferences. An add-in can store settings through `ISettingsService`, but there is no
  extension point in the Preferences dialog, and setting keys are not namespaced per add-in.
