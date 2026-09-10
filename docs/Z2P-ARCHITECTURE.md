# Zapret2Pilot / Z2P — Architecture v7

<!-- Z2P:CURRENT_MASTER_ARCHITECTURE -->

Status: **CURRENT ARCHITECTURE CONTRACT**.  
Live project/architecture/orchestration state is resolved only from `docs/Z2P-CURRENT-STATE.json`; this document does not mirror that dynamic tuple.  
Historical implementation anchor for the v7 rebase: `main@35e0356108efab6eceb96d613218bc15d2858da8`.  
Design provenance: PR #24 / archived proposed RFC.

This document describes the accepted target architecture and migration from the existing implementation. It does **not** claim that `z2p-broker.exe`, IPC or real production `winws2` already exist.

## 1. Architecture summary

```text
Control Plane — unelevated z2p.exe
  Avalonia / Application / profiles / compiler / Doctor orchestration
  probes / network context / SQLite / diagnostics / TUF download client
                      |
                      | bounded authenticated typed local IPC
                      v
Runtime Plane — session-scoped elevated z2p-broker.exe
  broker admission / accepted prepared identities
  RuntimeKernelLoop  <--- sole runtime mutation authority
  RuntimeProcessHost / ownership / privileged recovery
  fail-closed secure process creation / Job lifetime
                      |
                      v
approved immutable Runtime + Lua/Strategy payload + WinDivert

Trust / Delivery Plane
  TUF -> immutable Candidate -> activation -> Current / PreviousKnownGood
  leases / signing / provenance / release evidence
```

No persistent Windows Service is part of MVP. Runtime lifetime is bounded by the application/tray session.

## 2. Existing implementation versus target

The repository at the v7 rebase boundary already contains the Runtime Kernel and supporting safety work inside the current process composition. That code remains migration input until later v7 issues relocate it.

Explicitly retained hardening:

- `RuntimeKernelLoop` is reducer-driven and the single lifecycle authority;
- lifecycle commands/effect completions use guaranteed delivery while observations use a coalescing slot;
- operation/generation correlation rejects stale completions;
- `RuntimeCommandReceipt` allows Stop to supersede Start without another lifecycle authority;
- `CancelOperation` propagates typed supersede cancellation into in-flight effects;
- cancellation after the irreversible stop boundary surfaces recovery-required semantics;
- `RuntimeAffinityOwner` owns the dedicated bounded affinity queue used by process-host start/stop/dispose pipelines.

Target v7 moves/recomposes that same authority into `z2p-broker.exe`; the App becomes a client/projection/policy owner. No duplicate Runtime Kernel is created.

## 3. Authority and ownership

`RuntimeKernelLoop` owns production lifecycle state transitions. `RuntimeSupervisor`/future runtime client are façades; health/probes are evidence; Application coordination resolves use-case conflicts but never owns process state.

After broker migration:

```text
z2p.exe
  DesiredState / RuntimeClient / projections / policy / evidence

z2p-broker.exe
  RuntimeKernelLoop / generation / process host / ownership / Job / recovery
```

The broker may expose typed snapshot/query contracts, but the App cannot independently decide that runtime is Running.

Existing exclusive ownership, mutex, Job Object, transaction/recovery and crash-loop semantics are retained. Their privileged implementation moves with the runtime authority where required. The broker is session-scoped; loss of the owning client/session must lead to bounded rejection/cleanup according to the broker contract, not to a persistent background service.

## 4. Privilege boundary

`DEC-0003` whole-process elevation is superseded.

The unelevated Control Plane owns user-input-heavy/general application work. The privileged Runtime Plane is intentionally narrow and must not expose arbitrary process launch, shell/PowerShell execution, arbitrary file write/delete, remote download or plugin loading.

Exact broker bootstrap/session authentication and IPC wire format belong to #16. This document fixes security properties, not an unreviewed implementation.

## 5. Truth model

| Truth | Authority/meaning | Must not be confused with |
|---|---|---|
| `DesiredState` | user/policy intent | observed execution |
| `ObservedRuntimeState` | broker/runtime-generation proof | service health |
| `RuntimeLiveness` | process observed/exited/unknown | readiness or bypass success |
| `RuntimeReadiness` | startup/attachment contract satisfied | capability health |
| `ServiceCapabilityHealth` | bounded capability probes for generation/network/policy | process liveness |
| `Recommendation` | Doctor/Autopilot policy output + provenance | applied runtime truth |

Evidence is bound to `RuntimeGeneration`, `NetworkEpoch` and probe/endpoint policy identity. Stale generation/network evidence is ignored.

## 6. Runtime Kernel invariants

The migration preserves reducer authority, generation allocation/stale-completion rejection, cancellation/supersede, recovery-aware cancellation after irreversible boundaries, bounded shutdown/cleanup, `RuntimeAffinityOwner`, exclusive ownership, typed transactions/recovery, and the rule that observers/subscribers cannot become lifecycle writers.

A future broker request-admission layer may be bounded, but external IPC must never become an uncontrolled writer directly into internal kernel transport.

## 7. Secure process creation

Production requirements retained from v6 and strengthened by v7:

- Job containment is mandatory and fail-closed;
- no child instruction may execute before required containment exists;
- ordinary `Process.Start` followed by `AssignProcessToJobObject` is not the final production launcher;
- inherited handles are explicit/allowlisted;
- no shell execute or BAT/CMD production launcher;
- exact argument/environment/working-directory policy is explicit;
- launch/containment failure cannot publish false Running/Ready state.

#18 owns the concrete supported Windows creation path and proof matrix.

