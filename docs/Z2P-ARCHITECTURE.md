# Zapret2Pilot / Z2P — Architecture v7

<!-- Z2P-CURRENT-STATE: architecture=v7; version=0.0.25; track=#13; work=#14 -->
<!-- Z2P:CURRENT_MASTER_ARCHITECTURE -->

Status: **CURRENT ARCHITECTURE CONTRACT**.  
Implementation baseline: `main@35e0356108efab6eceb96d613218bc15d2858da8`, `VERSION = 0.0.25`.  
Parent architecture track: #13.  
Design provenance: PR #24 / archived proposed RFC.  

This document describes the accepted target architecture and the migration from the existing implementation. It does **not** claim that `z2p-broker.exe`, IPC or real production `winws2` already exist.

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

No persistent Windows Service is part of MVP. The runtime session is bounded by the application/tray session.

## 2. Current implementation versus target

### Current `0.0.25+`

The existing repository already contains the Runtime Kernel and supporting safety work inside the current process composition. That code remains the implementation baseline until later v7 issues migrate it.

Post-`0.0.25` hardening that is explicitly retained:

- `RuntimeKernelLoop` is reducer-driven and the single lifecycle authority;
- lifecycle commands/effect completions use guaranteed delivery; observations use a coalescing slot;
- operation/generation correlation rejects stale completions;
- `RuntimeCommandReceipt` allows Stop to supersede Start without creating a second lifecycle state machine;
- `CancelOperation` propagates typed supersede cancellation into in-flight effects;
- cancellation after the irreversible stop boundary surfaces recovery-required semantics;
- `RuntimeAffinityOwner` owns the dedicated bounded affinity queue used by process-host start/stop/dispose pipelines.

### Target v7

The same runtime authority is moved/recomposed into `z2p-broker.exe`. The App becomes a client/projection/policy owner. No duplicate Runtime Kernel is created.

## 3. Authority and ownership

### Sole runtime mutation authority

`RuntimeKernelLoop` owns production lifecycle state transitions. `RuntimeSupervisor`/future runtime client are façades; health/probes are evidence; Application coordination resolves use-case conflicts but never owns process state.

After broker migration:

```text
z2p.exe
  DesiredState / RuntimeClient / projections / policy / evidence

z2p-broker.exe
  RuntimeKernelLoop / generation / process host / ownership / Job / recovery
```

The broker may expose typed snapshot/query contracts, but the App cannot independently decide that runtime is Running.

### Ownership and lifetime

Existing exclusive ownership, mutex, Job Object, transaction/recovery and crash-loop semantics are retained. Their privileged implementation moves with the runtime authority where required.

The broker is session-scoped. Loss of the owning client/session must lead to bounded rejection/cleanup according to the later broker contract, not to a persistent background service.

## 4. Privilege boundary

`DEC-0003` whole-process elevation is superseded.

The unelevated Control Plane owns user-input-heavy/general application work. The privileged Runtime Plane is intentionally narrow and must not expose generic operations such as arbitrary process launch, shell/PowerShell execution, arbitrary file write/delete, remote download or plugin loading.

Exact broker bootstrap/session authentication and the IPC wire format belong to #16. This document fixes only the security properties, not an unreviewed implementation.

## 5. Truth model

Runtime/product truth is split into six categories:

| Truth | Authority/meaning | Must not be confused with |
|---|---|---|
| `DesiredState` | user/policy intent | observed execution |
| `ObservedRuntimeState` | broker/runtime-generation proof | service health |
| `RuntimeLiveness` | process observed/exited/unknown | readiness or bypass success |
| `RuntimeReadiness` | startup/attachment contract satisfied | service capability health |
| `ServiceCapabilityHealth` | bounded capability probes for generation/network/policy | process liveness |
| `Recommendation` | Doctor/Autopilot policy output + provenance | applied runtime truth |

Evidence is versioned/bound to `RuntimeGeneration`, `NetworkEpoch` and probe/endpoint policy identity. Stale generation/network evidence is ignored.

## 6. Runtime Kernel invariants

The migration preserves:

- reducer as single lifecycle transition authority;
- generation allocation and stale-completion rejection;
- cancellation/supersede semantics;
- recovery-aware cancellation after irreversible boundaries;
- bounded shutdown/cleanup;
- process-host affinity (`RuntimeAffinityOwner`) until a later proven simplification replaces it;
- exclusive runtime ownership;
- typed transactions/recovery;
- no observer/subscriber path becoming a lifecycle writer.

A future broker request-admission layer may be bounded, but external IPC must never become an uncontrolled writer directly into internal kernel transport.

## 7. Secure process creation

Production requirements retained from v6 and strengthened by v7:

- Job containment is mandatory and fail-closed;
- no child instruction may execute before required containment exists;
- no final production launcher based on ordinary `Process.Start` followed by `AssignProcessToJobObject`;
- inherited handles are explicit/allowlisted;
- no shell execute or BAT/CMD production launcher;
- exact argument/environment/working-directory policy is explicit;
- launch failure/containment failure cannot publish false Running/Ready state.

#18 owns the concrete supported Windows creation path and proof matrix.

## 8. Execution identity and TOCTOU

`VerifiedRuntimeExecutablePath` remains useful historical/type-safety work but is no longer sufficient as final privileged execution identity.

Target flow:

