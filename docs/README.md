# Zapret2Pilot / Z2P — Documentation Index

<!-- Z2P-CURRENT-STATE: architecture=v7; version=0.0.25; track=#13; work=#14 -->

`docs/` is the repository documentation source of truth. Current architectural state is deliberately separated from historical design sources.

## Current authority order

1. `Z2P-CURRENT-STATE.json` — parseable current version/track/work item and canonical paths.
2. `Z2P-CANON.md` — current non-negotiable product/architecture rules.
3. `Z2P-ARCHITECTURE.md` — **the current master architecture**.
4. `Z2P-ROADMAP.md` — **the current master roadmap**.
5. `Z2P-DECISION-LOG.md` — active decisions and explicit supersessions.
6. `Z2P-IMPLEMENTATION-STATUS.md` — what is actually implemented now.
7. Feature/review documents — supporting evidence only.
8. `history/` — historical/superseded sources; never current instructions.

## Current direction

v7 accepts an unelevated `z2p.exe` Control Plane plus a minimal, session-scoped elevated Runtime Broker. `RuntimeKernelLoop` remains the sole runtime lifecycle authority and is moved/recomposed under that broker during the later migration; there is no second authoritative runtime state machine in the App.

No persistent Windows Service is introduced for MVP. No real production `winws2` is enabled by the documentation rebase. Rust remains deferred/evidence-driven.

## Agent rule

Implementation agents must read `Z2P-CURRENT-STATE.json` before choosing work. `Z2P-NEXT.md`, old milestone plans, archived RFCs and documents under `history/` are not authority for the next task.
