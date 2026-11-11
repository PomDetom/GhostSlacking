# Repository Guidelines

## Start here

GhostSlacking is a Windows tray app for one target window: Pick → Ghost → Peek → Restore. Read [knowledge navigation](docs/README.md), then the nearest local `AGENTS.md` for the change surface. The five production projects and their dependency boundaries are described in [architecture](docs/architecture/OVERVIEW.md).

| Change surface | Local rules |
| --- | --- |
| Domain, state, geometry, settings | [Core](src/GhostSlacking.Core/AGENTS.md) |
| Win32, hooks, background input | [Platform](src/GhostSlacking.Platform/AGENTS.md) |
| Tray, settings, visual host, notifications | [App](src/GhostSlacking.App/AGENTS.md) |
| Independent recovery / upgrade process | [Watchdog](src/GhostSlacking.Watchdog/AGENTS.md), [Updater](src/GhostSlacking.Updater/AGENTS.md) |
| Automated checks and manual selection | [Tests](tests/AGENTS.md) |
| Native installation / release automation / research | [Installer](installer/AGENTS.md), [Scripts](scripts/AGENTS.md), [Tools](tools/AGENTS.md) |

## Non-negotiable boundaries

- Capture and register a complete snapshot before mutating a target. Capture failure leaves it untouched. Both app and Watchdog restore must validate HWND, PID and process-start identity; never replay a snapshot into a reused handle.
- Keep Core independent of Win32 and UI. Keep native interop in Platform; optional visuals must not be required for recovery.
- Never force-terminate App, Watchdog or a target to install, update or uninstall. Validation must not terminate a user's running instance; deliberate crash tests are limited to disposable fixtures, leaving their target and Watchdog alive. Do not default to elevation, persist window content, log input text or persist stale HWNDs.
- Preserve schema, protocol, resource and output contracts during structural moves. Do not turn a directory move into an API or behavior change.

## Work and validation

Use an isolated feature/fix/maintenance branch and a focused PR into `dev`; never routinely push directly to `dev` or `master`. Follow [development](docs/development/DEVELOPMENT.md) for style, branch protection and merge methods, and [release](docs/development/RELEASE.md) for version/tag rules. Use Conventional Commit subjects. Do not publish or tag merely because local packaging passed.

Run the complete Release suite before submission and the surface-specific checks in [tests rules](tests/AGENTS.md). Record automated and manual results and missing environments in the PR. Automated success does not establish real-window or clean-install compatibility. Repeated procedures are available as [validation](.agents/skills/ghostslacking-validation/SKILL.md) and [release preparation](.agents/skills/ghostslacking-release-preparation/SKILL.md) skills.

## Knowledge maintenance

Update the owning document with behavior changes, using [the ownership map](docs/README.md). Keep current, target, proposed and unknown states distinct. README is a product introduction; session plans, progress, ordinary verification and closeout belong in the chat/PR/build output. Keep durable constraints and consequential reasons near their owner, with one canonical home. Generated output belongs in `bin/`, `obj/` or `artifacts/`; never commit it.