```text
unprivileged candidate download
 -> TUF/metadata/hash checks
 -> immutable candidate identity
 -> broker receives typed identity, not arbitrary path authority
 -> protected immutable staging / broker revalidation
 -> PreparedRuntimeBundle + PreparedPlan
 -> secure contained launch
```

Runtime identity includes all behavior-relevant approved artifacts: `winws2`, WinDivert/runtime dependencies and relevant Lua/strategy content. Path existence or a hash computed earlier cannot close replace-after-check/reparse races by itself.

#19 owns protected staging/revalidation/PreviousKnownGood proof.

## 9. Trust / update invariants

Retain v6:

- `Candidate` / `Current` / `PreviousKnownGood` are immutable role references;
- failure before activation leaves Current untouched;
- PreviousKnownGood survives candidate/update failure;
- active leases prevent cleanup;
- activation is atomic/reference-based rather than in-place mutation;
- TUF is the update metadata trust design;
- broker does not perform arbitrary network downloads;
- portable whole-app staging remains required;
- signing/SBOM/provenance/release evidence remain release requirements.

Application SQLite remains Control Plane-owned. A minimal broker recovery store is allowed only if later correctness work demonstrates need; shared elevated access to the general app DB is not the default.

## 10. Product architecture retained/rebased

### Capability health

Service health is capability-specific (`WebAccess`, `MediaDelivery`, `RealtimeUdp`, `NativeClient` or later bounded equivalents). One capability cannot imply full service health.

### Traffic Impact Analyzer

Compiled plans retain/extend a typed traffic-impact result so equally effective plans can prefer narrower/safer capture and modification scope.

### Auto Doctor

Doctor is bounded, hard-gated and Pareto-oriented. It produces evidence/recommendations, tests candidates with cleanup/restore guarantees and cannot become runtime authority.

### Autopilot

Autopilot is a separate policy layer. Automatic mutation requires explicit policy, current evidence, hard gates, anti-flapping/cooldown rules, user pin/manual protection and intact rollback/recovery.

### Strategy Catalog

Stable UX uses a curated strategy catalog with provenance/risk/capability evidence. Community artifacts do not execute directly from arbitrary user/network locations.

### Network context

Network identity/context is privacy-preserving local evidence/hint for recommendation/revalidation. It is not an authentication or security authority.

## 11. Migration classification

| Existing subsystem/decision | v7 action | Result |
|---|---|---|
| `RuntimeKernelLoop` reducer/generation/stale completion | **MOVE** | Same authority recomposed under Broker; no duplicate |
| cancellation/supersede/irreversible-boundary semantics | **KEEP / MOVE** | Preserved with kernel |
| `RuntimeAffinityOwner` / process-host affinity | **KEEP / MOVE** | Preserve proven semantics; simplify only with separate evidence |
| ownership mutex, Job work, transactions, recovery | **KEEP / MOVE / ADAPT** | Privileged pieces move under broker |
| verified runtime asset work | **ADAPT** | Becomes immutable prepared bundle + broker revalidation |
| deterministic profile compiler/cache | **KEEP** | Remains Control Plane; semantic v2 deferred |
| SQLite/storage/history | **KEEP / ADAPT** | General DB remains unelevated; broker store only if proven |
| diagnostics/privacy | **KEEP / ADAPT** | UI/redaction in Control Plane; privileged facts may be queried narrowly |
| Current/Candidate/PreviousKnownGood, leases, TUF | **KEEP / ADAPT** | Identity expanded to full behavior payload |
| whole-process elevated `z2p.exe` | **SUPERSEDE** | Unelevated Control Plane + elevated session broker |
| Runtime Kernel permanently inside `z2p.exe` | **SUPERSEDE** | Runtime authority relocates under broker |
| `Process.Start -> AssignProcessToJobObject` as final production launch | **SUPERSEDE** | Fail-closed pre-execution containment required |
| prior hash/path-exists as final execution proof | **SUPERSEDE** | Protected immutable staging + broker revalidation |
| Windows Service for MVP | **KEEP: NOT USED** | No persistent service |
| IPC implementation details | **DEFER** | #16 |
| broker implementation | **DEFER** | #17 |
| secure native launch | **DEFER** | #18 |
| protected staging / TOCTOU closure | **DEFER** | #19 |
| real `winws2` | **DEFER** | #20 evidence gate |
| Compiler v2 / product rebase | **DEFER** | #21 |
| Rust/NativeAOT A/B/C | **DEFER** | #22, optional |
| updater/installer production work | **DEFER** | #23 |

## 12. Dependency graph

```text
#14 canonical docs/truth -----+
#15 stack servicing ----------+--> #16 threat model / IPC contract
                                    -> #17 C# broker + existing kernel + FakeRuntime
                                    -> #18 secure launch / pre-execution Job
                                    -> #19 staging / TOCTOU / PreviousKnownGood
                                    -> #20 controlled real winws2 evidence
                                         -> #21 product rebase
                                         -> #23 release/update trust
#22 optional C#/NativeAOT/Rust A/B/C only after contract/correctness freeze
```

## 13. #14 behavior boundary

#14 changes documentation, ADR/indexing and repository validation only. It must not add broker/pipe/elevation/runtime behavior. The first implementation work after this handoff is chosen by the orchestrator/current-state contract; this issue does not autonomously advance to #15/#16/#17.
