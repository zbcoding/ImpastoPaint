# Fork charter — why Impasto exists and how it stays rebasable

Impasto is a painting and image editing application.

A separate fork of [Pinta](https://github.com/PintaProject/Pinta) (MIT) that has new features and user interface of its own.

Impasto inherits MIT-licensed code from Pinta, but is not related - Impasto features have different contributors.
Many code changes, with links in commits, are inspired or sourced from Pinta, even though Impasto is a separate project. 

Not maintained by Pinta contributors. Pinta itself was inspired by PDN, which used to have an
MIT license. `Paint.NET` is a registered trademark of dotPDN LLC, so the PDN name, logo and
icon artwork are not used in the application.

## Ground rules

Ideally, stay mostly rebaseable compared to Pinta, the original upstream. Note the branch
names differ: Pinta's default branch is `master`, Impasto's is `main`.

```
git fetch upstream && git rebase upstream/master
```

- **Do not rename namespaces or project files.** They stay `Pinta.*`. Renaming them
  touches every file and makes every rebase a conflict. The one exception already taken
  is the `Impasto` app assembly (`Impasto/Impasto.csproj`), renamed because the binary
  name is what the desktop shell matches on — a build called `Pinta` resolved to an
  installed Pinta's icon. Its `RootNamespace` still stays `Pinta`.

  The `Pinta.*` folder and namespace names record where the code came from, not who
  maintains it now. Much of what lives under them is Impasto's own work.
- **`Pinta.Core` and `Pinta.Tools` are the expensive ones to change.** They are no longer
  anywhere near untouched, so treat this as a cost to weigh rather than a line not to
  cross. Measured against upstream:

  | | files | insertions |
  |---|---|---|
  | `Pinta.Core` | 71 | +5,293 |
  | `Pinta.Tools` | 36 | +5,678 |

  Regenerate rather than trust these numbers — they rot:

  ```
  git diff --shortstat upstream/master HEAD -- Pinta.Core Pinta.Tools
  ```

  The bulk is deliberate: the text tool, the keyboard-shortcut manager, the ORA/PDN/AVIF
  format handlers and the re-editable object rasterizer are Impasto features that had
  nowhere else to live. What the rule still asks is that a *one-line* change to these
  projects be worth the conflict it will cause — the palette-format string in
  `Pinta.Core/Extensions/OtherExtensions.cs` is the kind of edit to think twice about.
- Mark deliberate simplifications with a comment naming the ceiling.
- **Trademarks.** Linking to [Paint.NET](https://www.getpaint.net/) in documentation is
  fine, and naming it in prose for context is fine. What we avoid is the name, logo and
  icon artwork inside the application itself and in application code — no PDN branding in
  UI strings, identifiers, resources or file names. In docs, prefer `PDN` as shorthand.
- **Translations move in lockstep with their msgids.** Rebranding a string means editing
  the `msgid` *and* every `msgstr` that renders the old name, across all of `po/*.po`. A
  changed `msgid` with an untouched `msgstr` silently orphans that translation and the
  string falls back to English — nothing warns you. Two traps: the same `msgid` may
  already exist under the new wording, and duplicates make `msgfmt` reject the file, so
  merge the entries instead of renaming into a collision; and many `msgstr`s inflect or
  transliterate the product name (`Pinty`, `Pinto`, `Пинта`, `பிண்டா`), so searching for
  the literal name misses them. Verify with `msgfmt -c` over every catalogue.
- **Crediting upstream work:** when a commit ports a Pinta PR, link the PR author's GitHub
  profile in the commit message (e.g. `https://github.com/Sam-Gledhill`) plus the PR link,
  not just the username. For pure issue requests (no code), listing the reporter's name is
  enough and we avoid `Credit:` wording — use `Issue reported by <name>` or
  `Requested by <name>` with the issue link.

## Identity

App ID is `com.github.zbcoding.Impasto`. Settings live in `~/.config/Impasto/settings.xml`
so Impasto installs alongside Pinta rather than replacing it — the metainfo deliberately
drops upstream's `<replaces>pinta.desktop</replaces>`.
