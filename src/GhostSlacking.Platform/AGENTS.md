# Platform

Own Windows adapters: `Windows` for observation/placement/visibility, `Input` for hooks/hotkeys/message windows, `BackgroundInteraction` for sessions and control/automation/native adapters, `Installation` for current-user installation and update handshakes, `Recovery` for Watchdog transport and native restore. Platform references Core, never App.

Read [window recovery](../../docs/architecture/WINDOW_RECOVERY.md) or [Reveal/input](../../docs/architecture/REVEAL_INPUT.md) for the touched path. Keep native resource ownership and bounded failure/cleanup explicit. `SetWindowRgn` transfers ownership only on success. Validate identity before target writes; preserve physical-pixel versus workspace-placement coordinate conventions.

Background automation must preserve protected foreground and generation/input ownership. An issued mutation with an uncertain result must not be replayed through another route. Do not log document text, UIA names, key characters or clipboard data. Group COM declarations without changing their ABI, GUIDs, order or marshaling.

Platform cases live in `tests/GhostSlacking.App.Tests/Platform`. Apply the full [test requirements](../../tests/AGENTS.md), including applicable real-window/DPI/focus checks; self-owned HWND tests do not prove arbitrary app compatibility.
