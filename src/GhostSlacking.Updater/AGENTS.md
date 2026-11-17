# Updater

Independent upgrade executable; `Program.cs` owns entry/lifetime and `Application` owns update execution. References Core/Platform, never App. Preserve the published `Updater` subdirectory and the command-line/handshake contract.

Read [installation and updates](../../docs/architecture/INSTALLATION_UPDATES.md). Independently validate manifest/signature/package and retain the verified package lock. Ready/Go precedes App shutdown; wait for safe App/Watchdog exit, never kill them. Success requires confirmation of the newly started version, not just installer exit. Retain required backups/results on failure.

Use `tests/GhostSlacking.App.Tests/Updater` and applicable installation/manual checks from [test requirements](../../tests/AGENTS.md). Never exercise an update against the user's live installation for a structural validation.
