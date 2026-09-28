---
layout: ../../layouts/DocsLayout.astro
---

Add-ins extend Impasto with new tools, effects, and adjustments. You install, update, disable, and remove them from the Add-in Manager. Impasto uses the same add-in system as Pinta, so add-ins written for Pinta 3.0 or later also work in Impasto.

## Open the Add-in Manager

Choose **Add-ins > Add-in Manager...**. The window has three tabs:

| Tab | What it shows |
|---|---|
| **Gallery** | Add-ins available to download that work with your version of Impasto |
| **Installed** | Add-ins you have installed, then an **Included with Impasto** section for the add-ins that come with the application |
| **Updates** | Installed add-ins that have a newer version in the gallery |

Select an add-in to see its description, version, download size, and the repository it comes from. **More Information...** opens the add-in's web page, if it has one.

## Install an add-in from the gallery

1. Open **Add-ins > Add-in Manager...** and go to the **Gallery** tab.
2. Select the add-in you want.
3. Click **Install...**. A window lists the packages that will be installed, including any other add-ins it depends on.
4. Click **Install**.

The gallery combines three repositories: the two Pinta community add-in repositories (one for your platform, one cross-platform) and Impasto's own add-in repository. The gallery list is kept on disk and checked for new versions at most once a day when you open the Add-in Manager. Click the **Refresh** button in the title bar to check now.

Opening the gallery, refreshing it, and installing add-ins need an internet connection. See [the FAQ](/docs/faq/) for what Impasto sends over the network.

## Install an add-in from a file

Add-ins are distributed as `.mpack` files. To install one you downloaded:

1. Open **Add-ins > Add-in Manager...**.
2. Click the **Install from file...** button (the folder icon) in the title bar.
3. Choose one or more `.mpack` files and confirm the install.

If a package can't be installed because of missing or conflicting dependencies, the install window lists them.

## Update, disable, or remove add-ins

- **Update:** go to the **Updates** tab, select the add-in, and click **Update...**.
- **Disable:** on the **Installed** tab, select the add-in and turn off its switch. Turn it back on to enable it again.
- **Remove:** on the **Installed** tab, select the add-in and click **Uninstall...**. The confirmation lists any other add-ins that depend on it; those are removed too.

Add-ins in the **Included with Impasto** section can't be disabled or uninstalled.

## Where add-ins appear

Impasto places what an add-in contributes so you can tell it apart from the built-in features:

- **Effects and adjustments** go in an **Add-ins** submenu at the bottom of the Effects or Adjustments menu, grouped by the add-in's name. For example: **Effects > Add-ins > My Add-in > Distort > My Effect**.
- **Tools** get their own section at the bottom of the toolbox, below a divider. Each tool button's tooltip names the add-in that supplied it.
- **Other commands** an add-in adds go in the **Add-ins** menu, below **Add-in Manager...**.

You can give an add-in's tool a toolbox key on the **Tools** tab of [Keyboard Shortcuts](/docs/keyboard-shortcuts/). Keys that an add-in tool uses while it is active can't be rebound, and add-ins can't add pages to **Edit > Settings...**.

Installed add-ins are stored in the `addins` folder inside Impasto's settings folder, for example `~/.config/Impasto/addins/` on Linux. See [the FAQ](/docs/faq/#where-are-my-settings-and-how-do-i-reset-them) for the location on each system.

## Writing your own add-in

An add-in is a .NET library that references Impasto's core library and is loaded through Mono.Addins. The fastest way to start is the sample add-in in the Impasto repository, [samples/ImpastoSampleAddin](https://github.com/zbcoding/ImpastoPaint/tree/main/samples/ImpastoSampleAddin). It adds one effect and one tool, and its readme covers building, installing, and packaging.

The sample is also listed in the gallery as **Impasto Sample Add-in**, under the **Impasto Add-ins** repository, so you can install it to see the result before you build it.

### What an add-in needs

- **A manifest**, written as assembly attributes: an ID and version, a display name, and a dependency on the `Pinta` add-in root. Keep the root name `Pinta`; that is what lets Pinta add-ins load in Impasto unchanged.

  ```csharp
  [assembly: Mono.Addins.Addin ("MyAddin", "0.1", Category = "Effects")]
  [assembly: Mono.Addins.AddinName ("My Add-in")]
  [assembly: Mono.Addins.AddinDependency ("Pinta", PintaCore.PintaCompatVersion)]
  ```

- **One entry point:** a class that implements `IExtension` and is marked `[Mono.Addins.Extension]`. Register your tools and effects in `Initialize` and remove them in `Uninitialize`.
- **Icons (optional):** ship them as `icons/hicolor/scalable/actions/<name>-symbolic.svg` next to your add-in's DLL, and return `<name>-symbolic` as the icon name. If an icon can't be found, the toolbox draws a placeholder instead.

You don't choose where your add-in appears. Impasto places its menu items and tools as described in [Where add-ins appear](#where-add-ins-appear). An effect's category becomes a submenu under your add-in's name.

### Trying it out while you develop

Build the add-in and copy the DLL into its own folder under `addins/addins/` in Impasto's settings folder. On Linux, for the sample:

```sh
dotnet build samples/ImpastoSampleAddin
mkdir -p ~/.config/Impasto/addins/addins/ImpastoSampleAddin.0.1
cp samples/ImpastoSampleAddin/bin/Debug/net10.0/ImpastoSampleAddin.dll \
   ~/.config/Impasto/addins/addins/ImpastoSampleAddin.0.1/
```

Impasto scans that folder when it starts, so restart Impasto to load the add-in. Delete the folder to remove it. When you are ready to share it, package it as an `.mpack` so others can use **Install from file...**; the sample's readme shows the command.
