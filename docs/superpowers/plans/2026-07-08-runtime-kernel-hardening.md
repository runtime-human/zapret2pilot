# Runtime Kernel Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Eliminate the remaining P0-1 follow-up dead code/comments, fix P0-3 (silent `EffectCompleted` loss) and P0-2 (Stop cannot cancel in-flight Start), and prepare the ground for the future `RuntimeAffinityOwner` replacement.

**Architecture:** The Kernel loop (`RuntimeKernelLoop`) is the single runtime authority. It currently uses one bounded channel for all commands and `TryWrite` for critical `EffectCompleted` messages. The plan splits the transport into a guaranteed lifecycle channel and a coalescing observation slot, then adds per-operation cancellation so a `Stop` truly supersedes an in-flight `Start`. The threading layer (`RuntimeAffinityExecutor`) is only cleaned up; its structural replacement is deferred to a separate oracle-approved packet.

**Tech Stack:** C# / .NET 10 / `System.Threading.Channels` / xUnit v3.

## Global Constraints
- Public API changes require explicit justification and oracle re-approval.
- `IRuntimeProcessHost` interface must not change.
- `RuntimeProcessHost` and `RuntimeAffinityExecutor` public contracts must not change.
- Every packet must pass `dotnet build Zapret2Pilot.slnx -c Release` and `dotnet test Zapret2Pilot.slnx -c Release`.
- No push to remote.
- Prefer smallest correct patch; no unrelated cleanup.

---

## Packet 1: `RuntimeAffinityExecutor` cleanup

**Objective:** Remove dead `_threadJoinFailed` field and update misleading comments. Zero behavioral change.

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/Threading/RuntimeAffinityExecutor.cs`
- Test: `tests/Zapret2Pilot.Runtime.Tests/Threading/RuntimeAffinityExecutorTests.cs` (must still pass)

**Allowed edits:**
- Delete `private int _threadJoinFailed;` field.
- Delete the `Interlocked.Exchange(ref _threadJoinFailed, 1)` line in `Dispose()`.
- Update comments in `PumpUntilCompleted` `ObjectDisposedException` and `InvalidOperationException` catch blocks to state that, because `Dispose()` now joins the thread before completing/disposing the continuation queue, these catches are defensive guards rather than descriptions of an active race.

**Forbidden edits:** Any behavioral change, queue capacity change, public API change.

**Verification ladder:**
1. `dotnet build Zapret2Pilot.slnx -c Release`
2. `dotnet test tests\Zapret2Pilot.Runtime.Tests -c Release --filter FullyQualifiedName~RuntimeAffinityExecutorTests`
3. `dotnet test Zapret2Pilot.slnx -c Release`

---

## Packet 2: P0-3 — Split kernel command transport

**Objective:** Ensure `Start`, `Stop`, `EffectCompleted`, and `Dispose` are never dropped; allow health `Observation` commands to coalesce under pressure.

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelLoop.cs`
- Modify: `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelCommand.cs` (only if a new subclass is needed for observation slot enqueue)
- Modify: `src/Zapret2Pilot.Runtime/Supervisor/RuntimeSupervisor.cs` (health snapshot posting path)
- Test: `tests/Zapret2Pilot.Runtime.Tests/Kernel/RuntimeKernelLoopTests.cs`

**Design:**
1. Replace the single `Channel<RuntimeKernelCommand>` with:
   - `Channel<RuntimeKernelCommand> _lifecycleChannel` — unbounded or large bounded with `FullMode.Wait`, `SingleReader=true`, `SingleWriter=false`. Accepts `Start`, `Stop`, `EffectCompleted`, `Dispose`.
   - `Channel<RuntimeKernelCommand.Observation> _observationChannel` — bounded capacity 1 with `FullMode.DropOldest` (or a simple `RuntimeKernelCommand.Observation? _latestObservation` with `Interlocked.Exchange`).
2. Keep `PostCommandAsync(command, token)` public signature. Internally route:
   - `Observation` → overwrite `_latestObservation` via `Interlocked.Exchange` (coalescing slot).
   - All other commands → `await _lifecycleChannel.Writer.WriteAsync(command, token)`.
