# Contributing

Thanks for your interest in contributing to Impasto. This guide covers
contributing code and translations.

## Code

Contributing code to the **free edition** of Impasto is encouraged. The
free edition is licensed under the MIT License (see `license-mit.txt`).

**Editions.** A contribution received as a pull request to the free edition
may also be included and used in the **Premium edition** of Impasto. By
submitting a pull request, you acknowledge that your contribution may be
incorporated into both editions.

### AI-assisted coding

Contributing code with AI coding tools is encouraged. If you do:

- **Be able to explain the code** you submit. Understand what it does and
  why it works.
- **Be willing to edit the code** to match the project's architecture and
  conventions. `AGENTS.md` documents the design, naming, and rebase
  constraints the codebase follows.
- **Expect more review.** AI-assisted submissions may draw additional
  comments that must be addressed before the code is merged.

## Translations

Translations are welcome; the recommended way to contribute one is to open a
pull request with a translation file. Translation files (`.po`) live in
`po/`, one per language (e.g. `po/de.po`), with `po/messages.pot` as the
source template.

Using AI to generate the initial translation is encouraged — it's the fastest
way to cover a whole language — but treat the output as a draft, not a
finished translation. If you are a speaker of the language, edit and
proofread your assisted translation before submitting, so it reads naturally
instead of literally.

The workflow for AI-assisted entries:

- New or updated strings go in your language's `.po` file under `po/`.
- Every AI-generated entry must be marked so it is easy to spot and review:
  add a translator note and the fuzzy flag above the `msgid`:

      #. Translators: Describe what this string does for translators.
      #. AI-generated translation; human review requested.
      #, fuzzy
      msgid "..."
      msgstr "..."

  The `#, fuzzy` flag marks the string as needing editing until a human
  reviews it.
- Keep placeholders intact and mind your language's plural forms; see the
  existing `po/` files for the expected format.
- Once you have proofread and are confident in a string, remove the
  `#, fuzzy` flag (and the AI-generated note) so it counts as a
  human-reviewed translation.

## Submitting changes

To contribute code, make your changes available as a pull request on
GitHub. Using Git is an important part of contributing; GitHub's
documentation is a good starting point, and
[Pro Git](http://progit.org/book/) goes deeper.

#### Claiming an issue to work on

If you plan to work on an issue registered in the
[issue tracker](https://github.com/zbcoding/Impasto/issues), go to the
relevant issue, make sure no one else is already working on it, then leave
a comment saying that you are working on it.

#### Creating a fork

Create a GitHub account if you haven't got one (use your full name, so we
have something to put in the credits), then fork the
[Impasto repository](https://github.com/zbcoding/Impasto) and pull the
code down to your local machine.

#### Writing, compiling, and testing code

Make whatever changes are necessary to the source code, but please don't
attempt to clean up existing code. We're only interested in functional
changes; mixing in cosmetic changes makes it very hard to see what has
actually changed. Build the code according to the [building
instructions](readme.md) and test it to make sure it does what it's
supposed to and nothing it isn't.

Follow the architecture and conventions in `AGENTS.md`, and run the checks
it describes (build, tests, `dotnet format`) before submitting.

#### Committing and opening a pull request

Commit your changes to your fork with a clear commit message and a short
description. Then open a pull request against the Impasto repository from
your fork. Maintainers will review and test your solution; if it is good
it will be merged, and you'll be added to the credits as a contributor.

Questions on this process can be asked through the methods described in
`readme.md`.
