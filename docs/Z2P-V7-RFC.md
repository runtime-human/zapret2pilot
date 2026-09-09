# Zapret2Pilot / Z2P — Architecture v7 RFC

> Status: **PROPOSED / REVIEW REQUIRED**  
> Date: **2026-09-09**  
> Baseline: `main@35e0356108efab6eceb96d613218bc15d2858da8`  
> Supersedes only the architectural assumptions explicitly called out below.  
> Existing v6 runtime-kernel, integrity, recovery, compiler, storage and release work is preserved unless this RFC says otherwise.

---

## 1. Decision summary

Z2P should **not** be rewritten from scratch.

The existing v6 architecture contains a large amount of correct and reusable work: typed domain primitives, deterministic profile compilation, runtime ownership, runtime transactions, recovery, Job Object support, the `RuntimeKernelLoop` state machine, generation/cancellation semantics, verified runtime assets, safe path/file infrastructure, SQLite WAL discipline, PreviousKnownGood/leases planning, TUF planning, Traffic Impact Analysis, capability-oriented probes, Pareto-style Auto Doctor and release evidence gates.

The main architectural change proposed by v7 is the **privilege boundary**:

```text
v6 assumption

Elevated z2p.exe
  Avalonia + Application + Storage + Runtime Kernel
                  |
                  v
                winws2

v7 proposal

Unelevated z2p.exe                         Elevated z2p-broker.exe
+--------------------------------+       +--------------------------------+
| Avalonia / UI                  |       | authenticated broker session    |
| Application use cases          | IPC   | runtime mutation authority      |
| Profiles / compiler            +------>| RuntimeKernelLoop               |
| Auto Doctor orchestration      |       | exact runtime generation        |
| probes / network context       |       | secure process creation         |
| SQLite / history / settings    |       | Job Object / process lifetime   |
| TUF/download candidate client  |       | broker-side revalidation        |
+--------------------------------+       +---------------+----------------+
                                                        |
                                                        v
                                                    winws2 + Lua
                                                        |
                                                     WinDivert

Independent Trust / Delivery Plane:
TUF metadata -> immutable bundles -> Candidate / Current / PreviousKnownGood
-> signing / provenance / release evidence
```

The broker is **session-scoped**, not a persistent Windows Service. Runtime lifetime remains bounded by the application/tray session.

Rust is **not** proposed as a Z2P rewrite. After the broker contract is stable, the privileged broker may be benchmarked as:

1. managed C#;
2. C# NativeAOT;
3. Rust.

The choice must be evidence-driven.

---

## 2. Why v7 is needed now

The current roadmap is at the ideal migration point: `0.0.25` is implemented and the next major milestone is `0.0.26 — Privileged Boundary & Secure Process Launch`. Real production `winws2` launch is still gated.

That means the project can correct the process boundary **before** real runtime launch, Auto Doctor candidate execution, updater activation, public packaging and user-facing runtime state depend on the old whole-process-elevated assumption.

The old decision was reasonable for an early MVP:

- no Windows Service;
- one elevated desktop process;
- no repeated UAC while the app stays open;
- compensate with allowlists, safe paths, Job Objects, runtime ownership and recovery.

The product has since grown beyond that original trust surface. An elevated UI process would eventually contain:

- Avalonia/XAML/resources;
- presentation subscriptions and navigation;
- Generic Host and DI;
- SQLite;
- JSON/profile/hostlist parsing;
- diagnostics and support bundle logic;
- HTTP/probe/update logic;
- user-controlled file inputs;
- runtime mutation authority;
- low-level Windows process/driver interop.

The v7 principle is therefore:

> **Privilege only the runtime authority that requires privilege, not the entire application that happens to control it.**

---

## 3. Goals

v7 must provide all of the following.

### 3.1 Security / trust

- unelevated user-facing application by default;
- minimal privileged Trusted Computing Base (TCB);
- no generic privileged command execution surface;
- authenticated local-only IPC;
- exact client/session binding;
- no raw path or arbitrary executable authority crossing IPC;
- broker-side revalidation before privileged execution;
- fail-closed process containment;
- immutable/content-addressed runtime bundle identity;
- no update/download logic in the privileged broker;
- Current / Candidate / PreviousKnownGood with explicit leases;
- release signing, provenance and evidence as part of architecture, not post-processing.

### 3.2 Runtime correctness

- exactly one production runtime authority;
- retain `RuntimeKernelLoop` as single lifecycle state authority;
- preserve generation and stale-completion rejection;
- preserve cancellation / supersede semantics;
- no child instruction execution before Job containment;
- no orphan `winws2` after app/broker crash;
- no automatic unsafe takeover after crash;
- deterministic recovery state;
- runtime state must not depend on UI lifetime races.

### 3.3 Product correctness

- process liveness must not be presented as service health;
- desired state, observed runtime state, health evidence and recommendation remain distinct;
- Auto Doctor produces evidence/recommendation; it is not itself runtime authority;
- Autopilot is a policy layer above Doctor and may mutate only under explicit bounded rules;
- network-specific recommendations are hints/evidence, not universal truth;
- UI must explain what is running, why it is considered healthy and why a profile was selected.

