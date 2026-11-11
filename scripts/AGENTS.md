# Scripts

`release/` owns build-release, metadata, signing and asset validation. The single signed-package entry is `release/build-release.ps1`; dedicated installer scripts remain in `installer`, test scripts in `tests`, and probe scripts beside their tools. Do not add a second packaging wrapper or a script used only to preserve session history.

Resolve repository paths from the script location, not the caller's current directory. Preserve parameters, exit codes and artifact layout when relocating scripts. Update callers in CI, tests, docs and skills together. Signed manifest bytes must not change after signing; never print/copy private key material into artifacts.

Read [release](../docs/development/RELEASE.md) and apply [packaging validation](../tests/AGENTS.md). Local packaging does not authorize a tag or GitHub Release. Keep `Release tests` as the required CI status and preserve tag ancestry/version safeguards.
