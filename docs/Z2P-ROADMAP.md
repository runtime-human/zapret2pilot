# Zapret2Pilot / Z2P — v7 Roadmap

<!-- Z2P:CURRENT_MASTER_ROADMAP -->

Status: **CURRENT MASTER ROADMAP**.  
Live project version, architecture version, active track and current work item are resolved only from `docs/Z2P-CURRENT-STATE.json`.

The July v6 roadmap is preserved as historical design input under `docs/history/`. Its implemented work and safety invariants are retained; its future sequence is superseded by the v7 issue graph below.

## 1. Why the sequence changed

v6 reached the architectural boundary immediately before production privileged launch. v7 changes the privilege boundary before real `winws2` is enabled while reusing the existing Runtime Kernel/compiler/recovery/trust work. This is a migration, not a reset.

## 2. v7 execution graph

| Issue | Work | Dependency | Gate/result |
|---|---|---|---|
| #14 | v7-A canonical docs, ADR supersession, repository truth | root | architecture/repository contract; no runtime behavior |
| #15 | v7-B .NET/Avalonia servicing + presentation compatibility spike | #14 | evidence-only servicing/compatibility |
| #16 | v7-C broker threat model + bounded authenticated IPC contract | #14 | RED contracts; no generic privileged API |
| #17 | v7-D C# session broker + existing Runtime Kernel + FakeRuntime | **#15 and #16** | one authority across process boundary |
| #18 | v7-E secure Windows creation + pre-execution Job containment | #17 | fail-closed containment proof |
| #19 | v7-F immutable staging + broker revalidation + previous-good | #18 | TOCTOU/trust/retention proof |
| #20 | v7-G first controlled real `winws2` evidence | #19 | correctness/recovery/zero-residual gate |
| #21 | v7-H product/runtime rebase | #20 | capability truth, compiler evolution, Doctor/Autopilot/product work |
| #22 | v7-I optional managed C# vs NativeAOT vs Rust A/B/C | correctness freeze / late | optional; may be deferred indefinitely |
| #23 | v7-J TUF/signing/provenance/installer/portable hardening | later release path | release trust |

Hard dependency shape:

```text
#14
 ├──> #15 stack servicing / presentation spike
 └──> #16 broker threat model / IPC contract

#15 + #16
      ↓
     #17
      ↓
     #18
      ↓
     #19
      ↓
     #20
      ├──> #21 --> #22 optional
      └──> #23
```

#15 does **not** block #16. After #14, the orchestrator may select #15 and #16 independently and run them in parallel in separate work streams. #17 must not begin until both have completed.

The current selected work item is never inferred from this table. Resolve `currentWorkItem` only from `docs/Z2P-CURRENT-STATE.json`.

## 3. Implemented baseline retained

The detailed original record is preserved at `docs/history/Z2P-IMPLEMENTATION-STATUS-through-0.0.25-v6.md`.

| Historical implementation range | Outcome retained by v7 |
|---|---|
| 0.0.1–0.0.6 | repository/app/core/application/storage/file-safety foundations |
| 0.0.7–0.0.12 | runtime ownership, Job primitives/assignment, detector, verified assets, runtime workspace |
| 0.0.13–0.0.15 | profile document/definition and deterministic Zapret plan compiler |
| 0.0.16–0.0.19 | runtime transactions/process-host hardening/state store |
| 0.0.20–0.0.23 | health/runtime-kernel integration, crash loop guard, correctness hardening |
| 0.0.24 | Runtime Kernel lifecycle closure: `RuntimeKernelLoop` as single authority |
| 0.0.25 | trusted bootstrap/platform boundaries/UI composition |
| post-0.0.25 hardening | supervisor supersede receipts, guaranteed lifecycle/coalesced observation transport, `RuntimeAffinityOwner`, typed cancellation propagation |

No row above is removed or scheduled for greenfield reimplementation.

## 4. v6 future-intent migration

| Historical v6 intent | v7 owner |
|---|---|
| privileged boundary / secure process launch | #16–#18 |
| durable recovery / deployment trust / bundle compatibility | existing foundations + #17–#19 |
| first real runtime developer smoke | #20 |
| runtime UX, profiles, PlanDiff, observability, probes, Doctor, Autopilot, tray/support | #21 after #20 |
| TUF runtime activation, packaging, signing/provenance | preserved design + #23 |
| RC/full MVP evidence | follows successful v7 gates |

The historical v6 document remains useful for detailed requirements where a v7 ADR has not superseded them.

## 5. Mandatory invariants through the migration

- exactly one runtime mutation authority (`RuntimeKernelLoop`);
- no child instruction before Job containment;
- stale generation/completion cannot become current;
- cancellation after an irreversible boundary is recovery-aware;
- process liveness is not service health;
- Current/PreviousKnownGood/lease/TUF failure invariants remain;
- runtime identity includes relevant Lua/strategy behavior;
- no generic privileged execution protocol;
- no persistent Windows Service for MVP;
- no real `winws2` before #20;
- Rust is optional/evidence-driven only.

## 6. Repository-truth validation and enforced main gate

`docs/Z2P-CURRENT-STATE.json` is the sole dynamic state contract. Canonical documents carry stable role markers rather than duplicated state tuples. CI runs:

1. `build/Test-RepositoryTruthContract.ps1` — proves architecture/track/work-item changes can be made in the JSON contract without Markdown synchronization;
2. `build/Validate-RepositoryTruth.ps1` — checks `VERSION` ↔ state, canonical paths, unique architecture/roadmap markers, historical v6 supersession, archived RFC and non-canonical NEXT.

The scripts remain **CI validation**; enforcement is a separate GitHub repository control. #26 verified an active `main-required-ci` ruleset (id `22957088`) targeting `main`, requiring a pull request plus strict `Build and test` from GitHub Actions (`integration_id=15368`), with deletion/non-fast-forward blocked and no configured bypass actors. While that ruleset remains active, normal updates to `main` are protected by the GitHub-enforced required-CI gate.

#26 is governance closure, not an architectural dependency inserted between #14 and #15/#16.