### 3.4 Maintainability

- reuse existing C# runtime/domain work;
- avoid duplicating a second runtime state machine in App and Broker;
- avoid a large service-oriented architecture;
- keep project count bounded;
- keep IPC protocol small, versioned and fuzzable;
- enable future broker implementation experiments without rewriting the application plane.

---

## 4. Non-goals

v7 does **not** propose:

- a persistent Windows Service for the MVP;
- runtime continuation after the application/tray session exits;
- a VPN/proxy replacement for zapret2;
- a replacement packet engine for `winws2`;
- arbitrary plugin execution in the client;
- downloading arbitrary community EXE/BAT/Lua directly into the privileged boundary;
- cloud telemetry;
- browsing-history collection;
- packet capture storage;
- a Rust rewrite of the application;
- a generic remote-control API;
- multi-user/multi-session remote administration.

---

## 5. Existing architecture that remains authoritative

The following v6 decisions are retained unless implementation work proves a concrete defect.

### Runtime

- `RuntimeKernelLoop` remains the single lifecycle authority.
- Runtime mutations are serialized and generation-aware.
- stale effect completions cannot overwrite newer generations.
- cancellation after an irreversible boundary maps to recovery-required semantics, not ordinary cancellation.
- Job Objects remain mandatory.
- process ownership remains exclusive.
- crash-loop protection remains typed and bounded.

### Profiles / compiler

```text
ProfileDocument
    -> ProfileDefinition
    -> CompiledZapretPlan
    -> runtime artifact
```

Raw `winws2` arguments are generated artifacts, not source of truth.

Content-addressed compiler/cache rules remain.

### Files / storage

- `SafePathResolver` remains mandatory at user/import boundaries.
- generated files remain atomic.
- SQLite remains outside UI and Core.
- WAL / foreign-key / busy-timeout discipline remains.
- user overlays survive built-in-data updates.

### Trust / update

- upstream release != approved Z2P Runtime Bundle;
- update failure must not change Current;
- PreviousKnownGood must survive candidate/update failure;
- leased artifacts cannot be deleted;
- TUF remains the planned update metadata trust system;
- community strategy intake belongs to release infrastructure, not direct client execution.

---

## 6. Architectural planes

v7 makes three planes explicit.

## 6.1 Control Plane — `z2p.exe` (unelevated)

Owns user-facing and policy work:

- Avalonia shell;
- ViewModels / presentation projections;
- Application commands and queries;
- profile editing;
- profile compilation;
- strategy recommendation policy;
- Auto Doctor orchestration;
- network-context observation;
- service probing;
- SQLite settings/history/evidence;
- diagnostics UI and redaction;
- TUF metadata client;
- network downloads into unprivileged candidate cache;
- support bundle generation;
- update UX;
- tray UX.

The Control Plane may **request** privileged mutations but does not own the privileged runtime process tree.

## 6.2 Runtime Plane — `z2p-broker.exe` (elevated, session-scoped)

Owns only operations that require privileged authority or must be inside the runtime trust boundary:

- broker session authentication;
- accepted bundle identity;
- broker-side integrity revalidation;
- runtime generation allocation;
- `RuntimeKernelLoop`;
- runtime ownership mutex/lease;
- secure process creation;
- Job Object lifetime;
- exact process observation;
- exact stop/cleanup;
- recovery reconciliation for privileged runtime state;
- protected staging promotion where elevation is required.

It does **not** own:

- HTTP downloads;
- general settings;
- user profile editing;
- arbitrary file browsing;
- arbitrary process execution;
- plugin loading;
- generic shell execution;
- UI;
- application SQLite.

## 6.3 Trust / Delivery Plane

Logical plane spanning release infrastructure and local immutable stores:

- application bundles;
- runtime bundles;
- strategy bundles;
- built-in data packs;
- probe policy assets;
- TUF metadata;
- signatures;
- SBOM/provenance/attestations;
- Candidate / Current / PreviousKnownGood;
- leases and garbage collection.

---

## 7. Authority model

The project must stop using the word `state` for semantically different classes of truth.

Four independent categories are required.

### 7.1 DesiredState

What the user/policy wants:

```text
Stopped
Running(ProfileId, CompiledPlanHash)
```

### 7.2 ObservedRuntimeState

What the privileged authority can prove about the current runtime generation:

```text
NoGeneration
Starting(generation)
RunningObserved(generation, pid, bundle, plan)
Stopping(generation)
Stopped(generation)
RecoveryRequired(...)
```

### 7.3 HealthEvidence

What bounded probes say about service capabilities for a specific runtime/network context.

Health evidence is always keyed by identities such as:

- `RuntimeGeneration`;
- `NetworkEpoch`;
- `ProbePolicyVersion`;
- `EndpointManifestVersion`;
- timestamps/deadlines.

### 7.4 Recommendation

A non-authoritative policy output:

```text
RecommendedProfile
Reason/provenance
Evidence set
Confidence/limits
```

No recommendation becomes runtime truth until an explicit mutation is accepted by the runtime authority.

---

## 8. Identity model

At minimum the following identities should exist or have equivalents:

