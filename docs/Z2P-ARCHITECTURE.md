# Zapret2Pilot / Z2P — Architecture

## 1. Summary

Zapret2Pilot is a Windows desktop control plane for zapret2/winws2.

Architecture formula:

```text
z2p.exe elevated once per app session
  + Avalonia UI
  + Generic Host inside app
  + ReactiveUI presentation
  + Runtime Kernel
  + Windows Job Objects
  + Global Mutex ownership
  + typed profile compiler
  + transactional runtime apply
  + zapret2 adapter
  + bounded Auto Doctor
  + SQLite WAL state
  + diagnostics bundle
  + privacy-first logs
```

## 2. Process model

Initial design does not use Windows Service.

`z2p.exe` is elevated by UAC at app start. While the app remains open or minimized to tray, runtime operations do not require repeated UAC.

This is a conscious product/engineering trade-off:

- simpler installation;
- no privileged service protocol;
- less IPC complexity;
- but stronger in-process safety is mandatory.

## 3. Layering

```text
Zapret2Pilot.App
  -> Zapret2Pilot.Application
  -> Zapret2Pilot.Core
  -> Zapret2Pilot.Runtime
  -> Zapret2Pilot.Engine.Zapret2
  -> winws2.exe
```

### Presentation Layer

Project: `Zapret2Pilot.App`

Responsibilities:

- Avalonia shell;
- ReactiveUI ViewModels;
- navigation;
- design system;
- dashboard;
- profiles UI;
- rules UI;
- Auto Doctor UI;
- diagnostics/logs UI;
- tray.

Forbidden:

- direct process control;
- direct SQLite access;
- direct WinDivert/zapret2 calls;
- business decisions.

### Application Layer

Project: `Zapret2Pilot.Application`

Responsibilities:

- use cases;
- CommandBus;
- OperationGate;
- EventRouter;
- DashboardSnapshotBuilder;
- application state orchestration;
- diagnostics orchestration.

### Core Domain

Project: `Zapret2Pilot.Core`

Responsibilities:

- pure models;
- typed IDs;
- Result/Error model;
- profile and strategy domain;
- runtime plan domain;
- probe/diagnostics domain.

No dependency on Avalonia, Windows APIs, SQLite, file system or Process.

### Runtime Kernel

Project: `Zapret2Pilot.Runtime`

Responsibilities:

- runtime lifecycle;
- transactions;
- state;
- crash recovery;
- ownership;
- health monitoring.

### Zapret2 Adapter

Project: `Zapret2Pilot.Engine.Zapret2`

Responsibilities:

- compile profile to runtime plan;
- build winws2 arguments;
- verify runtime assets;
- materialize hostlists;
- parse logs;
- classify zapret2/runtime errors.

## 4. Internal hosting

Use `Microsoft.Extensions.Hosting` inside Avalonia app.

Hosted services:

- RuntimeSupervisorHostedService;
- RuntimeHealthMonitorHostedService;
- RuntimeRecoveryHostedService;
- EventJournalHostedService;
- NetworkChangeMonitorHostedService;
- StorageRetentionHostedService.

The Generic Host is not a Windows Service. It is a structured in-process lifetime model.

## 5. UI architecture

Presentation uses Avalonia + ReactiveUI + System.Reactive.

Rules:

- do not mix ReactiveUI and CommunityToolkit.Mvvm ViewModels;
- all UI updates through UI scheduler;
- compiled bindings where possible;
- virtualized logs/events;
- lazy screen loading;
- no blocking operations on UI thread.

## 6. Runtime safety

RuntimeProcessHost must use Windows Job Objects with kill-on-close.

Runtime ownership:

```text
Global\Z2P_RUNTIME_OWNER_v1  -> atomic ownership
z2p-runtime.lock             -> recovery metadata
```

Lock file is never trusted alone.

`VerifiedRuntimeExecutablePath` is a value object that binds the executable path the host is about to launch to a passing `ZapretAssetVerificationSummary`. The type has no public string constructor; the only production producer is the workspace materializer after `ZapretAssetVerifier` has confirmed the manifest hash, the file hash and the path stay inside the allowed root. `RuntimeProcessStartContext` accepts `VerifiedRuntimeExecutablePath` (not a raw `string`), so the property "the host only launches what was just verified" is enforced by the type system rather than by caller discipline. Expired or missing verification produces an explicit failure. See `DEC-0032` and Critical Review #26.

`RuntimeKernelWorker` owns a single dedicated thread (a `LongRunning` task registered as an `IHostedService`) on which all Runtime Kernel state mutations happen. The worker exposes `Enqueue(Func<CancellationToken, Task>)` and `Enqueue<T>(Func<CancellationToken, Task<T>>)` for callers. The UI and the Application layer enqueue kernel work asynchronously and observe results through observables that marshal back to the UI scheduler; the host never blocks the UI thread and never exposes a kernel handle outside the worker thread. The worker is the canonical owner thread for `RuntimeOwnershipLease`, the Job Object handle and the running `Process` reference. See `DEC-0033` and Critical Review #29.

## 7. Runtime lifecycle

Start:

```text
StartRuntimeCommand
  -> OperationGate
  -> load/validate ProfileDefinition
  -> compile CompiledZapretPlan
  -> verify runtime assets
  -> create runtime transaction
  -> materialize workspace
  -> acquire ownership mutex
  -> write metadata lock file
  -> create Job Object
  -> start winws2
  -> assign to Job Object
  -> readiness check
  -> health check
  -> commit session
```

Stop:

```text
StopRuntimeCommand
  -> OperationGate
  -> graceful stop
  -> wait timeout
  -> kill if needed
  -> release ownership
  -> remove/update metadata lock
  -> publish event
```

Apply profile:

```text
snapshot current runtime
  -> compile new plan
  -> stop previous runtime
  -> start new runtime
  -> verify
  -> commit or rollback
```

## 8. Storage

SQLite must be configured with WAL, busy_timeout, synchronous=NORMAL and foreign_keys=ON.

UI never talks to SQLite directly.

ProgramData layout:

```text
C:\ProgramData\Zapret2Pilot\
  z2p.db
  logs\
  runtime\
  generated\
  profiles\
  hostlists\
  strategy-packs\
  diagnostics\
  backups\
```

## 9. Cache

RuntimePlanCacheKey must be deterministic and content-based.

Inputs:

- profile document hash;
- strategy pack hash;
- hostlist fingerprints;
- runtime manifest hash;
- compiler version;
- compiler options hash.

Any input change causes cache miss.

## 10. File safety

Generated runtime files must use AtomicFileWriter.

User/import paths must use SafePathResolver and remain inside allowed roots.

## 11. Auto Doctor

Auto Doctor is bounded and diagnostic.

Modes:

- Quick;
- Full.

It must separate DPI/network artifacts from application/service errors.

HTTP 403/429/5xx is not automatically DPI failure.

## 12. Diagnostics

Diagnostics Bundle includes redacted runtime/application data:

- app version;
- OS version;
- runtime version;
- active profile id;
- plan hash;
- recent events;
- redacted logs;
- Auto Doctor summary;
- probe summaries.

No URL history, cookies, query params or raw packet dumps by default.
