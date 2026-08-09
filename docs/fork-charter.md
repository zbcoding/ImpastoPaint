# Fork charter — why Impasto exists and how it stays rebasable

A fork of [Pinta](https://github.com/PintaProject/Pinta) (MIT) that adopts Paint.NET's
interface. Upstream stays the source of truth for the engine; this fork changes the shell.

Named Impasto because `Paint.NET` is a registered trademark of dotPDN LLC. Layout and
interaction patterns are free to reimplement — Pinta has done exactly that for 16 years —
but the name, logo and icon artwork are not. `Paint.Linux`, `PaintDot` and `SimplePaint`
were all rejected: the first two read as official ports, the third is unprotectable and
unsearchable (1,663 GitHub repos).

## Ground rules

The whole strategy depends on staying rebasable against upstream:

```
git fetch upstream && git rebase upstream/main
```

- **Do not rename namespaces or project files.** They stay `Pinta.*`. Renaming them
  touches every file and makes every rebase a conflict. The one exception already taken
  is the `Impasto` app assembly (`Impasto/Impasto.csproj`), renamed because the binary
  name is what the desktop shell matches on — a build called `Pinta` resolved to an
  installed Pinta's icon. Its `RootNamespace` still stays `Pinta`.
- **Avoid `Pinta.Core` and `Pinta.Tools`.** Changes there are the expensive ones. So far
  `Pinta.Tools` is untouched; `Pinta.Core` carries the extended-palette helper plus a few
  one-line edits.
- Mark deliberate simplifications with a `ponytail:` comment naming the ceiling.
- Prefer flipping an existing upstream setting over writing a feature. The menu bar was
  already fully built for macOS — it needed a one-line default change, not a rewrite.
- **Avoid mentioning the trademarked reference editor by name in commit messages.**
  The name is a registered trademark of dotPDN LLC. Use generic phrasing like
  "reference implementation", "major raster editors", or "Impasto" / "Pinta" instead.
  Descriptive text in these docs can mention it for context, but commit logs should not.
- **Crediting upstream work:** when a commit ports a Pinta PR, link the PR author's GitHub
  profile in the commit message (e.g. `https://github.com/Sam-Gledhill`) plus the PR link,
  not just the username. For pure issue requests (no code), listing the reporter's name is
  enough and we avoid `Credit:` wording — use `Issue reported by <name>` or
  `Requested by <name>` with the issue link.

## Identity

App ID is `com.github.zbcoding.Impasto`. Settings live in `~/.config/Impasto/settings.xml`
so Impasto installs alongside Pinta rather than replacing it — the metainfo deliberately
drops upstream's `<replaces>pinta.desktop</replaces>`.