3. In `RunLoop`, read lifecycle commands with `await _lifecycleChannel.Reader.ReadAsync(workerCts.Token)`. After each lifecycle command (or when no lifecycle command is immediately available), drain `_latestObservation` if present and reduce it.
4. Change `TryPostCompletion` to `PostCompletionAsync` using `_lifecycleChannel.Writer.WriteAsync(completion)`. It must not drop `EffectCompleted`.
5. Shutdown: complete `_lifecycleChannel` writer, cancel `workerCts`, drain remaining lifecycle commands, then drain any final observation. Await in-flight effects as today.

**Interfaces (no public signature change):**
- `Task PostCommandAsync(RuntimeKernelCommand command, CancellationToken cancellationToken = default)` unchanged.
- `ValueTask<bool> TryPostCompletion(RuntimeKernelCommand.EffectCompleted completion)` becomes `ValueTask PostCompletionAsync(RuntimeKernelCommand.EffectCompleted completion)` (private).

**Tests to add:**
- `EffectCompleted_IsNeverDropped_WhenChannelFull` — fill lifecycle channel with 64 observations (legacy capacity) and verify an `EffectCompleted` still gets processed.
- `Observations_Coalesce_UnderPressure` — post 100 observations rapidly; assert reducer sees fewer than 100 calls and final state reflects the latest.
- `StopCommand_AlwaysDelivered` — post `Stop` while channel is under pressure; assert stop is processed.
- `Dispose_DrainsBothChannels` — post observation then dispose; assert no unprocessed commands remain.

**Verification ladder:**
1. `dotnet build Zapret2Pilot.slnx -c Release`
2. `dotnet test tests\Zapret2Pilot.Runtime.Tests -c Release --filter FullyQualifiedName~RuntimeKernelLoopTests`
3. `dotnet test Zapret2Pilot.slnx -c Release`

**Stop conditions:**
- If changing `RuntimeKernelCommand` hierarchy or reducer signature is required, stop and escalate.
- If `EffectCompleted` can still be dropped after the change, stop.

---

## Packet 3: P0-2 — `CancelOperation` / `SupersedeOperation` with Kernel-owned per-operation CTS

