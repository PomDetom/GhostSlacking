# Repository Guidelines

## Project Structure & Module Organization

`GhostSlacking.sln` contains five production projects under `src/`:

- `GhostSlacking.Core`: platform-neutral domain models, geometry, state coordination, settings, and recovery logic.
- `GhostSlacking.Platform`: Windows/Win32 adapters for window selection, hooks, hotkeys, and visibility.
- `GhostSlacking.App`: the Avalonia tray host, FluentAvalonia settings UI, startup integration, and logging.
- `GhostSlacking.Watchdog`: the independent recovery process used after an abnormal app exit.
- `GhostSlacking.Updater`: the independent handoff process used for verified MSI upgrades.

Automated tests live in `tests/GhostSlacking.Core.Tests` and `tests/GhostSlacking.App.Tests`; release metadata checks live in `tests/ReleaseMetadata.Tests.ps1`. Generated output belongs in `bin/`, `obj/`, or `artifacts/`; do not commit it.

## Authoritative Project Knowledge

Use `README.md` for product behavior and entry points, `docs/ARCHITECTURE.md` for implemented boundaries and recovery invariants, `docs/IMPLEMENTATION_STATUS.md` for current completion and unverified work, `docs/IMPLEMENTATION_PLAN.md` for targets and the Windows manual matrix, and `docs/DEVELOPMENT_WORKFLOW.md` for branch and release rules. Update the owning document when behavior changes; distinguish implemented behavior from targets and unverified compatibility.

## Build, Test, and Development Commands

Run commands from the repository root with the .NET 8 SDK or newer on Windows. CI uses .NET 8 to check the minimum supported SDK:

```powershell
dotnet restore GhostSlacking.sln
dotnet build GhostSlacking.sln --configuration Debug
dotnet test GhostSlacking.sln --configuration Debug
dotnet run --project src/GhostSlacking.App --configuration Debug
```

`restore` downloads dependencies, `build` compiles all projects with warnings treated as errors in production projects, `test` runs the xUnit suite, and `run` launches the tray app. Use `--configuration Release` before packaging or release validation.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, file-scoped namespaces, nullable reference types, and implicit usings. Use `PascalCase` for types, public members, and records; `_camelCase` for private fields; and `camelCase` for parameters and locals. Keep Core free of Win32 and UI dependencies, and isolate P/Invoke details in Platform. Prefer small methods, immutable records, early returns, and explicit recovery/error paths. There is no separate formatter configuration; run `dotnet format GhostSlacking.sln` when making broad formatting changes.

## Testing Guidelines

Tests use xUnit and `[Fact]`. Name tests as behavior statements in `snake_case`, for example `Target_identity_mismatch_is_not_restored_to_a_reused_handle`. Add tests for state transitions, geometry boundaries, settings normalization, and recovery safeguards. Run the full suite before submitting. Changes involving HWND lifecycle, DPI, multi-monitor behavior, focus, redraw, or privilege boundaries also require manual checks from `docs/IMPLEMENTATION_PLAN.md` on Windows 10/11.

| Change surface | Required checks |
| --- | --- |
| Core state, geometry, settings, or recovery | Relevant `GhostSlacking.Core.Tests` cases and the full Release suite. |
| Platform HWND, region, hooks, DPI, or Watchdog recovery | Relevant Core/App tests, the full Release suite, and the applicable Windows manual matrix in `docs/IMPLEMENTATION_PLAN.md`. |
| App settings, tray, notifications, or update handoff | Relevant `GhostSlacking.App.Tests` cases and the full Release suite; inspect changed UI behavior in the running app. |
| Installer, versioning, release scripts, or CI | `tests/ReleaseMetadata.Tests.ps1` and the full Release suite; validate an MSI when packaging changes. |

The CI `Release tests` job runs the PowerShell metadata checks and the Release .NET suite. Record manual results in the PR; a passing automated run does not establish real-window or clean-install compatibility.

## Commit & Pull Request Guidelines

Use Conventional Commit subjects with one of `feat`, `fix`, `perf`, `refactor`, `docs`, `build`, `ci`, `test`, `chore`, or `revert`; optional scopes and `!` are supported. For example, use `feat(hotkeys): add configurable Peek shortcut` and `chore(release): prepare 0.2.0`. Keep each commit focused. Pull requests should explain the user-visible behavior, list automated and manual validation, link relevant issues, and include screenshots for UI changes. Call out Win32 compatibility risks and any changes to restore behavior explicitly. Release PRs from `dev` to `master` must use `.github/PULL_REQUEST_TEMPLATE/release.md` and complete both release-notes language blocks.

## Branch and Release Workflow

Use `dev` for day-to-day development and keep `master` releasable. Each feature or fix must use a focused PR into `dev`; stable release changes must go through a GitHub Pull Request from `dev` to `master`. Do not routinely merge locally and push directly to either protected branch. The PR must pass the `Release tests` and metadata validation before merging. Feature PRs use Squash merge; the `dev` -> `master` release PR uses a merge commit so the release workflow can trace the merged feature PRs.

After the PR is merged, create the release tag on the resulting `master` commit using the `vMAJOR.MINOR.PATCH` format, then push only that tag:

```powershell
git fetch origin
git switch master
git pull --ff-only origin master
git tag v0.1.1
git push origin v0.1.1
```

The `v*.*.*` tag workflow is the source of truth for MSI artifacts. Stable tags use `vMAJOR.MINOR.PATCH` on `master`; beta/RC tags use `vMAJOR.MINOR.PATCH-beta.N` or `-rc.N` on `dev` and publish as GitHub Pre-releases. The workflow reruns Release tests, builds the Windows MSI, writes the SHA256 file and `release.json`, and publishes the GitHub Release. Do not create a release tag on the wrong branch, move or reuse a published tag, or replace a failed release asset under the same tag; use a new version after fixing the problem. Local packaging is for validation only.

Protect `dev` and `master` with pull requests, the required `Release tests` status check, conversation resolution, and disabled force-push/delete permissions. The single-maintainer configuration leaves an administrator emergency bypass available; use it only for recovery. The canonical settings and merge methods are in `docs/DEVELOPMENT_WORKFLOW.md`.

## Security & Recovery

Never weaken the capture-before-mutation or identity-validation safeguards. `GhostCoordinator` must register a complete snapshot before applying Ghost; a failed capture must leave the target untouched. Both main-process and Watchdog restore paths must reject a reused HWND or mismatched PID/process-start identity. Core coordinator and Watchdog protocol tests cover identity rejection and run in CI. Changes to snapshot or recovery contracts require corresponding focused tests and Windows restore checks. Avoid persisting window content or stale HWNDs. Do not require elevation by default; handle higher-privilege targets with a clear failure instead.
