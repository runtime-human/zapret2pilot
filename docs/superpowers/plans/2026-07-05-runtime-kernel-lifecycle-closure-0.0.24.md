# Runtime Kernel Lifecycle Closure (0.0.24) Implementation Plan

**Goal:** Replace the competing `RuntimeSupervisor.SemaphoreSlim` and generic `RuntimeKernelWorker.Channel` with a single, provable lifecycle authority — `RuntimeKernelLoop`. `IRuntimeSupervisor` becomes a façade over the loop. No real `winws2` launch.

**Architecture:** Introduce a pure, deterministic `RuntimeKernelReducer` that turns a `RuntimeKernelCommand` and the current `RuntimeKernelState` into the next state, a list of `RuntimeEffectIntent`s, public events and a command outcome. A dedicated thread inside `RuntimeKernelLoop` owns the reducer state commit and serialises all lifecycle commands through a bounded channel. `RuntimeStatePublisher` isolates subscribers from the kernel thread and replays the latest immutable snapshot. External effects are dispatched with `OperationId`/`Generation` identity so stale completions are logged and discarded, never mutating current state.

## Global Constraints

- No real `winws2` launch; tests use `Zapret2Pilot.Testing.FakeRuntime`.
- No Windows Service, IPC, VPN, proxy, MITM, per-URL router, `.bat`/`.cmd` wrappers, or arbitrary command execution.
- Lifecycle mutation happens only inside the `RuntimeKernelLoop` reader thread.
- No direct subscriber/observer callback on the kernel thread.
- No generic `Func<CancellationToken, Task>` as a lifecycle contract.
- No `disposed = true` before required cleanup.
- Do not mutate unrelated files (Storage, UI shell, SQLite schema, compiler, portable bootstrap).
- No new NuGet package versions; use existing centrally-managed packages only.
- Existing `IRuntimeSupervisor` public surface must remain source-compatible.

## Packet 1: Foundation — State Model, Pure Reducer and Command Loop Skeleton

**Objective:** Introduce `RuntimeKernelLoop` as a parallel, self-contained component. Existing `RuntimeSupervisor` and `RuntimeKernelWorker` remain untouched. Ends with compiling solution, passing existing tests and new Kernel tests.

**In scope:** value types, immutable model, pure reducer, state publisher, command loop, unit tests.

**Out of scope:** wiring loop into supervisor, removing worker, changing process host/health monitor/DI, executing real process effects.

**Files:** new files under `src/Zapret2Pilot.Runtime/Kernel/` and `tests/Zapret2Pilot.Runtime.Tests/Kernel/`.

**Verification:**
```powershell
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests/Kernel -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release
```

## Packet 2: Supervisor Façade and State Publication Wiring

**Objective:** Rewrite `RuntimeSupervisor` to delegate to `RuntimeKernelLoop` while preserving `IRuntimeSupervisor` contract. Wire health monitor to loop state.

**Files:** `RuntimeSupervisor.cs`, `IRuntimeSupervisor.cs`, `RuntimeSupervisorState.cs`, `RuntimeHealthMonitor.cs`, `RuntimeServiceCollectionExtensions.cs`.

## Packet 3: Effect Completion, Cancellation/Deadlines and Dispose/Shutdown

**Objective:** Execute external effects via `IRuntimeEffectRunner`, report completions with `OperationId`/`Generation`, add cancellation reason/deadline, irreversible-boundary semantics, bounded effect drain.

**Files:** new `IRuntimeEffectRunner.cs`, `RuntimeProcessEffectRunner.cs`; modify Kernel loop and reducer.

## Packet 4: Migration Cleanup, Worker Removal, Tests and Documentation

**Objective:** Delete `RuntimeKernelWorker.cs`, adapt `RuntimeProcessHost`, update DI, add mandated tests, update docs, bump version to `0.0.24`.

**Files:** all remaining Runtime files, docs, `VERSION`, `MainWindowViewModel.cs`.
