# Core

Platform-neutral domain behavior only: `Coordination` owns state transitions; `Windows` owns identity/snapshots/environment models; `Reveal` owns geometry; `Input` owns input ownership; `Recovery` owns restore registration and Watchdog contracts; `Settings`, `Updates`, `Diagnostics` and `Common` own their named concerns. Do not introduce Avalonia, Windows-targeted packages or P/Invoke.

Read [window recovery](../../docs/architecture/WINDOW_RECOVERY.md) before changing state, snapshots, geometry transitions or recovery. Register the original complete snapshot before any mutation; geometry updates may replace only the accepted geometry fields, never recapture Ghost styles or identity. Preserve serialized names, schema versions and public namespaces when moving models.

Use deterministic clocks and fake window APIs for state/failure cases. Relevant tests are grouped by responsibility under `tests/GhostSlacking.Core.Tests`; also apply [test requirements](../../tests/AGENTS.md). Identity rejection and capture failure must remain covered in both coordinator and Watchdog paths.
