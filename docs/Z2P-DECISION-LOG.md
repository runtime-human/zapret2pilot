# Zapret2Pilot / Z2P — Decision Log (current index)

This file is the current decision/supersession index. The exact pre-v7 decision log is preserved unchanged at `docs/history/Z2P-DECISION-LOG-v6.md`; history is not rewritten.

## Retained decisions

### DEC-0002 — No Windows Service in MVP — ACTIVE IN PART

**Retained:** MVP does not install/use a persistent Windows Service.  
**Superseded clause:** the old consequence “therefore use an elevated single-process app” is superseded by DEC-0052.  
**v7 consequence:** a session-scoped elevated broker is allowed/required by the accepted privilege boundary and exits with the application/tray session.

### DEC-0050 — RuntimeKernelLoop is the single lifecycle authority — ACTIVE / REBASED

`RuntimeKernelLoop` remains the sole runtime lifecycle/mutation authority. Its reducer, operation/generation correlation, stale-completion rejection, cancellation/deadline/recovery semantics and later hardening are preserved. DEC-0053 changes its process location, not the one-authority rule.

Other v6 decisions remain active unless explicitly superseded below, including typed plans/raw-args-as-artifacts, Job requirement, ownership, safe paths/atomic writes, SQLite discipline, privacy, deterministic compiler/cache, Current/Candidate/PreviousKnownGood, leases, TUF and release-trust invariants.

## Superseded decisions

### DEC-0003 — Elevated single-process app — SUPERSEDED BY DEC-0052 FROM v7

**Old decision:** `z2p.exe` runs elevated for the complete UI/application session.  
**Why superseded:** user-input, UI, storage, parsing, probing/update and runtime-authority surfaces do not all require privilege; keeping them elevated unnecessarily expands the privileged TCB.  
**History:** original text remains in `docs/history/Z2P-DECISION-LOG-v6.md`.

### DEC-0006 — Runtime Kernel inside `z2p.exe` — SUPERSEDED BY DEC-0053 FROM v7

**Old decision:** Runtime Kernel is permanently hosted inside `z2p.exe`.  
**Why superseded:** runtime authority must live inside the narrow privileged boundary while the user-facing Control Plane remains unelevated.  
**Important:** the existing kernel is moved/recomposed; it is not duplicated or rewritten from zero.

## New v7 decisions

### DEC-0052 — Unelevated Control Plane + session-scoped elevated Runtime Broker

Date: 2026-09-09.  
Status: **ACCEPTED AS v7 ARCHITECTURE CONTRACT; IMPLEMENTATION DEFERRED**.

Decision:

- `z2p.exe` is the unelevated user/policy Control Plane;
- privileged runtime mutation is owned by a minimal `z2p-broker.exe` session;
- broker is not a persistent Windows Service and cannot outlive the application/tray session as an independent product service;
- IPC is local, bounded, authenticated, typed/versioned and has no generic privileged execution primitive;
- broker does not download arbitrary remote content.

Implementation details and threat model are deferred to #16/#17.

### DEC-0053 — RuntimeKernelLoop relocates with the sole runtime authority

Date: 2026-09-09.  
Status: **ACCEPTED ARCHITECTURE / MIGRATION DECISION**.

Decision:

- retain `RuntimeKernelLoop` as one production lifecycle/mutation authority;
- move/recompose it, `RuntimeProcessHost`, privileged ownership/Job/recovery responsibilities under the Runtime Broker;
- App owns desired state, runtime client/projection, policy and evidence only;
- no second authoritative runtime state machine may remain in App.

The existing reducer, generation/stale handling, cancellation/supersede, `RuntimeAffinityOwner`, transaction/recovery and tests are migration assets.

### DEC-0054 — Separate desired, observed, liveness, readiness, capability health and recommendation truth

Date: 2026-09-09.  
Status: **ACCEPTED**.

`DesiredState`, `ObservedRuntimeState`, `RuntimeLiveness`, `RuntimeReadiness`, `ServiceCapabilityHealth` and `Recommendation` are separate contracts. Process liveness cannot be used as proof that bypass/service capability is working. Evidence is generation/network/policy-version scoped.

### DEC-0055 — Immutable behavior identity, broker revalidation and fail-closed launch

Date: 2026-09-09.  
Status: **ACCEPTED ARCHITECTURE; IMPLEMENTATION DEFERRED TO #18/#19**.

Decision:

