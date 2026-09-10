# Zapret2Pilot / Z2P — Documentation Index

<!-- Z2P:DOCUMENTATION_INDEX -->

`docs/` is the repository documentation source of truth. Dynamic orchestration state is deliberately separated from stable architecture/document roles and historical design sources.

## Authority order

1. `Z2P-CURRENT-STATE.json` — **sole dynamic authority** for project version, architecture version, active track, current work item and canonical paths.
2. `Z2P-CANON.md` — current non-negotiable product/architecture rules.
3. `Z2P-ARCHITECTURE.md` — the current master architecture.
4. `Z2P-ROADMAP.md` — the current master roadmap/dependency graph.
5. `Z2P-DECISION-LOG.md` — active decisions and explicit supersessions.
6. `Z2P-IMPLEMENTATION-STATUS.md` — implementation baseline and evidence pointers.
7. Feature/review documents — supporting evidence only.
8. `history/` — historical/superseded sources; never current instructions.

Canonical Markdown uses stable role markers; it does **not** mirror the dynamic `architectureVersion/projectVersion/activeTrack/currentWorkItem` tuple. An orchestration change therefore updates `Z2P-CURRENT-STATE.json` only, unless the architecture or roadmap content itself materially changes.

## v7 direction

v7 accepts an unelevated `z2p.exe` Control Plane plus a minimal, session-scoped elevated Runtime Broker. `RuntimeKernelLoop` remains the sole runtime lifecycle authority and is moved/recomposed under that broker during the later migration; there is no second authoritative runtime state machine in the App.

No persistent Windows Service is introduced for MVP. No real production `winws2` is enabled by the documentation rebase. Rust remains deferred/evidence-driven.

After #14, #15 and #16 are independent parallel-ready branches. #17 is their join point and must not start until both are complete.

## Validation versus enforcement

`build/Test-RepositoryTruthContract.ps1` and `build/Validate-RepositoryTruth.ps1` run in CI and validate repository truth. A passing workflow is not equivalent to GitHub-enforced protection. #26 tracks branch/ruleset protection and required-check enforcement; until its acceptance is verified from GitHub configuration, do not call the validator a non-bypassable repository gate.

## Agent rule

Implementation agents must read `Z2P-CURRENT-STATE.json` before choosing work. `Z2P-NEXT.md`, old milestone plans, archived RFCs and documents under `history/` are not authority for the next task.
