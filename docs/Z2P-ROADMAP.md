# Zapret2Pilot / Z2P — v7 Roadmap

<!-- Z2P-CURRENT-STATE: architecture=v7; version=0.0.25; track=#13; work=#14 -->
<!-- Z2P:CURRENT_MASTER_ROADMAP -->

Status: **CURRENT MASTER ROADMAP**.  
Current repository version: `0.0.25`.  
Active track: #13.  
Current work item: #14.  

The July v6 roadmap is preserved as historical design input under `docs/history/`. Its implemented work and safety invariants are retained; its post-`0.0.25` sequence is superseded by the v7 issue graph below.

## 1. Why the sequence changed

v6 reached the architectural boundary immediately before production privileged launch. v7 changes the privilege boundary before real `winws2` is enabled, while reusing the existing Runtime Kernel/compiler/recovery/trust work.

This is a migration, not a reset.

## 2. v7 execution graph

| Issue | Work | Status at this contract | Gate/result |
|---|---|---|---|
| #14 | v7-A canonical docs, ADR supersession, repository truth | **CURRENT** | v7 becomes reviewable repository contract; no behavior change |
| #15 | v7-B .NET/Avalonia servicing + presentation compatibility spike | queued | evidence-only servicing/compatibility |
| #16 | v7-C broker threat model + bounded authenticated IPC contract | blocked by architecture prep | RED contracts; no generic privileged API |
| #17 | v7-D C# session broker + existing Runtime Kernel + FakeRuntime | blocked by #16 | one authority across process boundary |
| #18 | v7-E secure Windows creation + pre-execution Job containment | blocked by #17 | fail-closed containment proof |
| #19 | v7-F immutable staging + broker revalidation + previous-good | blocked by #18 | TOCTOU/trust/retention proof |
| #20 | v7-G first controlled real `winws2` evidence | blocked by #19 | correctness/recovery/zero-residual gate |
| #21 | v7-H product/runtime rebase | blocked by #20 | capability truth, compiler evolution, Doctor/Autopilot/product work |
| #22 | v7-I optional managed C# vs NativeAOT vs Rust A/B/C | optional/late | correctness corpus first; may be deferred indefinitely |
| #23 | v7-J TUF/signing/provenance/installer/portable hardening | later | release trust |

Hard dependency shape:

```text
#14 -----+
#15 -----+--> #16 -> #17 -> #18 -> #19 -> #20 -> #21
                                             |       |
                                             |       +-> #22 optional
                                             +----------> #23 release trust
```

Do not infer the next task from file names. The orchestrator/current-state contract advances `currentWorkItem` explicitly.

## 3. Implemented baseline retained

The detailed original record is preserved at `docs/history/Z2P-IMPLEMENTATION-STATUS-through-0.0.25-v6.md`.

| Version | Implemented outcome retained by v7 |
|---|---|
| 0.0.1–0.0.6 | repository/app/core/application/storage/file-safety foundations |
| 0.0.7–0.0.12 | runtime ownership, Job primitives/assignment, detector, verified assets, runtime workspace |
| 0.0.13–0.0.15 | profile document/definition and deterministic Zapret plan compiler |
| 0.0.16–0.0.19 | runtime transactions/process-host hardening/state store |
| 0.0.20–0.0.23 | health/runtime-kernel integration, crash loop guard, correctness hardening |
| 0.0.24 | Runtime Kernel lifecycle closure: `RuntimeKernelLoop` as single authority |
| 0.0.25 | trusted bootstrap/platform boundaries/UI composition |
| 0.0.25+ hardening | supervisor supersede receipts, guaranteed lifecycle/coalesced observation transport, `RuntimeAffinityOwner`, typed CancelOperation/cancellation propagation |

No row above is removed or scheduled for greenfield reimplementation.

## 4. v6 future-intent migration

The old version-numbered future milestones are historical sequencing, but their valid product intent is carried forward:

| Historical v6 intent | v7 owner |
|---|---|
| privileged boundary / secure process launch | #16–#18 |
| durable recovery / deployment trust / bundle compatibility | existing foundations + #17–#19 |
| first real runtime developer smoke | #20 |
| runtime UX, profiles, PlanDiff, observability, probes, Doctor, Autopilot, tray/support | #21 after #20 |
| TUF runtime activation, packaging, signing/provenance | preserved design + #23 |
| RC/full MVP evidence | follows successful v7 gates; not renumbered by #14 |

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

## 6. Repository-truth gate

`docs/Z2P-CURRENT-STATE.json` is the small parseable state contract. CI runs `build/Validate-RepositoryTruth.ps1` and rejects version/current-state drift, missing/duplicate master markers, an unmarked v6 master compatibility path, an unarchived RFC pointer or a `Z2P-NEXT.md` file that can be mistaken for current instruction.

Historical documents do not carry current-master markers.
