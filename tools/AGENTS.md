# Research tools

These tools are outside the production solution. `BackgroundInteractionProbe` owns isolated UIA/native/Edit/MSAA/CDP checks and read-only capability inspection; `BackgroundImeProbe` owns the standalone TSF/small-dictionary experiment; `background-focus-probe.html` records foreground-browser focus events for manual checks. Each tool README owns its commands and operational limits.

`ExclusiveInputPrototype` owns the disposable input fixtures, probe build and launcher. The production bridge and cache loader live in Platform; use the shared native build script rather than duplicating its implementation. Normal App builds carry the DLL, and experimental settings gate target injection; recovery remains independent. Keep the same-process rejection, thread/window identity scope, activation guard, bounded lease, passive behavior after host loss and uncertain-operation no-retry behavior explicit. Never run its injection against a user's target merely to obtain automated evidence.

Keep output under `artifacts/tools/<tool-name>/`, not a numbered research round. Probe launchers may terminate only their own isolated helper tree at the deadline, never App/Watchdog/user targets. CDP uses a new isolated browser profile, never a real user's session. Do not send real chat messages or record target content.

Maintain buildability when dependencies move. Run applicable self-tests and the helper timeout test after changing the interaction probe. IME compilation establishes buildability only; do not claim production input or foreground-focus compatibility. Current evidence boundaries live in [quality](../docs/quality/KNOWN_LIMITATIONS.md), manual operations in the [matrix](../docs/quality/WINDOWS_MANUAL_MATRIX.md).
