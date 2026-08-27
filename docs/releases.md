# Checklist for a release

A release is a `v*` tag pushed to `main`. The tag triggers `.github/workflows/build.yml`,
whose `release` job builds every platform and runs
`gh release create <tag> out/* --generate-notes`. Everything below prepares `main` so that
tag produces a correct release and a correct store listing.

Do the file edits on a worktree branch and land them on `main` the normal way
(see AGENTS.md); tag only once `main` carries all of them.

## 1. Preconditions

- [ ] `main` is green on CI and the working tree is clean.
- [ ] You have the version number (semver). `X.Y.Z` below.

## 2. Version

- [ ] `configure.ac` line 2: `AC_INIT([impasto], [X.Y.Z])`. All other places should reference this file for the version number. This is the only place the
      version lives, and the release zip is named from it. Bump it if it is not already
      `X.Y.Z` (`git grep` the old number to be sure nothing else pinned it).

- [ ]  These must also be edited on each release:
      - Changelog.md
      - xdg/com.github.zbcoding.Impasto.metainfo.xml.in — the <release> entry

## 3. CHANGELOG.md

Check and update the changelog.md file based on the commits since the last version.

The `[Unreleased]` section is the release notes; cutting the release just stamps it.

- [ ] Rename `## Impasto - [Unreleased](.../compare/vPREV...main)` to
      `## Impasto - [X.Y.Z](https://github.com/zbcoding/ImpastoPaint/releases/tag/vX.Y.Z) - YYYY-MM-DD`.
- [ ] Add a fresh `## Impasto - [Unreleased](.../compare/vX.Y.Z...main)` above it, with the
      line `Changes for the next release go here.`

## 4. AppStream metainfo

`xdg/com.github.zbcoding.Impasto.metainfo.xml.in` is what GNOME Software, KDE Discover and
the flatpark.org page read for the version and the "What's New" text.

- [ ] Add a `<release>` entry at the top of `<releases>` (newest first; AppStream requires
      version order):

      ```xml
      <release version="X.Y.Z" date="YYYY-MM-DD">
        <url>https://github.com/zbcoding/ImpastoPaint/releases/tag/vX.Y.Z</url>
        <description>
          <p>One or two plain sentences of what changed, drawn from the CHANGELOG.</p>
        </description>
      </release>
      ```

- [ ] `xmllint --noout xdg/com.github.zbcoding.Impasto.metainfo.xml.in`.

## 5. Land and tag

- [ ] Land steps 2-4 on `main`; wait for CI to go green.
- [ ] `git tag -a vX.Y.Z -m "Impasto X.Y.Z" <commit>` then `git push origin vX.Y.Z`.
- [ ] Watch the tag's `build.yml` run. The `release` job needs all of
      `build-ubuntu`, `build-flatpak`, `build-macos`, `build-windows` to pass.

## 6. Verify the GitHub release

- [ ] `gh release view vX.Y.Z` — not a draft, not a prerelease, notes generated.
- [ ] Six assets: `Impasto-linux-dotnet-*.zip`, `Impasto-x86_64.flatpak`,
      `Impasto-osx-arm64-unsigned.dmg`, `Impasto-osx-x64-unsigned.dmg`,
      `Impasto-win-x64.exe`, `Impasto-win-arm64.exe`.
- [ ] The macOS `.dmg` files are unsigned; say so wherever users are pointed at them.
- [ ] Do not rename or drop `Impasto-linux-dotnet-*.zip` — flatpark matches it with
      `^Impasto-linux-dotnet-.*\.zip$`.

## 7. Specific releases

### macOS
- macOS signing/notarisation is not implemented

### Flatpak

#### FlatPark

FlatPark tracks GitHub `releases/latest` on its own. Its `update-check.yml` runs daily
(~06:17 UTC) and on manual dispatch: `resolve-update.sh` reads the releases API, picks the
`Impasto-linux-dotnet-*.zip`, bumps the pin, and `publish.yml` rebuilds. Impasto is already listed with the PR at https://github.com/flatpark/flatpark/pull/200

- [ ] To publish sooner than the next cron, dispatch `update-check.yml` from the
      flatpark/flatpark Actions tab (it sweeps every app and opens one PR).
- [ ] Check `https://flatpark.org/apps/com.github.zbcoding.Impasto/` shows `X.Y.Z` once it
      has rebuilt. The version and "What's New" there come from the metainfo `<releases>`
      list. flatpark keeps its own copy under `registry/com.github.zbcoding.Impasto/`; if a
      rebuild does not pick up the new `<release>` from upstream, you can open a PR on
      flatpark/flatpark updating that copy.
- [ ] `flatpak update` pulls `X.Y.Z` once flatpark has republished.


#### Flathub: the root `com.github.zbcoding.Impasto.yml` is not a Flathub submission 
- Not on Flathub due to their strict no AI policy
- A Flathub listing would need a generated `nuget-sources.json` and its own submission.
