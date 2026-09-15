# Privacy policy

Impasto does not send any of your data to other networked systems unless you modify the
program to do so, or you ask it to (by opening a link, refreshing the add-in gallery, or
installing an add-in). The automatic update check below is the one outbound connection
that happens without a click, and it sends nothing about you.

## Outgoing connections

| What happens | When | Endpoint | What leaves your machine |
| --- | --- | --- | --- |
| Update check | Once per launch, automatically | `api.github.com/repos/zbcoding/ImpastoPaint/releases/latest` | Nothing but the request itself. No usage data, image data, or settings; the only header added is `User-Agent: ImpastoPaint`. |
| Add-in repository index refresh | Registering repositories at first start, and whenever the add-in gallery refreshes | `www.pinta-project.com/Pinta-Community-Addins/repository/{Linux,Windows,Mac,All}/main.mrep` and `raw.githubusercontent.com/zbcoding/ImpastoPaint/main/samples/ImpastoSampleAddin/repository/main.mrep` | Nothing but the request itself. |
| Add-in download | Only when you install an add-in from the gallery | the `.mpack` file's repository host | Nothing but the request itself. |
| Browser opens | Only when you click Help, Issues, Website, About links, or "Update Impasto…" | GitHub pages for this repository, in your web browser | Nothing. Your browser's own behavior is its own policy. |

Windows' screen-snipping helper (`ms-screenclip:`) hands the clipboard to the operating
system's own snipping tool; it talks to the OS, not to a network.

## Nothing else

There is no telemetry, no crash reporting, no analytics, and no account. The program does
not transmit your images, layers, settings, or usage. Sources for every row of the table:
`UpdateChecker.cs`, `UpdateImpastoAction.cs`, `AddinSetupService.cs`,
`AddinManagerDialog.cs`, `HelpActions.cs`, `AboutDialogAction.cs` in this repository.