- runtime execution identity includes behavior-relevant runtime, WinDivert and Lua/strategy content, not only `winws2.exe` path/version;
- Current/Candidate/PreviousKnownGood, leases and TUF/update failure invariants remain;
- protected immutable staging plus broker-side revalidation is required before privileged execution;
- a previous hash/path-exists check is not final execution identity proof;
- production child execution is fail-closed: mandatory Job containment exists before child instruction execution;
- ordinary `Process.Start -> AssignProcessToJobObject` is not the approved final production path.

### DEC-0056 — Single dynamic repository-state authority + stable document roles

Date: 2026-09-09; amended by PR #25 orchestration review.  
Status: **ACCEPTED**.

Decision:

- `docs/Z2P-CURRENT-STATE.json` is the **only dynamic authority** for `architectureVersion`, `projectVersion`, `activeTrack`, `currentWorkItem` and canonical document paths;
- README/canonical Markdown must not duplicate that dynamic tuple or require synchronization when orchestration selects another work item;
- stable role markers identify repository overview, documentation index, current canon, exactly one current master architecture, exactly one current master roadmap and implementation status;
- `VERSION` must equal `current-state.projectVersion`;
- CI validates canonical-path existence, role ownership, unique master markers, historical v6 supersession, archived RFC and non-canonical `Z2P-NEXT.md`;
- a dedicated contract probe mutates architecture/track/work-item values in a temporary state file and proves Markdown synchronization is unnecessary;
- `Z2P-NEXT.md` is a compatibility pointer only, never independent guidance.

Governance distinction:

- repository-truth scripts are **CI validation**;
- a passing CI workflow does not prove GitHub enforcement;
- branch/ruleset protection + required CI is tracked by #26 and must be verified from GitHub configuration before the validator may be called a non-bypassable/enforced repository gate.

Rationale: prevent both stale-document drift and the opposite failure mode where a “single source” still requires mass Markdown churn on every current-work change.

### DEC-0057 — Runtime Broker admission is process-bound, replay-resistant and bounded before Runtime Kernel dispatch

Date: 2026-09-10.  
Status: **ACCEPTED SECURITY CONTRACT BASELINE; PRODUCTION TRANSPORT DEFERRED TO #17**.

Decision:

- same-user Named Pipe access or a successful DACL check is never sufficient authorization for privileged mutation;
- the production pipe is local-only (`PIPE_REJECT_REMOTE_CLIENTS`) with an explicit bounded DACL/instance policy;
- the broker obtains the actual client PID from the connected pipe and binds admission to the expected process identity: PID + process creation time + retained process handle + Windows session + user SID + logon `AuthenticationId` + integrity level;
- PID alone is explicitly rejected as a stable identity because numeric PIDs can be reused after process lifetime;
- every AppSession uses fresh 256-bit bootstrap material and one-use broker/client nonces with HMAC-SHA256 transcript binding;
- protocol, AppSession, BrokerSession, OperationId, request sequence, RuntimeGeneration and prepared bundle/plan identities are typed and explicit;
- protocol v1 exposes only `Hello`, `GetCapabilities`, `GetRuntimeSnapshot`, `PrepareBundle`, `PreparePlan`, `StartPreparedPlan`, `StopGeneration` and `ShutdownBroker`;
- no arbitrary process, command, path, file, download, plugin, DLL or Lua execution authority exists in the privileged contract;
- framing, connection/query/mutation concurrency, ingress/response buffering, request rate, replay ledger and timeouts are explicitly bounded;
- duplicate/replayed operations are deterministic and never dispatch a second mutation;
- disconnect/reconnect cannot become lifecycle authority: already-dispatched mutations remain owned by the single `RuntimeKernelLoop`, while reconnect performs fresh admission and reconciles against retained operation/generation state;
- application hard-death handling in #17 must be driven by the retained expected-process handle and route terminal cleanup through the sole Runtime Kernel authority.

The normative threat model, limits, Microsoft Win32 semantics and residual implementation obligations are in `docs/Z2P-RUNTIME-BROKER-SECURITY-CONTRACT.md`.

## Execution-order clarification

After #14, #15 and #16 are independent branches and may proceed in parallel. #17 depends on **both** #15 and #16; then #18 -> #19 -> #20. This changes sequencing only and does not expand #15/#16 scope.

## Explicit non-decision

Rust/NativeAOT is **not** selected by v7-A. Optional A/B/C remains deferred to #22 after correctness is frozen. No Windows Service is introduced. #16 does not implement the production broker process, Runtime Kernel relocation, real `winws2`, secure child-process creation or immutable staging.
