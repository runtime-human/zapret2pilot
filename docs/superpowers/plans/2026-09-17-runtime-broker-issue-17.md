# Runtime Broker #17 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the existing single Runtime Kernel authority out of the Avalonia App composition root and under a new session-scoped `z2p-broker.exe`, while preserving the existing reducer/generation/cancellation/recovery semantics and proving the boundary with FakeRuntime only.

**Architecture:** `Zapret2Pilot.Broker` becomes the only production composition root that registers `RuntimeProcessHost`, `RuntimeKernelLoop`, `RuntimeSupervisor`, runtime ownership/recovery primitives and the #16 broker protocol/security primitives. `Zapret2Pilot.App` stops composing runtime mutation authority and instead consumes a `RuntimeClient` abstraction that projects broker state and issues bounded lifecycle requests. #17 does not implement real `winws2` launch hardening (#18), immutable staging (#19), updater/download behavior or a persistent Windows Service.

**Tech Stack:** C# 14, .NET 10 LTS, Microsoft.Extensions.Hosting 10.0.12, xUnit v3, existing `Zapret2Pilot.Contracts` broker protocol/security primitives, existing `Zapret2Pilot.Runtime` kernel/supervisor/process-host implementation, FakeRuntime test executable.

**Spec:** GitHub issue #17 plus `docs/adr/DEC-0053-v7-runtime-authority-relocation.md` and `docs/Z2P-RUNTIME-BROKER-SECURITY-CONTRACT.md`.

## Global Constraints

- `RuntimeKernelLoop` remains the only runtime lifecycle/mutation authority.
- App must not register or resolve `RuntimeKernelLoop`, `RuntimeProcessHost` or production `RuntimeSupervisor` as mutation authority.
- No real `winws2` in #17; lifecycle evidence uses FakeRuntime only.
- No generic privileged execution primitive and no Windows Service.
- Reuse #16 protocol/authentication/replay/bounds types; do not invent a second protocol.
- Preserve generation, stale-completion rejection, Stop-supersedes-Start, irreversible-boundary recovery, ownership mutex/lease and affinity-owner semantics.
- `docs/Z2P-CURRENT-STATE.json` remains the sole dynamic repository state and `currentWorkItem` remains #13 until orchestrator closure.

---

### Task 1: Prove App no longer owns runtime authority

**Files:**
- Create: `tests/Zapret2Pilot.App.Tests/Architecture/RuntimeAuthorityBoundaryTests.cs`
- Modify: `src/Zapret2Pilot.App/Hosting/Z2PHostBuilder.cs`

**Interfaces:**
- Consumes: existing `Z2PHostBuilder.CreateBuilder` and runtime DI extensions.
- Produces: an App host where runtime authority concrete services are absent.

- [ ] **Step 1: Write the failing architecture test** asserting the App service collection does not contain `RuntimeKernelLoop`, `RuntimeProcessHost`, `IRuntimeProcessHost`, `RuntimeSupervisor`, or an `IHostedService` implemented by `RuntimeSupervisor`.
- [ ] **Step 2: Run PR CI and confirm RED** because the current App host still calls `AddRuntimeProcessHost`, `AddRuntimeKernelLoop`, and `AddRuntimeSupervisor`.
- [ ] **Step 3: Remove App-side runtime-authority registrations** while keeping storage/app lifecycle services that do not mutate runtime.
- [ ] **Step 4: Run PR CI and confirm GREEN** for the architecture test and existing App tests.

### Task 2: Add Broker composition root and authority registration proof

**Files:**
- Create: `src/Zapret2Pilot.Broker/Zapret2Pilot.Broker.csproj`
- Create: `src/Zapret2Pilot.Broker/Program.cs`
- Create: `src/Zapret2Pilot.Broker/Hosting/BrokerHostBuilder.cs`
- Create: `tests/Zapret2Pilot.Broker.Tests/Zapret2Pilot.Broker.Tests.csproj`
- Create: `tests/Zapret2Pilot.Broker.Tests/Hosting/BrokerAuthorityCompositionTests.cs`
- Modify: `Zapret2Pilot.slnx`

**Interfaces:**
- Produces: `BrokerHostBuilder.CreateBuilder(string[] args)` and `BrokerHostBuilder.Build(string[] args)`.

- [ ] **Step 1: Write failing composition tests** requiring exactly one `RuntimeKernelLoop`, one `RuntimeProcessHost`/`IRuntimeProcessHost`, one `RuntimeSupervisor`/`IRuntimeSupervisor`, and one hosted supervisor instance in the Broker service provider.
- [ ] **Step 2: Run CI and confirm RED** because Broker project/composition root is absent.
- [ ] **Step 3: Add minimal .NET 10 Windows Broker executable** referencing Contracts/Runtime/Storage and compose existing runtime DI extensions only in Broker.
- [ ] **Step 4: Add deterministic package lock and solution entries** so locked restore remains complete.
- [ ] **Step 5: Run CI and confirm GREEN** for Broker composition and existing solution.

### Task 3: Introduce App-side RuntimeClient seam without a second state machine