```text
AppSessionId
BrokerSessionId
OperationId
RuntimeGeneration
ApplicationBundleId
RuntimeBundleId
StrategyBundleId
CompiledPlanHash
NetworkEpoch
ProbePolicyVersion
EndpointManifestVersion
```

Rules:

1. every runtime mutation is bound to a `BrokerSessionId` and `OperationId`;
2. every observation is bound to a `RuntimeGeneration`;
3. every service-health result is bound to runtime generation + network epoch + policy version;
4. stale generations cannot become current merely because their operation completed later;
5. immutable bundle identities are content/trust identities, not display version strings;
6. raw paths are never cross-plane identity.

---

## 9. Broker lifecycle

The broker is not a service.

Target lifecycle:

```text
user launches z2p.exe unelevated
        |
        +-- operations not requiring runtime privilege work normally
        |
        +-- first privileged runtime request
                |
                v
          explicit UAC elevation
                |
                v
          z2p-broker.exe
                |
          authenticated BrokerSession
                |
          runtime operations
                |
          UI/tray session exits or broker session lease lost
                |
                v
          reject new mutation
          stop/contain runtime
          close Job authority
          broker exits
```

Whether the broker starts at application launch or lazily on first runtime mutation is an implementation decision. v7 prefers lazy elevation if it does not materially complicate recovery/onboarding.

---

## 10. IPC security contract

Named Pipe is the preferred initial transport on Windows because the product is local-only and requires Windows identity/process integration.

The protocol must be treated as an untrusted-input boundary even though the client is local.

### 10.1 Pipe requirements

- local machine only;
- reject remote pipe clients;
- explicit security descriptor/DACL;
- no Everyone/anonymous mutation access;
- bounded message size;
- bounded queue depth;
- explicit protocol version;
- request deadline;
- request ID / operation ID;
- strict enum/length validation;
- unknown fields/commands handled according to versioning policy;
- no deserialize-to-arbitrary-type behavior;
- no reflection-driven command resolution from wire type names.

### 10.2 Client binding

The broker must not assume that "same Windows user" means "trusted UI instance".

Proposed handshake:

1. `z2p.exe` creates cryptographically random session material and an `AppSessionId`.
2. `z2p.exe` starts the broker through Windows elevation and passes the expected UI PID plus bootstrap session data using a channel that is not reusable as generic command input.
3. broker opens/retains a synchronization handle to the expected UI process where practical;
4. broker creates the local-only Named Pipe;
5. after pipe connection, broker obtains the **actual connected client PID from Windows**;
6. actual client PID must match the expected UI PID;
7. process/session/user identity is validated;
8. executable/publisher identity may be verified for installed builds;
9. a `BrokerSessionId` is established;
10. only then are mutation commands accepted.

The exact bootstrap secret transport and installed/portable identity policy require a dedicated threat-model issue before implementation.

### 10.3 Protocol surface

Preferred narrow command family:

```text
Hello / Negotiate
GetCapabilities
GetRuntimeSnapshot
PrepareBundle
PreparePlan
StartPreparedPlan
StopGeneration
ShutdownBroker
```

Potentially acceptable only if justified:

```text
CollectPrivilegedDiagnostics
RecoverRuntime
```

Forbidden protocol design:

```text
RunProcess(path, args)
ExecuteCommandLine(...)
RunPowerShell(...)
WriteArbitraryFile(path, bytes)
DeleteArbitraryPath(path)
Download(url)
LoadPlugin(path)
LoadLuaFromUserPath(path)
```

The broker is an authority over **typed prepared identities**, not a privileged utility API.

---

## 11. Broker ingress and runtime kernel transport

The existing Runtime Kernel has guaranteed lifecycle delivery and coalesced observation behavior. That internal contract should remain.

However, external IPC input must never write directly into an unbounded internal lifecycle channel.

Required outer boundary:

```text
Named Pipe
   |
   v
bounded decode / validation
   |
   v
bounded broker request admission
   |
   +-- one active mutation or explicitly defined compatible set
   +-- duplicate/idempotency handling
   +-- deadline/cancellation mapping
   |
   v
Runtime facade
   |
   v
RuntimeKernelLoop
```

The system must have explicit tests for:

- oversize frames;
- fragmented reads;
- duplicate operation IDs;
- stale operation IDs;
- stop superseding start;
- client disconnect during start;
- broker shutdown during operation;
- slow reader/writer;
- malformed version/enum/length;
- request flood/backpressure;
- stale completion after new generation.

---

## 12. Runtime Kernel relocation

v7 must avoid two runtime authorities.

Target migration:

```text
before
z2p.exe
  RuntimeSupervisor / RuntimeKernelLoop
  RuntimeProcessHost

v7
z2p.exe
  RuntimeClient
  RuntimeProjection
  RuntimeUseCases

z2p-broker.exe
  RuntimeKernelLoop
  RuntimeProcessHost
  RuntimeAuthority
```

The existing reducer, generation model, cancellation logic, ownership logic, recovery primitives and process-host tests should be moved/reused rather than reimplemented.

