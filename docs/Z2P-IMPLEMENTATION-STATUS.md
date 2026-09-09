# Zapret2Pilot / Z2P — Implementation Status

<!-- Z2P-CURRENT-STATE: architecture=v7; version=0.0.25; track=#13; work=#14 -->

Status captured for the v7 architecture rebase. This file is the concise current status; the exact prior per-milestone record is preserved at `docs/history/Z2P-IMPLEMENTATION-STATUS-through-0.0.25-v6.md`.

## Current

- `VERSION`: `0.0.25`.
- Exact v7-A base: `main@35e0356108efab6eceb96d613218bc15d2858da8`.
- Active track: #13.
- Current work: #14.
- v7 architecture target is canonical after review of this PR; broker/runtime behavior remains unimplemented.
- Real production `winws2`: still gated by #20.

## Implemented baseline — retained

| Range | State | Major implemented evidence |
|---|---|---|
| `0.0.1–0.0.6` | implemented | repo/app/core/application/storage/file safety foundations |
| `0.0.7–0.0.12` | implemented | ownership mutex/metadata, Job Object primitives, assignment seam, detector, runtime verification/workspace |
| `0.0.13–0.0.15` | implemented | typed profile documents/definitions and deterministic compiler |
| `0.0.16–0.0.19` | implemented | runtime transactions, process-host hardening, persisted runtime state |
| `0.0.20–0.0.23` | implemented | health integration, crash-loop guard, Runtime Kernel correctness hardening |
| `0.0.24` | implemented | `RuntimeKernelLoop` lifecycle closure / one reducer-driven authority |
| `0.0.25` | implemented | trusted bootstrap, platform boundaries and UI composition |

## Runtime hardening after the `0.0.25` milestone commit

The following work exists on current `main` and is part of the migration baseline even though `VERSION` remains `0.0.25`:

1. supervisor disposal performs bounded/graceful stop semantics;
2. runtime ownership mutex error/scope handling was hardened;
3. cancellation during Stop after the irreversible boundary maps to `RecoveryRequired` rather than ordinary cancellation;
4. `RuntimeCommandReceipt` removed supervisor-level lifecycle serialization as an authority and permits Stop to supersede Start;
5. `RuntimeKernelLoop` transport was split into guaranteed lifecycle delivery and coalesced observation delivery so an effect completion is not lost under observation pressure;
6. process-host affinity was replaced with the sync-only `RuntimeAffinityOwner` and a dedicated bounded owner queue;
7. `CancelOperation` + per-operation cancellation source/reason tracking makes supersede cancellation reach the in-flight effect while generation/stale checks remain authoritative.

These are **KEEP/MOVE** inputs to v7, not work to repeat.

## #14 scope disposition

Changed by #14:

- canonical architecture/roadmap/status/README alignment;
- explicit decision supersession;
- preserved historical v6/RFC sources;
- machine-readable current-state contract;
- repository-truth CI gate.

Not changed by #14:

- application/runtime code;
- runtime process behavior;
- elevation behavior;
- IPC;
- broker implementation;
- real `winws2` execution;
- compiler/runtime updater implementation.

## Evidence location

For exact historical files, tests and validation commands per `0.0.1–0.0.25`, use `docs/history/Z2P-IMPLEMENTATION-STATUS-through-0.0.25-v6.md`. Current architecture decisions live in `docs/Z2P-DECISION-LOG.md`; previous decision text is preserved at `docs/history/Z2P-DECISION-LOG-v6.md`.