**Files:**
- Create: `src/Zapret2Pilot.Contracts/Client/IRuntimeClient.cs`
- Create: `src/Zapret2Pilot.Contracts/Client/RuntimeClientSnapshot.cs`
- Create: `src/Zapret2Pilot.App/Runtime/BrokerRuntimeClient.cs`
- Create: `tests/Zapret2Pilot.App.Tests/Runtime/BrokerRuntimeClientTests.cs`
- Modify: `src/Zapret2Pilot.App/DependencyInjection/AppServiceCollectionExtensions.cs`
- Modify consumers currently typed directly to `IRuntimeSupervisor` only where needed to remove App authority.

**Interfaces:**
- `IRuntimeClient.CurrentSnapshot`
- `IRuntimeClient.SnapshotChanged`
- bounded `StartPreparedPlanAsync`, `StopGenerationAsync`, `GetRuntimeSnapshotAsync`, `ShutdownBrokerAsync` methods using #16 request identities rather than executable paths/argv.

- [ ] **Step 1: Write failing client tests** proving the client forwards bounded broker operations and only publishes broker snapshots; it contains no reducer/kernel/process-host dependency.
- [ ] **Step 2: Run CI and confirm RED** because the seam is absent.
- [ ] **Step 3: Add minimal interface and App adapter** over an injectable transport/session abstraction; do not add arbitrary command/file/process methods.
- [ ] **Step 4: Rewire App composition/consumers** so App depends on `IRuntimeClient`, not runtime mutation authority.
- [ ] **Step 5: Run CI and confirm GREEN**.

### Task 4: Broker request dispatcher over the existing Runtime Kernel

**Files:**
- Create: `src/Zapret2Pilot.Broker/Runtime/BrokerRuntimeDispatcher.cs`
- Create: `tests/Zapret2Pilot.Broker.Tests/Runtime/BrokerRuntimeDispatcherTests.cs`

**Interfaces:**
- Consumes: authenticated, semantically validated #16 request envelopes and existing `IRuntimeSupervisor`/kernel projections.
- Produces: #16 `BrokerResponseEnvelope` values and exactly one mutation dispatch at a time.

- [ ] **Step 1: Write failing tests** for snapshot query, stale/future generation rejection, duplicate in-flight/completed operation behavior, single mutation admission and stop/start mapping.
- [ ] **Step 2: Run CI and confirm RED**.
- [ ] **Step 3: Implement dispatcher using `BrokerOperationLedger`, `BrokerConcurrencyGate`, `BrokerGenerationGuard`, and existing runtime authority**; no second lifecycle state machine.
- [ ] **Step 4: Run CI and confirm GREEN**.

### Task 5: Session-scoped broker transport/lifetime and FakeRuntime lifecycle corpus

**Files:**
- Create: `src/Zapret2Pilot.Broker/Transport/...` for Windows Named Pipe/session plumbing required by #16.
- Create: `tests/Zapret2Pilot.Broker.Tests/Lifecycle/BrokerFakeRuntimeLifecycleTests.cs`
- Reuse: `tests/Zapret2Pilot.Testing.FakeRuntime`.

**Interfaces:**
- Uses #16 `BrokerPreAuthenticationSession`, strict codecs, peer identity binding, bounded connection/challenge/request limits.
- Owns broker lifetime policy for App graceful exit, App hard loss, Broker graceful shutdown and Broker hard termination evidence.

- [ ] **Step 1: Add failing lifecycle tests** for the #17 corpus: start/stop, stop supersedes start, concurrent start rejection, concurrent stop semantics, disconnect during start, App graceful/hard loss while Running, Broker graceful/hard shutdown, unexpected FakeRuntime exit, stale completion, cancellation before/after irreversible boundary and abandoned ownership recovery.
- [ ] **Step 2: Run CI and confirm RED**.
- [ ] **Step 3: Implement the minimum session-scoped pipe/lifetime coordination needed for the tests**, retaining #16 authentication and bounds.
- [ ] **Step 4: Run CI and confirm no orphan FakeRuntime process tree** and all lifecycle tests GREEN.

### Task 6: Repository truth, architecture docs and handoff

**Files:**
- Modify: `docs/Z2P-ARCHITECTURE.md`
- Modify: `docs/Z2P-IMPLEMENTATION-STATUS.md`
- Modify: `docs/Z2P-RUNTIME-BROKER-SECURITY-CONTRACT.md` only where implementation evidence resolves #17 obligations.
- Do not change dynamic state tuple outside `docs/Z2P-CURRENT-STATE.json`; keep `currentWorkItem=#13` unless separately orchestrated.

- [ ] **Step 1: Update ownership diagrams/text** to reflect actual implemented Broker/App boundary.
- [ ] **Step 2: Run repository-truth scripts, locked restore, Release build and full tests**.
- [ ] **Step 3: Verify exact PR head CI GREEN, no real `winws2`, no updater/download, no App-side authority and no orphan FakeRuntime tree**.
- [ ] **Step 4: Mark PR ready for orchestrator review; do not self-merge #17 without an explicit merge instruction.**