UI-side state is a projection of broker-authoritative snapshots plus Control Plane desired state/evidence. It cannot independently decide that the runtime is Running.

---

## 13. Secure process creation

The production `winws2` path must be fail-closed.

Production requirements:

- no ordinary `Process.Start -> AssignProcessToJobObject` window;
- create process using an approved native launch path;
- configure Job Object before child code is allowed to run, using Windows supported creation/attribute mechanisms;
- explicit inherited-handle allowlist;
- no accidental broad handle inheritance;
- exact argument tokenization/quoting contract;
- explicit working directory;
- explicit environment policy;
- no shell execute;
- no `.cmd`/`.bat` production launcher;
- child executable must be a broker-accepted verified runtime identity;
- Job Object creation/association failure => **runtime launch failure**;
- readiness failure => bounded cleanup and no false Running state.

The current best-effort containment model seen in some comparable GUI projects is explicitly rejected.

---

## 14. TOCTOU / filesystem identity

`VerifiedRuntimeExecutablePath` is useful as a type-system boundary but path existence is not sufficient privileged proof.

v7 must close the gap between "verified earlier" and "executed now".

Required design direction:

```text
unprivileged download candidate
       |
       v
TUF/metadata/hash verification
       |
       v
immutable candidate identity
       |
       v
broker receives candidate identity, not arbitrary executable path
       |
       v
broker revalidates/promotes into protected immutable content-addressed store
       |
       v
PreparedRuntimeBundle
       |
       v
secure launch from accepted identity
```

Dedicated design work must assess:

- reparse points/junctions/symlinks;
- replace-after-check;
- directory ownership/ACLs;
- file handles vs paths;
- final path verification where used;
- file ID / volume identity where useful;
- Authenticode/upstream signature policy where available;
- content hash revalidation;
- protected staging ACL policy;
- installed vs portable trust differences.

Do not claim TOCTOU closure from SHA-256 alone if the file later executed can be replaced after verification.

---

## 15. Bundle model

v7 makes the following artifact classes explicit.

### 15.1 ApplicationBundle

Contains Z2P application executables/assets whose trust matters to privileged bootstrap, including broker identity.

### 15.2 RuntimeBundle

Contains the exact approved zapret2 runtime payload needed for execution, such as:

- `winws2`;
- WinDivert artifacts;
- required zapret2 runtime/base Lua assets;
- runtime capability manifest;
- compatibility metadata.

### 15.3 StrategyBundle

Contains curated typed strategy definitions and the Lua programs/assets required to implement them.

Important invariant:

> `winws2.exe` version alone is not complete runtime behavior identity. Exact Lua/strategy content participates in execution identity.

### 15.4 DataPack

Immutable built-in data such as shipped hostlists/reference data.

### 15.5 UserOverlay

User-owned additions/exclusions/settings. It is never silently overwritten by built-in updates.

### 15.6 ProbePolicy

Versioned bounded capability checks and endpoint policy used by Doctor/health evaluation.

---

## 16. Candidate / Current / PreviousKnownGood

Activation must be immutable and pointer/reference based.

```text
Candidate
   |
 verify
   |
 stage immutable
   |
 compatibility gate
   |
 activate atomically
   |
 Current --------------------+
   |                          |
   +---- previous ----------> PreviousKnownGood
```

Rules:

- failure before activation leaves Current untouched;
- activation never mutates Current contents in place;
- PreviousKnownGood is retained according to policy;
- running/diagnostic/update operations acquire leases;
- garbage collection cannot remove leased bundles;
- failed/partial candidates are cleaned without deleting previous-good;
- TUF/update metadata failure does not change runtime state;
- broker does not download remote candidates itself.

---

## 17. Storage ownership

Application SQLite remains in Control Plane.

The broker should **not** simply open the same general application database elevated.

Preferred rule:

- application history/settings/evidence DB: unelevated Control Plane owner;
- privileged broker durable state: minimal dedicated store only if necessary for crash/recovery correctness;
- no shared mutable SQLite database between UI process and broker unless an evidence-backed design proves it is necessary.

Possible broker durable information:

- accepted runtime bundle reference;
- current runtime generation/recovery marker;
- exact ownership/recovery metadata.

Keep the broker store small enough to reason about independently.

---

## 18. Health semantics

Process liveness is not service health.

The current liveness monitor concept should become explicitly named/typed as runtime observation rather than user-visible health.

Suggested layers:

```text
RuntimeLiveness
  process observed / exited / unknown

RuntimeReadiness
  process attached / startup contract satisfied

ServiceCapabilityHealth
  WebAccess
  MediaDelivery
  RealtimeUdp
  NativeClient
  ...bounded versioned capabilities...
```

A UI state such as `Обход активен` requires policy-defined evidence and must never be inferred solely from `Process.HasExited == false`.

Health snapshots must carry generation/network/policy identity so results from a previous runtime or network cannot overwrite current health.

---

## 19. Profile Compiler v2

The existing profile/compiler architecture remains, but its semantic model should become closer to zapret2 rather than treating the runtime as mostly a flat argument list.

Target conceptual model:

```text
ProfileDefinition
  CapturePolicy
  TrafficClassifier[]
  HostScope
  StrategyProgram
    LuaInstance[]
  RuntimePolicy
  CompatibilityContract
```

A `LuaInstance` or equivalent typed node may contain:

```text
FunctionId
TypedArguments
PayloadFilter
InRange
OutRange
OrchestratorRole
Order
```

Raw argv remains a generated artifact.

Benefits:

- semantic validation;
- deterministic canonicalization;
- meaningful PlanDiff;
- compatibility/risk analysis;
- explainable UI;
- candidate comparison;
- ability to reject unsafe/unsupported combinations before launch.

No production adoption is required until equivalence tests against known-good existing plans pass.

---

## 20. Traffic Impact Analyzer

`TrafficImpact` becomes a required compiled-plan output, not optional UI metadata.

At minimum it should describe bounded fields such as:

```text
CaptureBreadth
TCP/UDP port scope
host scope breadth
QUIC impact
all-traffic/catch-all behavior
Lua instance count/classes
autohostlist behavior
compatibility risk
expected WinDivert filter breadth
```

Selection policy should prefer equally effective strategies that capture/modify less traffic.

---

## 21. Auto Doctor v2

Doctor is an evidence generator and recommender.

Required lifecycle:

```text
capture exact original state
        |
baseline without bypass where meaningful
        |
Candidate A -> start -> probe -> cleanup
Candidate B -> start -> probe -> cleanup
Candidate C -> start -> probe -> cleanup
        |
hard correctness gates
        |
Pareto frontier
        |
confirmation of top candidates when policy requires
        |
Winner / NoWinner / ReviewRequired
        |
restore exact original state
        |
Recommendation
```

Hard gates dominate score/latency.

Examples:

- required capability failed => candidate cannot win;
- security/certificate integrity failure => candidate cannot win;
- cleanup proof failed => Doctor stops and surfaces recovery;
- candidate result from stale network epoch/generation => ignored;
- broader/more invasive strategy cannot win solely for negligible latency gain.

Doctor must never leave a test candidate running merely because testing was cancelled.

---

## 22. Autopilot

Autopilot is separate from Doctor.

```text
health degradation
      |
policy decides whether revalidation is justified
      |
bounded Doctor invocation
      |
recommendation
      |
optional apply if policy explicitly permits
```

Default behavior remains recommendation-first.

Automatic mutation is allowed only when all required policy conditions pass, such as:

- Autopilot explicitly enabled;
- current profile not pinned/manual-locked;
- network epoch current;
- candidate previously trusted/confirmed according to policy;
- hard gates pass;
- Current/PreviousKnownGood intact;
- rollback/recovery path available;
- anti-flapping/cooldown budget allows transition.

---

## 23. Network context

Z2P should remember evidence by network context without turning a network fingerprint into security authority.

Use a privacy-preserving local `NetworkContextHint`/`NetworkEpoch`, derived from bounded local characteristics such as:

- active interface identity/type;
- default gateway hint;
- prefix/config digest;
- DNS configuration digest;
- proxy presence;
- VPN/tunnel presence.

Store only the minimum local identifier needed for policy/history.

Rules:

- network match may preselect/suggest a previously successful profile;
- it is not proof that conditions are unchanged;
- revalidation policy still applies;
- network identity collection must remain local/privacy-first.

---

## 24. Compatibility and conflict intelligence

Add a typed compatibility model instead of generic "engine failed" messages.

Candidate states include, where evidence supports them:

```text
ForeignBypassConflict
WinDivertConflict
DriverStopPending
SecurityProductInterference
AntiCheatInterference
VPNOrTunnelPresent
SystemProxyPresent
RuntimeKilledExternally
RuntimeCrashed
```

Rules:

- detection is non-destructive;
- Z2P does not disable antivirus/anti-cheat;
- Z2P does not hide WinDivert;
- Z2P does not kill arbitrary third-party bypass tools automatically;
- Z2P explains what was observed and gives safe remediation guidance.

---

## 25. UI / information architecture

The main navigation should describe user goals rather than internal subsystems.

Proposed primary navigation:

```text
Обзор
Автопилот
Профили
Проверка
Журнал
Настройки
```

Advanced disclosure may expose:

```text
Списки и правила
Runtime
Strategy Bundles
Integrity
Generated Plan
Raw diagnostics
```

### Dashboard truth hierarchy

The dashboard should separate:

```text
Runtime
Запущен / Остановлен / Восстановление

Работоспособность
capability evidence summary

Профиль
selected/active profile

Актуальность
when and under which generation/network it was checked
```

### Explainability

For an automatically recommended/selected profile, show concise reason/provenance:

```text
Все обязательные проверки пройдены.
Результат подтверждён.
Профиль обрабатывает меньше трафика, чем альтернативы.
Более агрессивные варианты не дали измеримого преимущества.
```

### Integrity view

Provide a user-visible trust view for:

- Z2P application/broker identity;
- runtime bundle/version/hash;
- WinDivert identity/signature where available;
- Strategy Bundle;
- update channel;
- provenance/verification status.

---

## 26. Presentation framework decision

Do not mix ReactiveUI and CommunityToolkit.Mvvm ViewModels in production.

