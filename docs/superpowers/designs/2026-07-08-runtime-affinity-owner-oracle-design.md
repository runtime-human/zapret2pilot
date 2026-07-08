# Oracle Design Review: Packet 4 — `RuntimeAffinityOwner` Replacement

## 1. Executive Recommendation

**Adopt the sync-only command model.** Replace `RuntimeAffinityExecutor` (async `SynchronizationContext`-based) with `RuntimeAffinityOwner` — a dedicated-thread, typed-command-queue that executes **synchronous** `Action<CancellationToken>` / `Func<CancellationToken, T>` delegates. `RuntimeProcessHost` pipelines become synchronous methods that block on the two async seams (`MaterializeAsync`, `CheckAsync`) via `.GetAwaiter().GetResult()` on the owner thread.

**Replace `IRuntimeAffinityExecutor` with a new `IRuntimeAffinityOwner` contract.** The old interface's `Func<CancellationToken, Task<T>>` signature is incompatible with the sync-only model and carries the "false promise of async thread affinity" that the roadmap (§0.3.3) explicitly calls out for elimination.

**Express the thread-affinity invariant via `OwnerThreadId` + `RuntimeOwnershipLease`'s existing `ownerManagedThreadId` check.** No `SynchronizationContext` is needed.

## 2. Decision Rationale

### 2.1 Why sync-only over async-with-pinning

