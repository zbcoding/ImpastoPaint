# Impasto

A fork of [Pinta](https://github.com/PintaProject/Pinta) (MIT) that adopts Paint.NET's
interface. Upstream stays the source of truth for the engine; this fork changes the shell.

This file used to be the whole development log. It is now an index — each section lives
in its own document under `docs/`:

| Document | What's in it |
|---|---|
| [fork-charter.md](docs/fork-charter.md) | Why the fork exists, the naming decision, and the ground rules that keep it rebasable against upstream |
| [toolbox-layout.md](docs/toolbox-layout.md) | How tools are assigned to toolbox sections and stacks |
| [ora-format.md](docs/ora-format.md) | What Impasto writes into an `.ora` file, and how re-editable objects are stored |
| [addins.md](docs/addins.md) | What an add-in may reach: the entry point, menu placement, icons, tools, and what is not available yet |
| [releases.md](docs/releases.md) | The step-by-step checklist for cutting a `v*` release |

Working notes for individual subsystems stay in `docs-private/`, which is untracked —
they are scratch, not published documentation.

Build and run instructions are in [readme.md](readme.md). Contribution workflow is in
[CONTRIBUTING.md](CONTRIBUTING.md); the rules agents must follow are in
[AGENTS.md](AGENTS.md).