Before large UI expansion, run one bounded A/B spike on the same representative Dashboard workflow:

```text
ReactiveUI current supported line
vs
CommunityToolkit.Mvvm current supported line
```

Compare:

- LOC;
- subscription/disposal complexity;
- test ergonomics;
- scheduler/thread-affinity clarity;
- allocations where material;
- source-generated binding/tooling compatibility;
- maintainability.

Keep ReactiveUI if it materially simplifies the projection model. Migrate only if evidence shows a clear reduction in complexity/risk.

This is not allowed to block the broker/security work.

---

## 27. .NET / Avalonia / dependency modernization

Run a dedicated stack-servicing PR before or alongside v7 bootstrap.

Rules:

- use current supported .NET 10 servicing SDK/runtime;
- evaluate current supported Avalonia/ReactiveUI compatible versions from official upstream sources at execution time;
- no speculative pre-release framework adoption in the privileged-boundary work;
- central package management and locked restore remain;
- dependency audit remains blocking for security-critical native dependencies;
- warnings-as-errors/analyzers remain;
- benchmark any NativeAOT claim rather than assuming smaller/faster is always better.

Exact package versions belong in the execution PR/issue so the RFC does not become stale from servicing updates.

---

## 28. Rust decision

Rust is optional and narrowly scoped.

Do **not** rewrite Core/Application/UI/Storage in Rust.

After the IPC contract and broker behavior are proven in C#, create an evidence spike:

```text
A: managed .NET broker
B: .NET NativeAOT broker
C: Rust broker
```

Compare:

- privileged TCB/dependency graph;
- binary size;
- startup time;
- idle working set/private memory;
- IPC latency/throughput (secondary to correctness);
- Win32 API correctness/ergonomics;
- handle/resource lifetime safety;
- testability/fuzzing;
- signing/packaging complexity;
- cross-language contract maintenance;
- developer/CI complexity;
- long-term maintenance cost.

Correctness gates first. A smaller binary is not a win if lifecycle/security behavior is harder to prove.

---

## 29. Broker mitigation profile

A separate privileged broker enables a stricter process mitigation profile than the UI.

The implementation issue should evaluate applicable Windows mitigations for:

- broker itself;
- child `winws2` separately.

Do not blindly enable every mitigation. Validate compatibility with:

- runtime loading;
- Lua/runtime assets;
- WinDivert;
- diagnostics;
- supported Windows versions.

Mitigation incompatibility must fail safely and be documented.

---

## 30. Packaging model

Preferred initial distribution remains a signed Win32 desktop application with a signed installer and protected install root.

Do not force MSIX solely for modernity.

Requirements:

- signed `z2p.exe`;
- signed `z2p-broker.exe`;
- signed installer/bootstrap executables;
- protected installed application root;
- whole-app staging rules for portable distribution;
- no elevated execution directly from an untrusted portable extraction root;
- installer/uninstaller preserves user data according to explicit policy.

Package identity / sparse/external-location packaging may be evaluated later if it provides a concrete benefit.

---

## 31. Release / supply-chain architecture

Stable release requires evidence, not only CI green.

Release pipeline target:

```text
protected reviewed source
 -> locked restore
 -> build/test/security gates
 -> exact commit SHA
 -> SBOM
 -> hashes/manifests
 -> signing
 -> artifact attestation/provenance
 -> release evidence pack
```

The project should progress toward hardened provenance comparable to modern SLSA-style requirements, but no compliance level should be claimed until its exact requirements are met.

Branch protection/rulesets should be enabled before stable release so `main` is not a direct-write production source of truth.

---

## 32. Repository truth / documentation gate

The repository currently has historical documents that can drift from the canonical roadmap.

v7 requires an explicit truth hierarchy.

Proposed:

```text
1. accepted current architecture/roadmap document
2. ADR / decision log
3. implementation status
4. feature design docs
5. README summary
6. historical/superseded plans
```

Rules:

- every superseded document carries a visible superseded banner/link;
- README current milestone is CI-checked against a machine-readable or canonical source;
- `Z2P-NEXT.md`-style stale instructions are removed or generated;
- architecture test/CI checks prevent multiple documents from claiming to be the current master plan;
- implementation agents must be pointed to current canonical docs.

---

## 33. Decision-log changes

Do not erase history. Supersede old decisions.

### Retain

- `DEC-0002 — No Windows Service in MVP`;
- Job Objects mandatory;
- runtime ownership primitives;
- no network router in Z2P;
- raw args are generated artifacts;
- deterministic plan cache;
- SQLite/file safety/privacy decisions.

### Supersede

`DEC-0003 — Elevated single-process app`

with:

> **Unelevated Control Plane + session-scoped elevated Runtime Broker.**

`DEC-0006 — Runtime Kernel inside z2p.exe`

with:

> **Runtime mutation authority and RuntimeKernelLoop live in the privileged broker process; z2p.exe owns desired state, projections, policy and evidence.**

### Add

- broker session and IPC trust boundary;
- immutable bundle identity and broker-side revalidation;
- desired/observed/health/recommendation truth separation;
- broker never downloads remote content;
- fail-closed containment;
- Rust/NativeAOT only by evidence spike.

