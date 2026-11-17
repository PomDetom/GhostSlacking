---
name: ghostslacking-release-preparation
description: Prepare or verify a GhostSlacking release by checking branch ancestry, versions, bilingual notes, required validation and signed NSIS assets. Use for release preparation or packaging validation; preparation alone does not authorize tags, publishing or merging.
---

# GhostSlacking release preparation

1. Read the [release authority](../../../docs/development/RELEASE.md) and [script rules](../../../scripts/AGENTS.md). Inspect branch, clean/dirty state, relevant PR and existing version/tag evidence before selecting an action. Stable and prerelease ancestry differ; never infer release authorization from a successful local build.
2. Check the exact version, metadata and bilingual release-note blocks using existing `scripts/release/release-metadata.ps1` modes and templates. Follow the existing PR merge methods and required `Release tests` gate; do not bypass protected branches.
3. Establish the checks required by [test rules](../../../tests/AGENTS.md). For local package validation use `scripts/release/build-release.ps1` with an explicit version and the matching external private key. Never print the key or create a production-key copy. Preserve final manifest bytes and validate signature, hash, EXE version and complete artifact set with the existing validator.
4. Stop on wrong ancestry, failed checks, absent/mismatched keys or invalid assets and report the actual blocker. Keep local outputs in artifacts. Do not move/reuse published tags or replace failed release assets; fix and select a new version under the release rules.
5. Deliver the concrete readiness result and remaining manual/environment gaps. Create a tag, publish, push or merge only when the user's requested scope already authorizes that action. This skill supplies a procedure, not additional permissions.
