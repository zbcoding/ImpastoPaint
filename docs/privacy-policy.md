# Privacy policy

While this policy is subject to change, Impasto currently does not send any of your data to other networked systems unless you modify the
program to do so, or you ask it to (by opening a link, refreshing the add-in gallery, or
installing an add-in).

## Connections

| What happens | When | Endpoint | What leaves your machine |
| --- | --- | --- | --- |
| Update check | Once per launch, automatically | `api.github.com/repos/zbcoding/ImpastoPaint/releases/latest` | Nothing but the request itself. No usage data, image data, or settings; the only header added is `User-Agent: ImpastoPaint`. |
| Add-in repository index refresh | Registering repositories at first start, and whenever the add-in gallery refreshes | `www.pinta-project.com/Pinta-Community-Addins/repository/{Linux,Windows,Mac,All}/main.mrep` and `raw.githubusercontent.com/zbcoding/ImpastoPaint/main/samples/ImpastoSampleAddin/repository/main.mrep` | Nothing but the request itself. |
| Add-in download | Only when you install an add-in from the gallery | the `.mpack` file's repository host | Nothing but the request itself. |
| Browser opens | Only when you click Help, Issues, Website, About links, or "Update Impasto…" | GitHub pages for this repository, in your web browser | Nothing. Your browser's own behavior is its own policy. |

## Website

The website at `impastopaint.com` measures visits with two cookieless analytics services. Neither uses cookies or local storage, and both apply to visitors in all regions, including the EU, EEA, and UK.

| Service | What it records |
| --- | --- |
| GoatCounter | The page visited, page title, referring page, screen size, and query string. Visits from local or embedded pages are not counted. |
| Cloudflare Web Analytics, provided by Cloudflare, Inc. | Page load speed and performance, the page visited, the referring page, and your browser and device type. Cloudflare states that it does not fingerprint visitors. |

Both services receive your IP address as part of the request; Impasto does not store it.
