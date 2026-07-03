# Zapret2Pilot / Z2P — Critical Review Register

This document records accepted critical review findings. It is not a praise document. It exists to prevent architecture debt before implementation.

## Status legend

- **Accepted / P0** — must be fixed before or during foundational implementation.
- **Accepted / P1** — must be fixed before internal MVP.
- **Accepted / P2** — later improvement.
- **Rejected** — intentionally not adopted.

## 1. Job Objects are mandatory

Status: **Accepted / P0**

Problem:

- RuntimeLockManager and RuntimeExitCoordinator do not prevent orphan `winws2.exe` after `z2p.exe` crash.
- Lock file is recovery metadata, not process containment.

Decision:

- RuntimeProcessHost must create Windows Job Object.
- RuntimeProcessHost must set kill-on-close behavior.
- RuntimeProcessHost must assign `winws2.exe` to the Job Object.

Files/classes:

- `Zapret2Pilot.Runtime/RuntimeProcessHost.cs`
- `Zapret2Pilot.Platform.Windows/Jobs/WindowsJobObjectFactory.cs`
- `Zapret2Pilot.Platform.Windows/Jobs/WindowsJobHandle.cs`

Tests:

- Windows-only integration test with fake runtime.
- Verify child runtime terminates when job handle is closed.

## 2. Lock file TOCTOU risk

Status: **Accepted / P0**

Problem:

- `check PID → write lock file` is not atomic.
- Another process may perform the same sequence.

Decision:

- Use Global Mutex for atomic runtime ownership.
- Use lock file only for recovery metadata.

Required mutex:

```text
Global\Z2P_RUNTIME_OWNER_v1
```

Required lock file:

```text
C:\ProgramData\Zapret2Pilot\runtime\z2p-runtime.lock
```

Tests:

- concurrent ownership acquisition;
- stale lock recovery;
- lock file without mutex ownership must not be trusted.

## 3. Process ownership must not rely on PID only

Status: **Accepted / P0**

Decision:

RuntimeOwnershipDetector must verify:

- PID exists;
- process name;
- executable path;
- command line hash;
- compiled plan hash;
- process creation time compatibility.

## 4. Generic Host inside Avalonia app

Status: **Accepted / P0**

Decision:

- Use `Microsoft.Extensions.Hosting` inside `z2p.exe`.
- Do not add Windows Service.

Hosted services:

- RuntimeSupervisorHostedService;
- RuntimeHealthMonitorHostedService;
- RuntimeRecoveryHostedService;
- EventJournalHostedService;
- NetworkChangeMonitorHostedService;
- StorageRetentionHostedService.

## 5. ReactiveUI vs CommunityToolkit.Mvvm

Status: **Accepted / P0**

Decision:

- Presentation Layer uses ReactiveUI + System.Reactive.
- Do not use CommunityToolkit.Mvvm as primary UI ViewModel framework.
- Do not mix ReactiveUI ViewModels with Toolkit ViewModels.

## 6. UI thread dispatch strategy

Status: **Accepted / P0**

Decision:

- Add UI scheduler abstraction.
- Runtime/Application layers must not call Avalonia Dispatcher directly.
- UI subscriptions observe on UI scheduler.

## 7. SQLite WAL and busy timeout

Status: **Accepted / P0**

Decision:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

## 8. RuntimePlanCache invalidation

Status: **Accepted / P0**

Decision:

RuntimePlanCacheKey must include hashes of:

- ProfileDocument;
- StrategyPackDocument;
- hostlist fingerprints;
- runtime manifest;
- compiler version;
- compiler options.

Each input mutation must cause cache miss.

## 9. Atomic writes for generated runtime files

Status: **Accepted / P0**

Decision:

- All generated runtime files must use AtomicFileWriter.
- Write temp file in same directory, flush, atomic move, verify final file readable.

## 10. SafePathResolver

Status: **Accepted / P0**

Decision:

- Imported paths are relative by default.
- Resolved paths must stay inside allowed roots.
- Arbitrary absolute paths are forbidden unless explicitly trusted/advanced.

## 11. Auto Doctor false positives

Status: **Accepted / P1**

Decision:

- Add ProbeFailureClass.
- Separate DPI/network artifacts from application errors.
- Only network/DPI evidence should increase profile aggressiveness.

## 12. Auto Doctor time budget

