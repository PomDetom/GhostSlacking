# Repository Guidelines

## Project Structure & Module Organization

`GhostSlacking.sln` contains three production projects under `src/`:

- `GhostSlacking.Core`: platform-neutral domain models, geometry, state coordination, settings, and recovery logic.
- `GhostSlacking.Platform`: Windows/Win32 adapters for window selection, hooks, hotkeys, and visibility.
- `GhostSlacking.App`: the WinForms tray application, settings UI, startup integration, and logging.

Automated tests live in `tests/GhostSlacking.Core.Tests`. Architecture decisions, implementation phases, and the manual compatibility matrix are in `docs/`. Generated output belongs in `bin/`, `obj/`, or `artifacts/`; do not commit it.

## Build, Test, and Development Commands

Run commands from the repository root with the .NET 8 SDK on Windows:

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

## Commit & Pull Request Guidelines

History is currently minimal, so use concise, imperative commit subjects such as `Add configurable Peek hotkey`; keep each commit focused. Pull requests should explain the user-visible behavior, list automated and manual validation, link relevant issues, and include screenshots for UI changes. Call out Win32 compatibility risks and any changes to restore behavior explicitly.

## Security & Recovery

Never weaken the capture-before-mutation or identity-validation safeguards. Avoid persisting window content or stale HWNDs. Do not require elevation by default; handle higher-privilege targets with a clear failure instead.
