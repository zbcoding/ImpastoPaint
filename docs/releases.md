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

## 5. Translations

Regenerate the template from source, then merge it into every catalogue so a release ships
the new strings as translatable (empty `msgstr`) rather than silently missing. Skipping the
merge is what let the template and catalogues drift ~100 msgids apart between v0.0.1 and
v0.1.1.

- [ ] `make updatepotfiles && make updatepot` — rebuilds `po/POTFILES.in` and `po/messages.pot`
      from the current source.
- [ ] `for f in po/*.po; do msgmerge --no-fuzzy-matching --update --backup=none "$f" po/messages.pot; done`
      — folds the new msgids into each catalogue and moves now-unused entries to `#~` comments.
      Without `--no-fuzzy-matching`, msgmerge guesses at every new msgid from whichever old one
      looks similar: thousands of untagged machine translations for a translator to audit, which
      is also exactly what the AI-translation marker rule exists to keep out of the catalogues.
- [ ] `for f in po/*.po; do msgfmt -c -o /dev/null "$f" || echo "BAD: $f"; done` — every
      catalogue still compiles (header-default warnings are fine; errors are not).
- [ ] Confirm the merge cost nothing: `msgfmt --statistics` totals should only lose translations
      whose msgid the release actually removed, and the fuzzy count should not move at all.
      A rebuilt template rewrites every `#:` source reference, so the raw diff is large by
      nature — read the statistics, not the line count.

## 6. Land and tag

- [ ] Land steps 2-5 on `main`; wait for CI to go green.
- [ ] `git tag -a vX.Y.Z -m "Impasto X.Y.Z" <commit>` then `git push origin vX.Y.Z`.
- [ ] Watch the tag's `build.yml` run. The `release` job needs all of
      `build-ubuntu`, `build-flatpak`, `build-appimage`, `build-macos`, `build-windows` to pass.

## 7. Verify the GitHub release

- [ ] `gh release view vX.Y.Z` — not a draft, not a prerelease, notes generated.
- [ ] Seven assets: `Impasto-linux-dotnet-*.zip`, `Impasto-x86_64.flatpak`,
      `Impasto-x86_64.AppImage`,
      `Impasto-osx-arm64-unsigned.dmg`, `Impasto-osx-x64-unsigned.dmg`,
      `Impasto-win-x64.exe`, `Impasto-win-arm64.exe`.
- [ ] The macOS `.dmg` files are unsigned; say so wherever users are pointed at them.
- [ ] The AppImage bundles the build host's GTK and inherits its glibc as a floor. The
      `build-appimage` job prints both on every run; if the floor moved, say so wherever users
      are pointed at the image.
- [ ] Do not rename or drop `Impasto-linux-dotnet-*.zip` — flatpark matches it with
      `^Impasto-linux-dotnet-.*\.zip$`.
- [ ] The site's download button shows `vX.Y.Z`. It reads `configure.ac` at build time, and the
      tag's run rebuilds it in the `site` job, which calls `deploy-site.yml` — confirm that job
      succeeded. (A manual `gh workflow run deploy-site.yml` also works, and is the way to
      rebuild without a release.) The `github-pages` environment only accepts deployments from
      the refs it lists: `main` and `v*` tags. Recreating that environment without the tag policy
      makes the `site` job fail at its deploy step.

## 8. Specific releases

### macOS
- macOS signing/notarisation is not implemented

### Flatpak

#### FlatPark

FlatPark packages the release's `Impasto-linux-dotnet-*.zip` as
`com.github.zbcoding.Impasto`. It has three ways to hear about a release, all ending in the same
place: `resolve-update.sh` re-reads the releases API, bumps the pin under
`registry/com.github.zbcoding.Impasto/`, and `publish.yml` rebuilds and republishes the Flatpak.
Impasto was listed by https://github.com/flatpark/flatpark/pull/200

- [ ] **Ping (normal path).** The release job's `Notify FlatPark` step runs
      `flatpark/publish-action@v1`, which POSTs `{app_id, tag, repository}` to
      `https://hooks.flatpark.org/release` — no token, and `continue-on-error` so a FlatPark
      outage cannot fail an already-published release. Their `release-dispatch.yml` then opens
      `auto/release-com.github.zbcoding.Impasto`, auto-merges it and publishes, usually within
      minutes.
- [ ] **Daily sweep (safety net).** `update-check.yml` runs at ~06:17 UTC over every app. It
      catches what a ping cannot: a Linux asset uploaded after the ping, or a ping that never
      went out. Waiting for it is the normal outcome if the ping was skipped, so nothing needs
      doing.

To get a listing refreshed before that sweep — a ping that failed, a re-tagged release, or a
release cut before this step existed — do it by hand:

```bash
# The same request the release job sends. Nothing to authenticate: the endpoint is public,
# and it refuses only an app id that is not listed.
curl -sS --fail-with-body -H 'content-type: application/json' \
  -d '{"app_id":"com.github.zbcoding.Impasto","tag":"vX.Y.Z","repository":"zbcoding/ImpastoPaint"}' \
  https://hooks.flatpark.org/release
```

Then confirm it took, rather than assuming: the dispatch run and the PR it opens are public.
`flatpark/flatpark` write access is not needed for the ping, but `gh workflow run update-check.yml
--repo flatpark/flatpark` also works — it sweeps every app and opens one PR — for anyone who has
it.

- [ ] `gh run list --repo flatpark/flatpark --workflow=release-dispatch.yml` shows an
      `app-release` run for the ping, or `update-check.yml` for the sweep, and it succeeded.
- [ ] `gh pr list --repo flatpark/flatpark --search "Impasto"` shows the update PR merged.
- [ ] Check `https://flatpark.org/apps/com.github.zbcoding.Impasto/` shows `X.Y.Z` once it
      has rebuilt. The version and "What's New" there come from the metainfo `<releases>`
      list. flatpark keeps its own copy under `registry/com.github.zbcoding.Impasto/`; if a
      rebuild does not pick up the new `<release>` from upstream, you can open a PR on
      flatpark/flatpark updating that copy.
- [ ] `flatpak update` pulls `X.Y.Z` once flatpark has republished.


#### Flathub: the root `com.github.zbcoding.Impasto.yml` is not a Flathub submission 
- Not on Flathub due to their strict no AI policy
- A Flathub listing would need a generated `nuget-sources.json` and its own submission.
