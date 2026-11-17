# Validation ownership

Tests use xUnit `[Fact]` and behavior-style `snake_case` names. Preserve the two test assemblies. Core tests mirror Core responsibilities; App.Tests separates `App`, `Platform`, `Updater` and shared `Support`. Source-reading architecture tests must resolve the actual relocated source; do not weaken assertions to accommodate a move.

| Change surface | Required checks |
| --- | --- |
| Core behavior, settings, geometry, recovery | Relevant Core cases and complete Release .NET suite |
| Platform HWND, input, DPI, region or Watchdog | Relevant Core/App cases, complete Release suite, applicable Windows manual matrix |
| App settings, tray, notifications, update handoff | Relevant App cases, complete Release suite, running UI inspection |
| Installer, versioning, release scripts or CI | ReleaseMetadata.Tests.ps1, complete Release suite; Installer.Tests.ps1 and signed NSIS validation for packaging changes |

Canonical commands and output isolation are in [development](../docs/development/DEVELOPMENT.md#环境与常用命令). CI's `Release tests` job owns metadata tests, native installer transaction tests and the Release .NET suite on SDK 8. Keep this gate and its status name intact.

Manual operations and expectations live only in [Windows matrix](../docs/quality/WINDOWS_MANUAL_MATRIX.md). Use disposable self-owned targets and isolated configuration; distinguish mocked, self-owned HWND, UI-host rendering and real app results. Record version/environment, outcomes and missing environments in the PR; maintain only decision-relevant gaps in [quality boundaries](../docs/quality/KNOWN_LIMITATIONS.md). Do not add snapshot counts or validation-history documents.
