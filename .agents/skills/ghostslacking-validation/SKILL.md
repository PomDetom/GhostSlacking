---
name: ghostslacking-validation
description: Validate a GhostSlacking change for submission by selecting the required automated and Windows checks, running applicable checks, and reporting evidence gaps. Use for validation or delivery of a repository change, not general product questions or unrelated repositories.
---

# GhostSlacking validation

1. Inspect the diff and read the root and touched directory rules. Use the repository's [test ownership map](../../../tests/AGENTS.md) to select the relevant cases and manual checks; do not assume file moves are behavior changes or that packaging moves are harmless.
2. Use [canonical commands](../../../docs/development/DEVELOPMENT.md#环境与常用命令). Run relevant cases during development and the complete Release suite before submission. Use an isolated build output if a running instance locks files; do not end the user's instance. For packaging/CI changes, run metadata and native transaction checks and validate a locally signed package through the existing release scripts.
3. Use the [Windows matrix](../../../docs/quality/WINDOWS_MANUAL_MATRIX.md) only for applicable scenarios. Separate self-owned HWND checks, isolated UI hosts and actual applications. Never operate on user documents or a live installation merely to obtain evidence.
4. Check changed documentation links, old paths, resources, output contracts and `git diff --check`. If a required check fails, investigate the concrete failure and rerun affected checks after correction; do not drop coverage or change expected behavior to make structural moves pass.
5. Report commands, results, manual coverage and unavailable environments in the PR. Update [quality boundaries](../../../docs/quality/KNOWN_LIMITATIONS.md) only when evidence changes a durable claim. Keep logs/screenshots/test artifacts out of Git and do not create a verification-history document. Passing automation is not real-window compatibility.