**Objective:** When `Stop` supersedes an in-flight `Start`, the actual `Start` effect observes cancellation and unwinds promptly, not only the supervisor receipt.

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelLoop.cs`
- Modify: `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelCommand.cs` (add `CancelOperation` record)
- Modify: `src/Zapret2Pilot.Runtime/Kernel/RuntimeKernelReducer.cs` (handle `CancelOperation` and superseded `EffectCompleted`)
- Modify: `src/Zapret2Pilot.Runtime/Kernel/RuntimeProcessEffectRunner.cs` (link per-operation CTS with worker token)
- Modify: `src/Zapret2Pilot.Runtime/Supervisor/RuntimeSupervisor.cs` (post `CancelOperation` when Stop supersedes Start)
- Test: `tests/Zapret2Pilot.Runtime.Tests/Kernel/RuntimeKernelLoopTests.cs`
- Test: `tests/Zapret2Pilot.Runtime.Tests/Kernel/RuntimeProcessEffectRunnerTests.cs`
- Test: `tests/Zapret2Pilot.Runtime.Tests/Supervisor/RuntimeSupervisorTests.cs`

**Design:**
1. Add record to `RuntimeKernelCommand`:
   ```csharp
   public sealed record CancelOperation(
       RuntimeOperationId OperationId,
       RuntimeGeneration Generation,
       RuntimeCancellationReason Reason) : RuntimeKernelCommand;
   ```
2. In `RuntimeKernelLoop`:
   - Add `private readonly ConcurrentDictionary<RuntimeOperationId, CancellationTokenSource> _operationCancellations = new();`.
   - In `DispatchAsyncEffect`, create a linked CTS from `workerCts.Token` and the operation-specific CTS (if one exists). Store the per-operation CTS before dispatch under the effect's `OperationId`.
   - When a `CancelOperation` command arrives, the reducer validates `Generation`/`OperationId` matches the current pending operation. If valid, the loop calls `TryCancelOperation` which cancels and removes the matching CTS.
   - When the effect task completes (including due to cancellation), remove the per-operation CTS and dispose it.
3. In `RuntimeProcessEffectRunner`:
   - Accept the per-operation token (passed from the loop via `RunAsync` already as part of the linked token).
   - Ensure `DetermineCancellationReason` returns `Superseded` when the operation-specific CTS was canceled with reason `Superseded`.
4. In `RuntimeSupervisor.ResolveStopReceiptConflictAsync`:
   - After `existing.TrySetSuperseded()`, also post a `RuntimeKernelCommand.CancelOperation(existing.OperationId, existing.Generation, RuntimeCancellationReason.Superseded)` to the kernel loop.
5. Reducer:
   - `ReduceCancelOperation`: if OperationId/Generation matches current pending operation, set `CancellationReason = Superseded`, clear `Deadline`, emit guard effect.
   - `ReduceEffectCompleted`: if the completion indicates cancellation with reason `Superseded`, treat as a stale/superseded completion and transition to `Stopped` cleanly.

**Tests to add:**
- `Stop_DuringStart_CancelsInFlightStartEffect` — slow start effect; stop supersedes; assert start effect task is canceled before its natural deadline.
- `Start_Cancelled_CompletesAsSuperseded` — assert `EffectCompleted` has `CancellationReason.Superseded` and reducer lands in `Stopped`.
- `CancelOperation_WithStaleGeneration_IsIgnored` — post cancel with old generation; assert state unchanged.
- `WorkerShutdown_CancelsAllInFlightOperations` — dispose loop while effects in flight; all observe cancellation.

**Verification ladder:**
1. `dotnet build Zapret2Pilot.slnx -c Release`
2. `dotnet test tests\Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeKernelLoopTests|FullyQualifiedName~RuntimeProcessEffectRunnerTests|FullyQualifiedName~RuntimeSupervisorTests"
3. `dotnet test Zapret2Pilot.slnx -c Release`

**Stop conditions:**
- If `IRuntimeEffectRunner` interface must change, stop.
- If per-operation CTS leaks (not disposed on completion), stop.
- If existing supersede tests break and cannot be fixed within scope, stop.

---

## Packet 4: `RuntimeAffinityOwner` replacement (oracle pre-approval required)

**Objective:** Replace the async `SynchronizationContext`-based `RuntimeAffinityExecutor` with a dedicated-thread, typed-command-queue owner that runs only synchronous thread-affine actions.

**Status:** Planned but NOT approved for implementation yet. The oracle must review the design before coding begins because the current `RuntimeProcessHost` pipelines are async and rely on `ConfigureAwait(true)` to resume on the affinity thread.

**Open design questions for oracle:**
1. Should `RuntimeAffinityOwner` accept synchronous commands only, requiring `RuntimeProcessHost` to block the owner thread on `Task.Run(async pipeline)`? Or should the owner support async commands with explicit thread pinning?
2. Should `IRuntimeAffinityExecutor` interface be preserved/adapted, or replaced with a new `IRuntimeAffinityOwner` contract?
3. How is the ownership-mutex / lease thread-affinity invariant expressed and tested without `SynchronizationContext`?

**Files likely affected:**
- Delete/replace: `src/Zapret2Pilot.Runtime/Threading/RuntimeAffinityExecutor.cs`
- Create: `src/Zapret2Pilot.Runtime/Threading/RuntimeAffinityOwner.cs`
- Modify: `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs`
- Modify: `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`
- Replace tests: `tests/Zapret2Pilot.Runtime.Tests/Threading/RuntimeAffinityExecutorTests.cs` → `RuntimeAffinityOwnerTests.cs`
- Adapt: `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`

**Next step:** Submit a design document to the oracle and only then schedule Packet 4.

---

## Execution order

```
Packet 1 → Packet 2 → Packet 3 → [Packet 4 after oracle approval]
```

Each packet is a separate coder subagent + reviewer subagent cycle. Do not combine packets.