Status: **Accepted / P1**

Decision:

- Key Services Check: short/lightweight.
- Auto Doctor Quick: approximately 45 seconds.
- Auto Doctor Full: up to 90–120 seconds.

## 13. Network change detection

Status: **Accepted / P0 hooks, P1 behavior**

Decision:

- Add NetworkChangeMonitor hooks in P0.
- P0 reaction: warning + lightweight check.
- P1 reaction: network-aware profile binding.

## 14. Crash loop backoff

Status: **Resolved**.

Decision:

- Add CrashLoopGuard.
- Use exponential backoff.
- Reset crash counter only after stability window and successful health check.

Resolution:

- `Zapret2Pilot.Runtime.Guard.CrashLoopGuard` lands in `0.0.22` as a pure in-memory primitive with the documented defaults (`BaseBackoff = 2 seconds`, `MaxBackoff = 5 minutes`, `StabilityWindow = 60 seconds`, `MaxConsecutiveFailures = 10`). The backoff formula is `min(BaseBackoff * 2^(consecutiveFailures - 1), MaxBackoff)`. Permanent lockout uses a strict `>` comparison (the 11th consecutive failure is the first to be rejected) and is sticky: only an explicit `Reset()` call escapes it. The stability window measures "time since the most recent event (success or failure)" so a fresh failure always restarts the window. The guard is registered as a singleton through `AddCrashLoopGuard` in `AppHost.Build`, is intentionally NOT registered as an `IHostedService` (it owns no background timer / disposable resources) and is not yet consumed by any other runtime component. The integration with `RuntimeProcessHost` / `RuntimeHealthMonitor` / `RuntimeKernelWorker` is intentionally deferred to a future milestone that will require its own oracle review.
- `0.0.23 — Runtime Kernel Correctness Hardening` further hardened the `CrashLoopGuard` primitive: the consecutive-failure counter is now reset only when `RecordSuccess()` has observed a stable, failure-free window (success-gated reset), and the counter is saturated at `MaxConsecutiveFailures + 1` so a runaway restart loop cannot inflate it further. These changes are covered by the new `RecordFailure_NoSuccess_AfterStabilityWindow_DoesNotReset`, `RecordSuccess_BeforeFailure_AfterStabilityWindow_DoesNotReset` and `RecordFailure_SaturatesAtMaxConsecutiveFailuresPlusOne` tests. Wiring the guard into `RuntimeProcessHost` / `RuntimeHealthMonitor` / a future `RuntimeSupervisor` (the start/stop/exit flow that actually drives `RecordFailure` / `RecordSuccess` / `Check`) remains pending and is gated on an oracle-reviewed `RuntimeSupervisor` design.
- `0.0.23 — Runtime Kernel Correctness Hardening` also delivered the integration: `IRuntimeSupervisor` / `RuntimeSupervisor` (`src/Zapret2Pilot.Runtime/Supervisor/`) is the single owner of the runtime start / stop state machine. It implements `IHostedService`, is registered by `AddRuntimeSupervisor` (singleton under `RuntimeSupervisor`, `IRuntimeSupervisor` and `IHostedService` — all the same instance), and wires `ICrashLoopGuard.Check` into every `StartAsync`, `ICrashLoopGuard.RecordFailure` into failed starts and unexpected `Exited` health snapshots, and `ICrashLoopGuard.RecordSuccess` into transitions into `Healthy` from `IRuntimeHealthMonitor`. `MainWindowViewModel` subscribes to `IRuntimeSupervisor.StateChanged` and surfaces `StartBlocked` snapshots through `LastAction`. The integration is covered by `RuntimeSupervisorTests`, the DI test `AddRuntimeSupervisorRegistersSupervisorAsHostedService` and the view-model test `StartBlocked_UpdatesLastAction`. No real `winws2` process is launched in 0.0.23 — the integration is exercised against fake `IRuntimeProcessHost` / `IRuntimeHealthMonitor` / `FakeClock` collaborators.

## 15. Global exception handling

Status: **Accepted / P0**

Decision:

- Add GlobalExceptionHandler.
- Add CrashReportWriter.
- On next start show recovery screen if needed.

## 16. Fake runtime must not be production code

Status: **Accepted / P0**

Decision:

- Put fake runtime in `Zapret2Pilot.Testing` or test tool project.