## 8. Execution identity and TOCTOU

`VerifiedRuntimeExecutablePath` remains useful historical/type-safety work but is not sufficient as final privileged execution identity.

```text
unprivileged candidate download
 -> TUF/metadata/hash checks
 -> immutable candidate identity
 -> broker receives typed identity, not arbitrary path authority
 -> protected immutable staging / broker revalidation
 -> PreparedRuntimeBundle + PreparedPlan
 -> secure contained launch
```

Runtime identity includes all behavior-relevant approved artifacts: `winws2`, WinDivert/runtime dependencies and relevant Lua/strategy content. Path existence or an earlier hash cannot close replace-after-check/reparse races. #19 owns protected staging/revalidation/PreviousKnownGood proof.

## 9. Trust / update invariants

Retain v6:

- `Candidate` / `Current` / `PreviousKnownGood` are immutable role references;
- failure before activation leaves Current untouched;
- PreviousKnownGood survives candidate/update failure;
- active leases prevent cleanup;
- activation is atomic/reference-based rather than in-place mutation;
- TUF remains the update metadata trust design;
- broker does not perform arbitrary network downloads;
- portable whole-app staging remains required;
- signing/SBOM/provenance/release evidence remain requirements.

Application SQLite remains Control Plane-owned. A minimal broker recovery store is allowed only if later correctness work demonstrates need; shared elevated access to the general app DB is not the default.

## 10. Product architecture retained/rebased

Capability health remains capability-specific. Traffic Impact remains typed plan evidence. Auto Doctor is bounded, hard-gated and Pareto-oriented and produces evidence/recommendations rather than runtime truth. Autopilot stays a separate bounded policy layer with current-evidence, anti-flapping, pin/manual-protection and rollback gates. Strategy Catalog stays curated/provenance-aware. Network context remains a privacy-preserving evidence/hint, never authentication/security authority.

## 11. Migration classification

| Existing subsystem/decision | v7 action | Result |
|---|---|---|
| `RuntimeKernelLoop` reducer/generation/stale completion | **MOVE** | same authority recomposed under Broker; no duplicate |
| cancellation/supersede/irreversible-boundary semantics | **KEEP / MOVE** | preserved with kernel |
| `RuntimeAffinityOwner` / process-host affinity | **KEEP / MOVE** | preserve proven semantics |
| ownership mutex, Job work, transactions, recovery | **KEEP / MOVE / ADAPT** | privileged pieces move under broker |
| verified runtime asset work | **ADAPT** | immutable prepared bundle + broker revalidation |
| deterministic profile compiler/cache | **KEEP** | remains Control Plane; semantic v2 deferred |
| SQLite/storage/history | **KEEP / ADAPT** | general DB unelevated; broker store only if proven |
| diagnostics/privacy | **KEEP / ADAPT** | privileged facts queried narrowly |
| Current/Candidate/PreviousKnownGood, leases, TUF | **KEEP / ADAPT** | identity expanded to full behavior payload |
| whole-process elevated `z2p.exe` | **SUPERSEDE** | unelevated Control Plane + elevated session broker |
| Runtime Kernel permanently inside `z2p.exe` | **SUPERSEDE** | authority relocates under broker |
| `Process.Start -> AssignProcessToJobObject` as final launch | **SUPERSEDE** | fail-closed pre-execution containment |
| prior hash/path-exists as final execution proof | **SUPERSEDE** | protected staging + broker revalidation |
| Windows Service for MVP | **KEEP: NOT USED** | no persistent service |
| IPC contract/implementation | **DEFER** | #16 then #17 |
| secure native launch | **DEFER** | #18 |
| protected staging / TOCTOU closure | **DEFER** | #19 |
| real `winws2` | **DEFER** | #20 evidence gate |
| Compiler v2 / product rebase | **DEFER** | #21 |
| Rust/NativeAOT A/B/C | **DEFER** | #22, optional |
| updater/installer production work | **DEFER** | #23 |

## 12. Dependency graph

```text
#14 canonical architecture / repository truth
  ├──> #15 stack servicing / presentation compatibility spike
  └──> #16 broker threat model / IPC contract

#15 + #16
     |
     v
    #17 C# broker + existing Runtime Kernel + FakeRuntime
     |
     v
    #18 secure launch / pre-execution Job
     |
     v
    #19 immutable staging / TOCTOU / PreviousKnownGood
     |
     v
    #20 controlled real winws2 evidence
     |
     +--> #21 product/runtime rebase --> #22 optional C#/NativeAOT/Rust A/B/C
     |
     +--> #23 release/update trust
```

#15 does **not** block #16. Both are independently eligible after #14 and may run in parallel. #17 is the join point and must not start until both are complete.

## 13. Repository truth and GitHub enforcement

`docs/Z2P-CURRENT-STATE.json` is the sole dynamic authority for architecture version, project version, active track and current work item. Canonical Markdown carries stable role markers only. Changing orchestration state does not require synchronizing README/canon/architecture/roadmap/status copies.

CI runs a single-source contract probe and repository-truth validator. That validates content, but it is **not equivalent to enforced branch protection**. GitHub branch/ruleset + required-CI enforcement is tracked by #26; until its acceptance is verified from GitHub configuration, the validator must be described as CI validation, not a non-bypassable gate.

## 14. #14 behavior boundary

#14 changes documentation, ADR/indexing, repository validation and orchestration/governance metadata only. It must not add broker/pipe/elevation/runtime behavior. After #14, #15 and #16 may be selected independently; this issue does not itself start either one.
