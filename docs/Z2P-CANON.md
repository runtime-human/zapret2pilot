# Zapret2Pilot / Z2P — Project Canon v7

<!-- Z2P:CURRENT_CANON -->

Dynamic project state is resolved only from `docs/Z2P-CURRENT-STATE.json`. This canon intentionally does not mirror the live version/track/work-item tuple.

## 1. Identity and implementation boundary

- Product: **Zapret2Pilot / Z2P**.
- Main user-facing executable: `z2p.exe`.
- Platform: Windows desktop.
- Stack: C# / .NET 10 / Avalonia.
- v7 is a migration from the implemented Runtime Kernel baseline, not a greenfield rewrite.

The v7 architecture contract describes a target boundary while the broker migration itself remains later work. Documentation/repository validation changes do not imply broker/runtime behavior already exists.

## 2. Privilege model

Target v7 process boundary:

```text
unelevated z2p.exe
  -> bounded/authenticated/typed local IPC
session-scoped elevated z2p-broker.exe
  -> RuntimeKernelLoop as sole runtime mutation authority
  -> fail-closed secure contained runtime launch
winws2 + Lua/strategy payload + WinDivert
```

Rules:

- no persistent Windows Service for MVP;
- broker lifetime is bounded by the application/tray session;
- no generic privileged process/file/shell execution API;
- broker does not download arbitrary remote content;
- Rust is not an accepted rewrite or prerequisite.

`DEC-0003` (whole-process elevated app) is superseded by v7. The no-Windows-Service part of `DEC-0002` remains active.

## 3. Runtime authority

There is exactly one production runtime lifecycle/mutation authority: **`RuntimeKernelLoop`**.

During v7 broker migration the existing reducer, generation model, cancellation logic, ownership/recovery primitives and tests are **moved/recomposed** under the broker. They are not reimplemented in parallel.

After migration `z2p.exe` owns desired state, user policy, projections and evidence. It must not contain a second authoritative runtime state machine.

Preserved hardening includes:

- Stop can supersede/cancel an in-flight Start;
- stale effect completions cannot mutate newer generations;
- lifecycle delivery is guaranteed while observations are coalesced;
- `RuntimeAffinityOwner` retains serialized process/ownership affinity semantics;
- cancellation after an irreversible stop boundary requires recovery rather than ordinary cancellation.

## 4. Truth model

The following are distinct and must stay typed/separate:

- `DesiredState` — what user/policy requests;
- `ObservedRuntimeState` — what runtime authority can prove for a generation;
- `RuntimeLiveness` — process observation only;
- `RuntimeReadiness` — startup/attachment contract satisfied;
- `ServiceCapabilityHealth` — bounded capability evidence;
- `Recommendation` — non-authoritative policy output.

A live process is **not** proof that bypass/service capabilities work. UI must never promote liveness alone to a healthy/active claim.

Health/evidence must be bound to the relevant runtime generation, network epoch and policy/endpoint versions so stale evidence cannot become current.

## 5. Profiles and compiler

Preserve:

```text
ProfileDocument -> validated ProfileDefinition -> CompiledZapretPlan -> generated runtime artifacts
```

Raw `winws2` arguments remain generated artifacts, never source of truth. Deterministic canonicalization/cache/versioning and `TrafficImpact` analysis remain architectural requirements.

Profile Compiler v2 is deferred to #21 and must prove equivalence before production adoption.

## 6. Trust, bundles and updates

Retained v6 invariants:

- upstream release != approved Z2P runtime;
- only verified/accepted immutable bundle identity can reach privileged execution;
- `Candidate`, `Current`, `PreviousKnownGood` remain distinct roles;
- failure before activation cannot replace `Current` or delete `PreviousKnownGood`;
- leased bundles/workspaces/artifacts cannot be deleted;
- TUF remains the update metadata trust design;
- portable whole-app staging remains required;
- community code never crosses directly into privileged execution.

Runtime execution identity includes the behavior-affecting payload: executable/runtime artifacts **and relevant Lua/strategy content**, not merely a `winws2.exe` path or display version.

## 7. Secure launch and TOCTOU

Production launch invariant:

> No child instruction may execute before mandatory Job containment is established.

`Process.Start -> AssignProcessToJobObject` is not a production-approved final path.

A previous SHA-256 result, `File.Exists`, or a typed verified path is not sufficient proof of the exact bytes later executed. v7 requires protected immutable staging plus broker-side revalidation/accepted prepared identity before secure launch, with reparse/replace-after-check cases addressed in #18/#19.

## 8. Storage, files, diagnostics and privacy

Preserve:

- application SQLite ownership in the unelevated Control Plane unless a separate minimal broker recovery store is proven necessary;
- WAL / foreign-key / busy-timeout discipline;
- `SafePathResolver` at user/import boundaries;
- atomic generated-file publication;
- privacy-first local diagnostics and deterministic redaction;
- no browsing-history, cookie, query-parameter or raw-packet collection by default.

The broker must not simply become an elevated owner of general application SQLite.

## 9. Product policy

Retain/rebase:

- capability-specific probes;
- Traffic Impact Analyzer;
- hard-gated/Pareto Auto Doctor;
- Autopilot as a separate bounded policy layer;
- curated Strategy Catalog;
- privacy-first diagnostics;
- network binding/context only as evidence/hint, never security authority.

Auto Doctor measures/recommends. It does not become runtime authority.

## 10. Hard architecture gates

- no local IPC implementation before #16 contract/threat-model work;
- #15 and #16 may proceed independently after #14, but #17 requires both;
- no real `winws2` before #20 correctness/recovery evidence;
- no Rust decision before optional #22 A/B/C;
- no stale `NEXT`/historical roadmap may override `Z2P-CURRENT-STATE.json` + canonical roadmap.

## 11. Documentation and governance truth

Repository: `runtime-human/zapret2pilot`.

`docs/Z2P-CURRENT-STATE.json` is the sole dynamic state authority. Canonical Markdown uses stable role markers and references that contract rather than copying its dynamic tuple. Historical v6/RFC text is preserved under `docs/history/`; supersession is recorded in `docs/Z2P-DECISION-LOG.md` rather than rewriting history.

Repository-truth scripts provide CI validation, not proof of GitHub enforcement. Enforced branch/ruleset + required-check governance is tracked in #26 and must be verified separately before being described as a non-bypassable repository gate.
