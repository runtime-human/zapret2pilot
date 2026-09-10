# Zapret2Pilot / Z2P — Implementation Status

<!-- Z2P:IMPLEMENTATION_STATUS -->

Live project version, architecture version, active track and current work item are resolved only from `docs/Z2P-CURRENT-STATE.json`. This file records implementation/evidence status and must not duplicate that dynamic tuple.

The exact prior per-milestone record is preserved at `docs/history/Z2P-IMPLEMENTATION-STATUS-through-0.0.25-v6.md`.

## v7-A implementation anchor

- Exact architecture-rebase base: `main@35e0356108efab6eceb96d613218bc15d2858da8`.
- The broker/runtime process split is architecture target only; broker, elevation IPC and real production `winws2` are not implemented by #14.
- Real production `winws2` remains gated by #20.

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

## Runtime hardening after the 0.0.25 milestone commit

The following work exists on the v7-A base line and is a migration asset:

1. supervisor disposal performs bounded/graceful stop semantics;
2. runtime ownership mutex error/scope handling was hardened;
3. cancellation during Stop after the irreversible boundary maps to `RecoveryRequired` rather than ordinary cancellation;
4. `RuntimeCommandReceipt` removed supervisor-level lifecycle serialization as an authority and permits Stop to supersede Start;
5. `RuntimeKernelLoop` transport was split into guaranteed lifecycle delivery and coalesced observation delivery;
6. process-host affinity uses the sync-only `RuntimeAffinityOwner` and a dedicated bounded owner queue;
7. `CancelOperation` + per-operation cancellation tracking makes supersede cancellation reach in-flight effects while generation/stale checks remain authoritative.

These are **KEEP/MOVE** inputs to v7, not work to repeat.

## #14 scope disposition

Changed by #14:

- canonical architecture/roadmap/status/README alignment;
- explicit decision supersession;
- preserved historical v6/RFC sources;
- machine-readable single-source current-state contract;
- repository-truth contract probe + CI validation;
- corrected parallel dependency: #15 and #16 after #14, #17 after both;
- explicit distinction between CI validation and GitHub-enforced governance; #26 tracks protection enforcement.

Not changed by #14:

- application/runtime code;
- runtime process behavior;
- elevation behavior;
- IPC/broker implementation;
- real `winws2` execution;
- compiler/runtime updater implementation.

## Governance status

The repository-truth scripts run in CI and fail the CI job on contract violations. They do not themselves make CI non-bypassable. GitHub branch/ruleset + required-check enforcement is separately tracked by #26 and must be verified from GitHub configuration before being claimed as an enforced repository gate.

## Evidence location

For exact historical files, tests and validation commands per `0.0.1–0.0.25`, use `docs/history/Z2P-IMPLEMENTATION-STATUS-through-0.0.25-v6.md`. Current architecture decisions live in `docs/Z2P-DECISION-LOG.md`; previous decision text is preserved at `docs/history/Z2P-DECISION-LOG-v6.md`.