---

## 34. Project layout target

Keep the solution bounded. A possible target:

```text
Zapret2Pilot.Core
Zapret2Pilot.Contracts
Zapret2Pilot.Application
Zapret2Pilot.Engine.Zapret2
Zapret2Pilot.Storage
Zapret2Pilot.Infrastructure / Platform.Windows
Zapret2Pilot.Runtime
Zapret2Pilot.Broker
Zapret2Pilot.App
```

Do not split one assembly per conceptual noun.

`Zapret2Pilot.Contracts` must remain dependency-light and safe to consume from both App and Broker.

---

## 35. Migration strategy from `main@35e0356`

No greenfield rewrite.

### Phase A — Architecture rebase

- accept/reject this RFC;
- write ADRs;
- publish v7 roadmap;
- mark v6 assumptions superseded, while preserving historical text;
- fix repository truth drift;
- perform stack servicing.

**No runtime behavior change.**

### Phase B — Broker contracts / RED first

Add:

- `Zapret2Pilot.Contracts`;
- protocol DTO/versioning;
- transport abstractions;
- threat model;
- malformed/oversize/stale/duplicate tests;
- fake broker/client harness.

No real elevation and no real `winws2`.

### Phase C — C# broker with FakeRuntime

- create `Zapret2Pilot.Broker`;
- move/recompose runtime authority under broker;
- keep FakeRuntime-only gate;
- prove process/session lifecycle;
- prove UI process loss -> runtime cleanup;
- prove broker crash -> Job cleanup;
- prove stale generation rejection across IPC.

### Phase D — secure launch / protected staging

- implement approved secure `CreateProcess` path;
- fail-closed pre-execution containment;
- handle inheritance tests;
- immutable protected staging;
- broker-side revalidation;
- Candidate / Current / PreviousKnownGood;
- leases.

Still no user-facing real-runtime claim until evidence gate passes.

### Phase E — first real `winws2` evidence

Controlled developer-only matrix:

- normal start/stop;
- app process crash;
- broker crash;
- startup cancellation;
- stop cancellation after irreversible boundary;
- stale generation;
- duplicate request;
- foreign runtime ownership;
- WinDivert conflict;
- readiness failure;
- reboot/recovery scenario where applicable;
- zero residual Z2P-owned process tree after terminal cleanup.

### Phase F — product/runtime features

Resume/rebase existing v6 work for:

- production profile application;
- Profile Compiler v2;
- PlanDiff/rollback;
- observability;
- capability probes;
- Doctor;
- network binding;
- Autopilot;
- production UI/tray;
- support bundle;
- TUF updater;
- packaging/release.

---

## 36. Required correctness matrix before public runtime enablement

### IPC

- wrong user/session;
- wrong PID;
- stale PID/reused process scenario considered;
- missing/invalid bootstrap material;
- remote client rejected;
- malformed frame;
- oversized frame;
- unknown protocol version;
- duplicate request;
- disconnected client;
- slow client;
- flood/backpressure;
- deadline/cancel race.

### Runtime lifecycle

- start success;
- stop success;
- stop during start;
- concurrent starts;
- concurrent stops;
- app exit while Running;
- app hard kill while Running;
- broker hard kill while Running;
- readiness failure;
- process unexpected exit;
- stale generation completion;
- Job creation failure;
- Job association failure;
- cleanup failure;
- ownership abandoned/recovery.

### Files/trust

- candidate corrupted;
- metadata/hash mismatch;
- path traversal;
- reparse point attack fixtures;
- file replacement between phases;
- invalid ACL/protected staging;
- incomplete bundle;
- incompatible capability manifest;
- failed activation;
- PreviousKnownGood retention;
- leased bundle GC rejection.

### Product truth

- process alive but required service capability failed;
- stale health from previous generation;
- stale network epoch;
- Doctor cancelled mid-candidate;
- no candidate passes hard gates;
- Autopilot flapping prevention;
- pinned/manual profile protection;
- rollback/recovery UI truth.

---

## 37. Performance evidence

Performance is secondary to correctness but must be measured once correctness is green.

Measure at least:

- app cold start unelevated;
- first broker elevation/handshake;
- repeat broker request latency;
- broker working set/private bytes;
- app working set;
- runtime start -> readiness;
- stop -> zero residual process tree;
- IPC allocations under normal snapshot flow;
- health projection update cost;
- Doctor candidate setup/cleanup overhead;
- SQLite impact from evidence/history retention;
- package size.

For the broker implementation A/B/C, use the same correctness corpus before comparing startup/memory/size.

---

## 38. Security review gates

At minimum perform dedicated reviews for:

1. IPC/bootstrap authentication;
2. secure process creation / Job containment;
3. filesystem staging and TOCTOU;
4. update/TUF client;
5. installer/portable trust boundary;
6. diagnostics redaction;
7. release signing/provenance.

No "secure" claim is accepted solely because a component is implemented in Rust or uses SHA-256.

---

## 39. Immediate execution order

If this RFC is accepted, execute in this order:

```text
1. v7 canonical docs + ADRs + truth hierarchy
2. current supported stack servicing
3. broker threat model + wire contract RED tests
4. C# broker + FakeRuntime lifecycle proof
5. secure process creation + pre-execution Job containment
6. immutable/protected staging + broker-side revalidation
7. crash/cancel/stale/TOCTOU evidence matrix
8. first developer-only real winws2 evidence
9. resume product features on new authority model
10. optional C# / NativeAOT / Rust broker A/B/C
```

Do not start with Rust.

Do not start with UI redesign.

Do not start by copying ZapretQuick, ZapretJ, zapret2UI, CDPI UI or Prizma code. They are evidence/reference sources, not architecture authorities for Z2P.

---

## 40. Reference projects / lessons

### ZapretQuick

Use as a reference for:

- unelevated UI + privileged broker separation;
- exact staged payload/generation ownership;
- candidate cleanup symmetry;
- runtime lifetime bound to app session;
- safer Auto Doctor lifecycle.

Do not copy its legacy BAT/Flowseal constraints into the zapret2 domain.

### ZapretJ

Use as a reference for:

- TOCTOU discipline;
- narrow Windows interop boundaries;
- resource lifetime/rollback reasoning;
- typed conflict states;
- evidence-driven use of Rust.

Do not copy service/registry architecture unless Z2P develops a concrete requirement for it.

### Asterlike/zapret2UI

Use as product evidence for:

- simple vs advanced UX;
- per-network remembered strategy;
- target-specific diagnostics;
- auto-selection/generation;
- custom hostlists/targets;
- tray/autostart expectations.

Do not adopt whole-process elevation or best-effort Job containment.

### CDPI UI

Use as evidence for component/preset UX and distribution expectations.

Do not create an arbitrary executable/plugin store inside the privileged client trust boundary.

### Prizma

Use as evidence for:

- correctness-before-latency candidate selection;
- bounded strategy tournaments;
- network-specific recommendation;
- transparent measurement.

Do not replace zapret2 with a new packet engine.

### Portmaster / mature Windows security software

Use as evidence that separating user UI from privileged enforcement is a mature product pattern.

Do not infer that Z2P therefore needs a persistent Windows Service.

---

## 41. Open design questions that require explicit resolution

These are not permission to keep the architecture vague; each gets a child issue with evidence and a decision.

1. exact broker bootstrap authentication primitive for installed and portable modes;
2. lazy broker startup vs startup-time elevation;
3. exact protected staging root and ACL ownership model;
4. file-handle/final-path/file-ID strategy for final TOCTOU closure;
5. minimal broker durable recovery store format;
6. exact IPC serialization format and compatibility rules;
7. whether installed publisher/AuthentiCode verification is mandatory in the broker handshake;
8. which process mitigations are compatible with broker and `winws2`;
9. exact boundary between Control Plane probing and broker privileged diagnostics;
10. ReactiveUI vs CommunityToolkit after bounded spike;
11. whether NativeAOT materially improves the broker;
12. whether Rust produces a meaningful net improvement after the C# contract is proven.

---

## 42. Acceptance criteria for the RFC itself

This RFC can move from `PROPOSED` to `ACCEPTED` only when:

- [ ] current `main` baseline is rechecked before merge;
- [ ] v6 decisions being superseded are explicitly named;
- [ ] there is exactly one planned runtime mutation authority;
- [ ] Windows Service remains out of MVP unless separately re-approved;
- [ ] broker protocol contains no arbitrary execution primitive;
- [ ] broker is forbidden from remote downloading;
- [ ] fail-closed pre-execution containment is mandatory;
- [ ] Current/PreviousKnownGood update invariants are preserved;
- [ ] desired/observed/health/recommendation truth separation is accepted;
- [ ] migration reuses existing Runtime Kernel rather than reimplementing it;
- [ ] real `winws2` remains gated until FakeRuntime + secure launch + staging evidence passes;
- [ ] Rust is explicitly an optional later A/B/C, not a prerequisite;
- [ ] a v7 roadmap/ADR update is merged with or immediately after RFC acceptance;
- [ ] README/status/truth hierarchy is synchronized.

---

## 43. Proposed final decision text

If accepted, record the core decision approximately as:

> Zapret2Pilot v7 uses an unelevated user-facing Control Plane and a minimal session-scoped elevated Runtime Broker. The broker is the sole mutation authority for the production zapret2 runtime and owns RuntimeKernelLoop, runtime generation, process containment and broker-side execution revalidation. The UI owns desired state, policy, evidence, persistence and presentation. IPC is local, authenticated, bounded and typed; it exposes no generic command/process/file execution primitive. Remote downloads occur only in the unelevated Control Plane and can become executable only after trust validation and broker-side promotion/revalidation into protected immutable staging. Runtime lifetime remains bounded by the application/tray session; no Windows Service is introduced for MVP. Existing v6 runtime correctness, recovery, compiler, TUF, PreviousKnownGood and release-evidence work is preserved and rebased onto this boundary. Rust may later replace only the broker implementation if an evidence-based comparison against managed C# and NativeAOT demonstrates a net benefit.