| Criterion | Sync-only | Async-with-pinning (current) |
|---|---|---|
| **Complexity** | Single queue, single pump, no continuation queue | Two queues (work + continuation), custom `SynchronizationContext`, sentinel unblock logic |
| **Deadlock surface** | Only if async seam depends on owner thread (it doesn't) | Continuation queue overflow (256 cap) can drop continuations; sentinel `ContinueWith` is the safety net |
| **Roadmap alignment** | §0.3.3 explicitly rejects "ложного обещания async thread affinity" | Perpetuates the pattern the roadmap wants eliminated |
| **Test surface** | Assert thread ID, assert serialization, assert lease round-trip | All of the above + continuation queue capacity, pump sentinel, `SynchronizationContext.CreateCopy` |
| **Async seams** | 2 (`MaterializeAsync`, `CheckAsync`) — both are independent I/O, safe to block | Same 2 seams, but continuations must be pumped |
| **Thread blocking** | Owner thread blocks during I/O — acceptable, it's a dedicated thread | Owner thread "appears" non-blocking but is actually busy pumping continuations |

**Deadlock analysis for sync-only:**

The two async seams in `RuntimeProcessHost`:
1. `workspaceMaterializer.MaterializeAsync` — file I/O (`FileStream` writes). Completion runs on an I/O completion port / thread pool. No dependency on the owner thread.
2. `RuntimeReadinessChecker.CheckAsync` — `Process.WaitForExitAsync` or polling loop. Completion runs on a thread pool thread. No dependency on the owner thread.

Neither seam posts work back to the owner thread. Blocking the owner thread on `.GetAwaiter().GetResult()` is safe. There is no `SynchronizationContext` on the owner thread (we're explicitly removing it), so async state machines inside these methods will use `ThreadPool` scheduling — no implicit capture, no deadlock.

**The `CleanupPipelineAsync` and `FailStartAndCleanupAsync` methods are already synchronous in practice** (they `await Task.CompletedTask` at the end). Converting them to pure synchronous methods is trivial.

### 2.2 Why a new interface instead of adapting the old one

`IRuntimeAffinityExecutor` declares:
```csharp
Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, ...);
```

The sync-only model requires:
```csharp
Task<T> ExecuteAsync<T>(Func<CancellationToken, T> work, ...);
```

These are fundamentally different contracts. Adapting the old interface would mean either:
- (a) Keeping the `Task<T>`-returning delegate and wrapping it in `.GetAwaiter().GetResult()` inside the owner — this hides the sync-over-async from the caller but makes the interface lie about its semantics.
- (b) Adding new sync methods alongside the old async ones — dual API surface, confusion about which to use.

Both options are worse than a clean break. The plan already anticipates deleting/replacing the file. The global constraint "RuntimeAffinityExecutor public contracts must not change" applies to Packets 1–3; Packet 4 explicitly requires oracle approval because it breaks this constraint.

### 2.3 Roadmap alignment

The v6 roadmap §0.3.3 states:

> "RuntimeSupervisor и RuntimeKernelWorker объединяются в один authority. Не остаётся двух semaphore/channel state machines и **ложного обещания async thread affinity**."

The current `RuntimeAffinityExecutor` is precisely this "false promise": it presents an async API (`Func<CancellationToken, Task<T>>`) but the continuations are pinned to a single thread via a custom `SynchronizationContext`, making it effectively synchronous execution with async overhead. The sync-only model makes this honest.

## 3. Proposed Interface Contract: `IRuntimeAffinityOwner`

```csharp
namespace Zapret2Pilot.Runtime.Threading;

/// <summary>
/// Owns a dedicated background thread and a typed command queue.
/// All work submitted via <see cref="ExecuteAsync{T}"/> or
/// <see cref="ExecuteAsync(Action{CancellationToken}, CancellationToken)"/>
/// runs synchronously on the owner thread, serialized.
/// The caller's thread is never blocked: the returned
/// <see cref="Task{T}"/> completes when the owner thread
/// finishes the work.
/// </summary>
/// <remarks>
/// <para>
/// The owner thread is the single thread-affinity anchor for the
/// runtime pipeline. The ownership mutex lease
/// (<see cref="Ownership.RuntimeOwnershipLease"/>) is acquired
/// and disposed on this thread, satisfying the lease's
/// <c>ownerManagedThreadId</c> invariant without a
/// <see cref="SynchronizationContext"/>.
/// </para>
/// <para>
/// Work delegates MUST be synchronous. If a delegate needs to
/// perform asynchronous I/O, it should block on the result
/// (e.g., <c>.GetAwaiter().GetResult()</c>). The owner thread
/// is dedicated and will not service other work while blocked.
/// </para>
/// </remarks>
public interface IRuntimeAffinityOwner : IDisposable
{
    /// <summary>
    /// The managed thread ID of the owner thread.
    /// Stable for the lifetime of the owner.
    /// Used for diagnostics and thread-affinity assertions.
    /// </summary>
    int OwnerThreadId { get; }

    /// <summary>
    /// Schedules <paramref name="work"/> to run synchronously on
    /// the owner thread. Returns a <see cref="Task{T}"/> that
    /// completes when the delegate finishes.
    /// </summary>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, T> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules <paramref name="work"/> to run synchronously on
    /// the owner thread. Returns a <see cref="Task"/> that
    /// completes when the delegate finishes.
    /// </summary>
    Task ExecuteAsync(
        Action<CancellationToken> work,
        CancellationToken cancellationToken = default);
}
```

**Key differences from `IRuntimeAffinityExecutor`:**
- Work delegates are `Func<CancellationToken, T>` / `Action<CancellationToken>` (synchronous), not `Func<CancellationToken, Task<T>>` / `Func<CancellationToken, Task>` (asynchronous).
- `OwnerThreadId` is exposed for assertion and diagnostic purposes.
- No `SynchronizationContext` is installed on the owner thread.

## 4. Required Changes to `RuntimeProcessHost`

### 4.1 Constructor

Replace `IRuntimeAffinityExecutor affinityExecutor` with `IRuntimeAffinityOwner affinityOwner`.

### 4.2 `StartAsync`

```csharp
public Task<Result<RuntimeProcessHostResult>> StartAsync(
    RuntimeProcessStartContext context,
    CancellationToken cancellationToken = default)
{
    // ... validation unchanged ...
    return affinityOwner.ExecuteAsync(
        ct => StartPipeline(context, ct),  // sync method, returns Result<...>
        cancellationToken);
}
```

### 4.3 `StartPipelineAsync` → `StartPipeline` (synchronous)

- Rename to `StartPipeline` (drop `Async` suffix).
- Remove `async` modifier.
- Replace `await workspaceMaterializer.MaterializeAsync(...).ConfigureAwait(true)` with `workspaceMaterializer.MaterializeAsync(...).GetAwaiter().GetResult()`.
- Replace `await RuntimeReadinessChecker.CheckAsync(...).ConfigureAwait(true)` with `RuntimeReadinessChecker.CheckAsync(...).GetAwaiter().GetResult()`.
- Replace `await FailStartAndCleanupAsync(...).ConfigureAwait(true)` with `FailStartAndCleanup(...)` (synchronous).
- Remove all `ConfigureAwait(true)` calls.

### 4.4 `StopAsync`

```csharp
public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
{
    return affinityOwner.ExecuteAsync(
        ct => StopPipeline(ct),  // sync method
        cancellationToken);
}
```

### 4.5 `StopPipelineAsync` → `StopPipeline` (synchronous)

- Rename, remove `async`.
- Replace `await CleanupPipelineAsync(ct).ConfigureAwait(true)` with `CleanupPipeline(ct)`.

### 4.6 `CleanupPipelineAsync` → `CleanupPipeline` (synchronous)

- Already synchronous in practice. Remove `async`, remove `await Task.CompletedTask.ConfigureAwait(true)`.
- Return `Result<Unit>` directly.

### 4.7 `FailStartAndCleanupAsync` → `FailStartAndCleanup` (synchronous)

- Already synchronous in practice. Remove `async`, remove `await Task.CompletedTask.ConfigureAwait(true)`.
- Return `Result<RuntimeProcessHostResult>` directly.

### 4.8 `Dispose()`

```csharp
public void Dispose()
{
    // ... disposed flag unchanged ...
    try
    {
        affinityOwner
            .ExecuteAsync(_ => CleanupPipeline(CancellationToken.None))
            .GetAwaiter()
            .GetResult();
    }
    catch (Exception ex)
    {
        // ... fallback unchanged ...
    }
}
```

Note: `ExecuteAsync` now takes `Func<CancellationToken, Result<Unit>>` (sync), returns `Task<Result<Unit>>`. The `.GetAwaiter().GetResult()` blocks the calling thread until the owner thread finishes the cleanup.

### 4.9 `BestEffortDispose`

No change needed — it already runs on the calling thread as a fallback.

### 4.10 XML documentation

All references to `IRuntimeAffinityExecutor`, `"Z2P-RuntimeAffinity" thread`, and `ConfigureAwait(true)` must be updated to reference `IRuntimeAffinityOwner` and the owner thread.

## 5. Migration Steps

Execute in this order within a single coder session:

1. **Create `IRuntimeAffinityOwner`** in `src/Zapret2Pilot.Runtime/Threading/IRuntimeAffinityOwner.cs`.

2. **Create `RuntimeAffinityOwner`** in `src/Zapret2Pilot.Runtime/Threading/RuntimeAffinityOwner.cs`.
   - Dedicated `Thread` named `"Z2P-RuntimeAffinity"` (same name for continuity).
   - `BlockingCollection<Action>` command queue (bounded, capacity 256).
   - Pump loop: `foreach (Action cmd in _queue.GetConsumingEnumerable()) cmd();`
   - `ExecuteAsync<T>`: enqueue work with `TaskCompletionSource<T>`, return `tcs.Task`.
   - `Dispose()`: cancel CTS, `CompleteAdding()`, join thread (5s timeout), dispose queue.
   - No `SynchronizationContext` installation.
   - Capture `OwnerThreadId` in the thread's startup before entering the pump.

3. **Create `RuntimeAffinityOwnerTests`** — port and adapt from `RuntimeAffinityExecutorTests`.

4. **Modify `RuntimeProcessHost`** — apply changes from §4 above.

5. **Modify `RuntimeProcessHostTests.HostFixture`** — replace `IRuntimeAffinityExecutor` with `IRuntimeAffinityOwner`, replace `new RuntimeAffinityExecutor()` with `new RuntimeAffinityOwner()`.

6. **Modify `RuntimeServiceCollectionExtensions`** — replace `RuntimeAffinityExecutor` / `IRuntimeAffinityExecutor` registration with `RuntimeAffinityOwner` / `IRuntimeAffinityOwner`.

7. **Delete `RuntimeAffinityExecutor.cs`** and `RuntimeAffinityExecutorTests.cs`.

8. **Run verification ladder:**
   ```powershell
   dotnet build Zapret2Pilot.slnx -c Release
   dotnet test tests\Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeAffinityOwnerTests|FullyQualifiedName~RuntimeProcessHostTests"
   dotnet test Zapret2Pilot.slnx -c Release
   ```

## 6. Test / Invariant Checklist

### 6.1 `RuntimeAffinityOwnerTests` (new)

| # | Test | Invariant |
|---|---|---|
| 1 | `ExecuteAsync_WorkCompletes_ReturnsResult` | Basic functionality |
| 2 | `ExecuteAsync_WorkItemsExecuteSerially` | No concurrent execution |
| 3 | `ExecuteAsync_AllWorkRunsOnOwnerThread` | `Environment.CurrentManagedThreadId == owner.OwnerThreadId` for every work item |
| 4 | `ExecuteAsync_CallerTokenCanceled_CompletesAsCanceled` | Cancellation propagation |
| 5 | `ExecuteAsync_DisposeWhileWorkPending_CompletesAsCanceled` | Disposal cancels pending work |
| 6 | `ExecuteAsync_ThrowsAfterDispose` | `ObjectDisposedException` post-dispose |
| 7 | `Dispose_IsIdempotent` | Multiple `Dispose()` calls are safe |
| 8 | `OwnerThreadId_IsStable` | Same ID across multiple `ExecuteAsync` calls |
| 9 | `Queue_HasBoundedCapacity` | Reflection check on internal queue capacity (256) |
| 10 | `ExecuteAsync_NullWork_ThrowsArgumentNullException` | Argument validation |

### 6.2 `RuntimeProcessHostTests` (adapted)

All existing tests must pass with the new owner. Key tests:

| # | Test | What it proves |
|---|---|---|
| 1 | `StartAsync_StopAsync_FakeRuntime_Success` | Full lifecycle with sync pipeline |
| 2 | `Dispose_CleansUpRunningProcess` | Lease released, process killed, lock file deleted — all via owner thread |
| 3 | `StopAsync_AfterSuccess_AllowsRestart` | State cleared, restart works |
| 4 | `StopAsync_LockFileDeleteFails_AllowsRestart` | Cleanup failure doesn't wedge the owner |
| 5 | `StartAsync_AlreadyRunning_Fails` | Serialization on owner thread |

### 6.3 New integration test (add to `RuntimeProcessHostTests`)

| # | Test | Invariant |
|---|---|---|
| 1 | `Lease_AcquireAndDispose_OnOwnerThread` | Assert that `ownershipLease` is acquired on `affinityOwner.OwnerThreadId` and disposed on the same thread. Use a custom lease wrapper or reflection to capture the thread IDs at acquire and dispose time. |

### 6.4 Invariants that must hold

1. **Thread identity:** Every command executed by `IRuntimeAffinityOwner` runs on `OwnerThreadId`.
2. **Serialization:** No two commands overlap in time.
3. **Lease round-trip:** `RuntimeOwnershipLease` is constructed with `ownerManagedThreadId == OwnerThreadId` and disposed on the same thread (no `RuntimeOwnershipThreadAffinityException`).
4. **No SynchronizationContext:** `SynchronizationContext.Current` is `null` inside owner-thread commands.
5. **Disposal drains:** After `Dispose()`, all enqueued commands have been executed or cancelled.

## 7. Risks and Mitigations

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| 1 | **Sync-over-async on `MaterializeAsync` deadlocks** if the materializer internally captures a `SynchronizationContext` that depends on the owner thread. | Medium | The owner thread has no `SynchronizationContext`. `MaterializeAsync` is file I/O — it uses `FileStream` with async I/O completion ports, not `SynchronizationContext`. Verify with a test that calls `MaterializeAsync` from the owner thread and asserts completion within a timeout. |
| 2 | **`CheckAsync` blocks the owner thread for the full readiness timeout** (default 10s), preventing other commands from executing. | Low | This is acceptable — the owner thread is dedicated, and the readiness check is bounded. The kernel loop dispatches effects asynchronously, so the caller is not blocked. |
| 3 | **Breaking change to `IRuntimeAffinityExecutor`** affects any code that depends on it. | Low | Grep shows only 3 consumers: `RuntimeProcessHost`, `RuntimeServiceCollectionExtensions`, and tests. All are in scope for this packet. No external consumers. |
| 4 | **`RuntimeProcessHost.Dispose()` blocks the calling thread** on `affinityOwner.ExecuteAsync(...).GetAwaiter().GetResult()`. If the owner thread is stuck in a long-running command, `Dispose()` hangs. | Medium | Same risk as today — the current `Dispose()` also blocks on `affinityExecutor.ExecuteAsync(...).GetAwaiter().GetResult()`. The 5-second thread-join timeout in `RuntimeAffinityOwner.Dispose()` provides a bounded exit. |
| 5 | **`GetAwaiter().GetResult()` wraps exceptions in `AggregateException`** for some async methods. | Low | `GetAwaiter().GetResult()` unwraps `AggregateException` for `Task` and `Task<T>` — it rethrows the inner exception directly. This is the standard behavior and matches the current code. |
| 6 | **Future async seams added to the pipeline** could introduce a deadlock if they depend on the owner thread. | Medium | Document the constraint: "Work delegates submitted to `IRuntimeAffinityOwner` MUST NOT await async operations that require posting back to the owner thread." Add a code comment and an architecture test that asserts no `SynchronizationContext` is installed on the owner thread. |

## 8. Constraints the Coder Must Follow

1. **`IRuntimeProcessHost` interface MUST NOT change.** The public `StartAsync` / `StopAsync` signatures remain `Task<Result<...>>`.
2. **`RuntimeKernelLoop` and `RuntimeProcessEffectRunner` MUST NOT change.** They call `IRuntimeProcessHost.StartAsync` / `StopAsync` with `ConfigureAwait(false)` — this is correct and unaffected by the internal threading change.
3. **The owner thread name MUST remain `"Z2P-RuntimeAffinity"`** for diagnostic continuity.
4. **Queue capacity MUST remain 256** (same as current).
5. **`Dispose()` MUST be idempotent** and MUST drain enqueued commands before exiting (or timeout after 5 seconds).
6. **No `SynchronizationContext` installation** on the owner thread.
7. **`RuntimeOwnershipLease` MUST NOT be modified.** Its `ownerManagedThreadId` check is the invariant enforcer.
8. **All existing `RuntimeProcessHostTests` MUST pass** without modification to test logic (only fixture wiring changes).
9. **No changes to `RuntimeKernelLoop`, `RuntimeSupervisor`, `RuntimeHealthMonitor`, or any kernel command/reducer code.**

## 9. Stop Conditions

1. If `MaterializeAsync` or `CheckAsync` cannot be safely blocked on the owner thread (e.g., they internally depend on a `SynchronizationContext`), **stop and escalate**.
2. If any existing `RuntimeProcessHostTests` integration test fails and the failure is not attributable to a test fixture wiring issue, **stop and escalate**.
3. If the `RuntimeOwnershipLease.Dispose()` throws `RuntimeOwnershipThreadAffinityException` during any test, **stop immediately** — the thread-affinity invariant is broken.
4. If changing `IRuntimeProcessHost` is required, **stop** — this is forbidden.
5. If the `RuntimeKernelLoop` or `RuntimeProcessEffectRunner` need changes, **stop** — they are out of scope.

## 10. Reviewer Focus

1. **Thread-affinity round-trip:** Verify that the lease is acquired and disposed on `OwnerThreadId`. Check the `StartPipeline` and `CleanupPipeline` methods for any code path that could dispose the lease on a different thread.
2. **Sync-over-async safety:** Verify that `MaterializeAsync(...).GetAwaiter().GetResult()` and `CheckAsync(...).GetAwaiter().GetResult()` are safe to call from a thread with no `SynchronizationContext`. Check the implementations of these methods for any `SynchronizationContext` dependency.
3. **Disposal ordering:** Verify that `RuntimeProcessHost.Dispose()` → `affinityOwner.ExecuteAsync(CleanupPipeline)` → `lease.Dispose()` runs on the owner thread, and that `affinityOwner.Dispose()` is called AFTER `host.Dispose()` in the test fixture.
4. **No async leaks:** Verify that no `async` method remains in `RuntimeProcessHost` (all should be converted to synchronous). Check for orphaned `ConfigureAwait(true)` calls.
5. **DI registration:** Verify that `RuntimeAffinityOwner` is registered as a singleton and that the `IRuntimeAffinityOwner` resolution is correct.
6. **Test coverage:** Verify that all 10 `RuntimeAffinityOwnerTests` and all existing `RuntimeProcessHostTests` pass.

## 11. Open Questions Still Needing User/Product Decision

1. **Should `MaterializeAsync` and `CheckAsync` be given synchronous counterparts** (e.g., `Materialize` and `Check`) to avoid the sync-over-async pattern entirely? This would be a cleaner long-term solution but requires changes to `IRuntimeWorkspaceMaterializer` and `RuntimeReadinessChecker`, which are out of scope for Packet 4. **Recommendation: defer to a follow-up packet; use `.GetAwaiter().GetResult()` for now.**

2. **Should the `RuntimeAffinityOwner` be merged into `RuntimeKernelLoop`** as the roadmap (§5.1) suggests? The roadmap envisions a single dedicated thread for the kernel loop. Currently, the kernel loop has its own dedicated thread (via `ChannelReader.ReadAsync`), and the affinity owner has a separate dedicated thread. Merging them would eliminate one thread and simplify the model, but it's a much larger change. **Recommendation: defer to a future packet; Packet 4 is a threading-layer replacement, not a kernel restructuring.**

3. **Should the `BestEffortDispose` fallback path be removed?** It exists because the pre-0.0.20 `Dispose()` had a bug where the cleanup pipeline was rejected. With the new owner, the cleanup always runs. However, `BestEffortDispose` is a safety net for the case where `ExecuteAsync` itself throws (e.g., owner thread stuck). **Recommendation: keep it as a defensive fallback.**