## 17. RuntimeStateStore naming conflict

Status: **Accepted / P0**

Decision:

- Runtime layer: `RuntimeKernelStateStore`.
- Application/UI projection: `DashboardStateStore` or `RuntimeApplicationState`.

## 18. ProfileDocument vs ProfileDefinition boundary

Status: **Accepted / P0**

Decision:

- ProfileDocument = storage/import/export DTO.
- ProfileDefinition = validated domain model.
- Compiler accepts ProfileDefinition only.

## 19. Diagnostics redaction algorithm

Status: **Accepted / P0**

Decision:

- Deterministic redaction rules.
- Stable hashes with local salt where needed.
- Remove URL query data.
- Redaction tests required.

## 20. Elevated GUI security risk

Status: **Accepted / ongoing**

Decision:

Compensate with:

- command allowlist;
- typed commands;
- SafePathResolver;
- no arbitrary execution;
- trust levels;
- advanced mode gate;
- atomic generated files;
- manifest verification;
- redacted diagnostics.

## 21. Autostart reality

Status: **Accepted / P1**

Decision:

- MVP does not promise elevated autostart.
- P1 may add Scheduled Task with explicit user consent.

## 22. SmartScreen / signing / trust

Status: **Accepted / release concern**

Decision:

- Internal builds can be unsigned.
- Public preview should have signing strategy.
- Stable public release should have code signing and checksums.

## 23. Windows HVCI / WinDivert compatibility

Status: **Accepted / P1**

Decision:

- RuntimeCompatibilityChecker should detect blocked runtime states.
- UI should show `BlockedByOS` / HVCI-friendly explanation instead of generic crash.

## 24. ApplyProfile downtime

Status: **Accepted / UX warning**

Decision:

- UI must warn that applying profile may interrupt bypass for a few seconds.
- Some existing connections may need browser refresh.

## 25. Current review conclusion

The architecture remains valid, but Runtime Kernel implementation must start with process safety and concurrency primitives, not with UI buttons or direct process launch.

## 26. Verified executable integrity gap (P0-4)

Status: **Accepted / P0**

Problem:

- `RuntimeProcessHost` previously launched any `RuntimeExecutablePath` string it was handed. The output of `ZapretAssetVerifier` (a `ZapretAssetVerificationSummary`) was produced by the workspace materializer but was **not** threaded into the launch decision.
- A caller could pass a corrupted or attacker-controlled path and the elevated token would execute it.

Decision:

- Introduce a new value object `VerifiedRuntimeExecutablePath` that can only be constructed from a passing `ZapretAssetVerificationSummary`.
- `RuntimeProcessStartContext` accepts `VerifiedRuntimeExecutablePath` (or an equivalent opaque type) instead of a raw `string`.
- The workspace materializer is the only place that produces this type in production.
- The host launches only what it receives. Expired or missing verification produces an explicit failure, not a silent fallback.

Files/classes:

- `src/Zapret2Pilot.Runtime/Integrity/VerifiedRuntimeExecutablePath.cs`
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessStartContext.cs`
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`
- `src/Zapret2Pilot.Engine.Zapret2/Assets/ZapretAssetVerificationSummary.cs`
- `src/Zapret2Pilot.Runtime/Workspace/RuntimeWorkspaceMaterializer.cs`

Tests:

- Host rejects an unverified executable.
- Launched path matches the manifest.
- Expired verification summary cannot construct `VerifiedRuntimeExecutablePath`.
- `FakeRuntime` paths remain launchable.

Roadmap alignment:

- Implements `0.0.18-A` and codifies `DEC-0032`.

## 27. Redirected stdout / stderr not consumed (P0-5)

Status: **Accepted / P0**

Problem:

- `ProcessStartInfo.RedirectStandardOutput` and `RedirectStandardError` were set to `true`, but the host never subscribed to `OutputDataReceived` / `ErrorDataReceived`.
- A verbose `winws2` could deadlock on a full pipe before reaching a readiness check.

Decision:

- For `0.0.18`, both redirects are set to `false` so the OS owns the child stdio.
- A bounded output pump (fixed-size ring buffer, explicit consumer thread, backpressure contract) is planned for `0.0.19`.
- The pump must be observable from the Application layer for diagnostics, not hidden in the host.

Files/classes:

- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`

Tests:

- A `FakeRuntime` that emits a configurable volume of stdout / stderr must not deadlock within 30 seconds.
- The process is reported `Running` and `Exited` cleanly.

Roadmap alignment:

- Implements `0.0.18-B` and the preparation for the bounded pump in `0.0.19`.

## 28. StopAsync rollback after irreversible kill (P0-6)

Status: **Accepted / P0**

Problem:

- `RuntimeProcessHost.StopAsync` killed the process and then called `Rollback` on the transaction, which restored `IsRunning = true` in `RuntimeTransactionManager`.
- A subsequent restart found the kernel thinking it was still running, with no live process, no live Job Object and no live mutex.

Decision:

- After an irreversible kill, the transaction is **not** rolled back. The transaction remains in `Stopped` or `Failed`.
- Cleanup failures (e.g. lock file delete failures) are logged with severity `Warning` or `Error` and the host state (`IsRunning`, `OwnedHandles`) is cleared.
- A subsequent `StartAsync` is allowed and must succeed.

Files/classes:

- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`
- `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionManager.cs`
- `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`
- `tests/Zapret2Pilot.Runtime.Tests/Transactions/RuntimeTransactionManagerTests.cs`

Tests:

- After kill, transaction manager state is `Stopped` even if a follow-up lock delete fails.
- A second `StartAsync` is allowed.
- `Rollback` from a kill path is not invoked.

Roadmap alignment:

- Implements `0.0.18-B` and codifies `DEC-0030`.

## 29. UI thread affinity and missing RuntimeKernelWorker (P0-7)

Status: **Resolved**

Problem:

- The host used `GetAwaiter().GetResult()` for mutex thread affinity.
- There was no dedicated runtime thread. `ImmediateUiScheduler` was synchronous.
- UI code could therefore block on kernel awaits, and the kernel had no single, named thread to reason about for `SafeHandle` ownership.

Decision:

- Add a `RuntimeKernelWorker` that owns a single dedicated thread (or `LongRunning` task) and exposes `Enqueue(Func<CancellationToken, Task>)` and `Enqueue<T>(Func<CancellationToken, Task<T>>)`.
- UI and the Application layer enqueue kernel work asynchronously; the host never blocks the UI thread.
- The worker is registered as an `IHostedService` in `0.0.19` and started/stopped by the Generic Host lifecycle.
- `Program.cs` becomes `async Task<int>` so the host's `StartAsync` / `StopAsync` are awaited instead of `GetAwaiter().GetResult()`. (Resolved by `0.0.20` packet 3 and `DEC-0034`.)
- The `RuntimeProcessHost` is wired into the host through the new `AddRuntimeProcessHost` DI extension and resolves the same `RuntimeKernelWorker` singleton that the host starts as an `IHostedService`. (Resolved by `0.0.20` packet 3 and `DEC-0034`.)

Files/classes:

- `src/Zapret2Pilot.Runtime/Hosting/RuntimeKernelWorker.cs` (implemented in `0.0.20` packet 1, registered in `0.0.20` packet 3)
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` (marshalled onto the worker in `0.0.20` packet 2, registered in `0.0.20` packet 3)
- `src/Zapret2Pilot.App/Program.cs` (async entry point in `0.0.20` packet 3)
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` (new `AddRuntimeProcessHost` extension in `0.0.20` packet 3)

Tests:

- Host commands execute on the dedicated thread (assert `Thread.CurrentThread.ManagedThreadId`). Implemented in `RuntimeKernelWorkerTests.Enqueue_RunsOnDedicatedThread`.
- UI thread is not blocked for more than 1 ms during a kernel call. Implemented in `RuntimeKernelWorkerUiNonBlockingTests.Enqueue_ReturnsImmediately_WithoutBlockingCaller` and `Enqueue_DoesNotBlockCaller_WhenWorkItemAwaitsForever` (caller-side bound is 5 ms over a 100 ms work item).
- Cancellation is honored. Implemented in `RuntimeKernelWorkerTests.Cancellation_Honored`.

Roadmap alignment:

- Implemented by `0.0.20` (worker: packet 1, worker-marshalled host: packet 2, host wiring + async Main: packet 3). Codifies `DEC-0033` and `DEC-0034`. The 0.0.18 milestone added the **decision** and the documentation; the worker and the host integration landed in 0.0.20.