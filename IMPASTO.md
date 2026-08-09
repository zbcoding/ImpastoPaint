# Impasto

A fork of [Pinta](https://github.com/PintaProject/Pinta) (MIT) that adopts Paint.NET's
interface. Upstream stays the source of truth for the engine; this fork changes the shell.

This file used to be the whole development log. It is now an index — each section lives
in its own document under `docs/`:

| Document | What's in it |
|---|---|
| [fork-charter.md](docs/fork-charter.md) | Why the fork exists, the naming decision, and the ground rules that keep it rebasable against upstream |
| [feature-status.md](docs/feature-status.md) | What has been built, and how far each item is actually verified |
| [known-issues.md](docs/known-issues.md) | Open bugs and accepted warts |
| [roadmap.md](docs/roadmap.md) | Next up, and the deferred big rocks |
| [toolbox-layout.md](docs/toolbox-layout.md) | How tools are assigned to toolbox sections and stacks |

Working notes for individual subsystems stay in `docs-private/`, which is untracked —
they are scratch, not published documentation.

Build and run instructions are in [readme.md](readme.md). Contribution workflow is in
[CONTRIBUTING.md](CONTRIBUTING.md); the rules agents must follow are in
[AGENTS.md](AGENTS.md).
