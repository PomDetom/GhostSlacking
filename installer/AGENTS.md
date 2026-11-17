# Installer

Own the NSIS definition, version baseline, native `InstallerHelper.cpp`, pinned NSIS acquisition and native/package build scripts. Keep installation current-user and the helper independent of .NET. C++ builds require the Visual Studio Desktop C++ workload; NSIS is pinned to 3.11 with size/hash verification.

Read [installation architecture](../docs/architecture/INSTALLATION_UPDATES.md) and [release instructions](../docs/development/RELEASE.md). Never force-terminate App or Watchdog. Identity/SAFE checks must complete before replacing files. Retain safe-directory/ownership validation, staging/rollback and interruption recovery. Uninstall preserves user data and works without .NET.

Version comparison uses complete SemVer; preserve command-line, registry and installer protocol contracts during moves. Run [packaging checks](../tests/AGENTS.md), including native transaction tests and local signed EXE verification. Tests must use isolated directories and installer-test registration, never the live installation.
