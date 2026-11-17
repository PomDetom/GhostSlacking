# Watchdog

Independent recovery executable; `Program.cs` owns process/pipe lifetime, `Diagnostics` owns its logger. References Core/Platform, never App. Keep its build/publish output compatible with App's artifact-copy targets.

Follow [window recovery](../../docs/architecture/WINDOW_RECOVERY.md): current-user pipe, fresh session, validated handshake and versioned in-memory manifest. Process a manifest once; validate target identity before recovery; clear it on completed shutdown. Never persist HWND recovery data or depend on the UI to recover.

Protocol/executor coverage is in Core's `Recovery` tests; transport coverage is in App.Tests' `Platform/Recovery`. Actual crash checks end only the self-owned App process and leave Watchdog/target alive; apply [test requirements](../../tests/AGENTS.md).
