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

Status: **Accepted / P0**

Decision:

- Add CrashLoopGuard.
- Use exponential backoff.
- Reset crash counter only after stability window and successful health check.

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