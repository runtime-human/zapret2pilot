# Zapret2Pilot / Z2P — Roadmap

Versioning starts from `0.0.1` and increments by patch versions during early development.

Implementation is performed through ChatGPT in small patches. Codex is not the implementation executor for this project.

## 0.0.1 — Repo bootstrap and initial app skeleton

Goal:

- create the first code skeleton without any real runtime launch;
- establish .NET 10 solution structure;
- establish Avalonia + ReactiveUI + Generic Host baseline;
- keep runtime/process work out of the first patch.

Substeps:

### 0.0.1-a — Build foundation

Status: Implemented.

Implemented in:

- solution file;
- `src/` and `tests/` layout;
- `Directory.Build.props`;
- `Directory.Packages.props`;
- `global.json`;
- `VERSION = 0.0.1`;
- `.gitignore`;
- minimal root `README.md`;
- `Zapret2Pilot.Core` skeleton;
- `Zapret2Pilot.Application` skeleton;
- `Zapret2Pilot.Core.Tests`;
- `Zapret2Pilot.Application.Tests`.

Scope:

- solution file;
- `src/` and `tests/` layout;
- `Directory.Build.props`;
- `Directory.Packages.props`;
- `global.json`;
- `VERSION = 0.0.1`;
- `.gitignore`;
- minimal root `README.md`;
- Core project skeleton;
- Application project skeleton;
- basic test projects.

Non-goals:

- no Avalonia UI yet;
- no real winws2 launch;
- no RuntimeSupervisor;
- no Windows Job Objects;
- no SQLite;
- no Auto Doctor.

### 0.0.1-b — Avalonia + ReactiveUI shell

Status: Implemented.

Implemented in:

- `src/Zapret2Pilot.App`;
- Avalonia desktop app project;
- ReactiveUI baseline;
- System.Reactive baseline;
- `Microsoft.Extensions.Hosting` inside app;
- light theme placeholder;
- `MainWindow`;
- sidebar skeleton;
- dashboard skeleton with mock/design-time data;
- `MainWindowViewModel` based on ReactiveUI;
- ViewModel tests;
- solution update;
- README status update.

Scope:

- Avalonia desktop app project;
- ReactiveUI baseline;
- `Microsoft.Extensions.Hosting` inside app;
- light theme placeholder;
- MainWindow;
- sidebar skeleton;
- dashboard skeleton with mock/design-time data;
- ViewModel tests.

Non-goals:

- no real winws2 launch;
- no runtime process logic in ViewModel;
- no Windows Service;
- no IPC service layer;
- no tray yet.

Acceptance for 0.0.1:

- `dotnet restore` passes;
- `dotnet build -c Release` passes;
- `dotnet test -c Release` passes;
- app starts and shows mock dashboard shell;
- no Windows Service;
- no IPC service layer;
- no `Process.Start` / `Process.Kill` runtime logic;
- no `.bat` / `.cmd` wrapper architecture;
- no real `winws2` launch.

## 0.0.2 — Core primitives

Scope:

- `Result<T>`;
- `Result`;
- `Unit`;
- `ErrorInfo`;
- `ErrorSeverity`;
- `ErrorCategory`;
- typed IDs:
  - `ProfileId`;
  - `StrategyPackId`;
  - `RuntimePlanId`;
  - `RuntimeSessionId`;
  - `RuntimeTransactionId`;
  - `HostlistId`;
  - `ProbeSessionId`;
  - `EventId`.

Acceptance:

- unit tests for primitives;
- no dependency from Core to UI/Windows/SQLite.

## 0.0.3 — Application command foundation

Scope:

- `IAppCommand<TResponse>`;
- `ICommandBus` with single generic response inference;
- `ICommandHandler<TCommand,TResponse>`;
- no runtime commands yet;
- basic handler resolution tests.

Acceptance:

- CommandBus call site does not require two explicit generic parameters;
- no runtime process logic.

## 0.0.4 — UI navigation foundation

Scope:

- `NavigationRouter`;
- `RouteId`;
- page factory abstraction;
- dashboard/profile/settings route placeholders;
- UI scheduler abstraction.

Acceptance:

- ViewModels remain ReactiveUI-based;
- no CommunityToolkit.Mvvm ViewModels;
- no direct background-thread UI mutation.

## 0.0.5 — Storage foundation

Scope:

- SQLite connection factory;
- DbInitializer;
- migrations foundation;
- settings repository;
- event journal skeleton.

Required PRAGMA:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

Acceptance:

- SQLite initializer tests;
- no UI direct database access.

## 0.0.6 — File safety foundation

Scope:

- ProgramData layout;
- AtomicFileWriter;
- SafePathResolver;
- diagnostics redaction first version.

Acceptance:

- atomic write tests;
- path traversal tests;
- redaction tests.

## 0.0.7 — Runtime ownership foundation

Scope:

- Global Mutex ownership;
- runtime lock metadata;
- stale lock recovery;
- process ownership detector interfaces.

Acceptance:

- lock file is metadata only;
- mutex is atomic ownership primitive;
- stale lock scenarios tested.

## 0.0.8 — Windows Job Objects + fake runtime

Scope:

- Windows Job Object factory;
- RuntimeProcessHost abstraction;
- fake runtime test tool/project;
- kill-on-close integration test design.

Acceptance:

- fake runtime is not in production Runtime project;
- RuntimeProcessHost can attach process to Job Object;
- Windows-only test proves child runtime termination when job handle closes.

## 0.0.9 — ProfileDocument / ProfileDefinition

Scope:

- profile document DTO;
- validated domain definition;
- trust level;
- schema version;
- validation pipeline.

Acceptance:

- compiler does not accept raw ProfileDocument;
- invalid profile cannot reach runtime compiler.

## 0.0.10 — Profile compiler and deterministic cache

Scope:

- StrategyPack model;
- Hostlist model;
- CompiledZapretPlan;
- RuntimePlanCacheKey;
- golden test skeleton.

Acceptance:

- cache key includes all inputs;
- hostlist/profile/strategy change causes cache miss;
- golden snapshots exist for built-ins.

## 0.0.11 — Runtime manifest verification

Scope:

- RuntimeAssetManifest;
- SHA-256 verification;
- runtime compatibility checker;
- missing/quarantined file diagnostics.

Acceptance:

- runtime cannot start if required asset missing or hash mismatch.

## 0.0.12 — RuntimeSupervisor with fake runtime

Scope:

- RuntimeSupervisor;
- RuntimeTransactionManager;
- RuntimeKernelStateStore;
- CrashLoopGuard;
- OperationGate integration.

Acceptance:

- fake runtime can start/stop/crash;
- crash loop uses backoff;
- operation races are blocked.

## 0.0.13 — Real winws2 start/stop

Scope:

- real runtime launch;
- Job Object assignment;
- ownership mutex lifecycle;
- metadata lock lifecycle;
- dashboard real state.

Acceptance:

- start/stop works;
- full exit stops runtime by default;
- crash path does not leave orphan process.

## 0.0.14 — Runtime health and network changes

Scope:

- RuntimeHealthMonitor;
- process monitoring;
- network change hooks;
- degraded/crashed/blocked states;
- tray status states.

Acceptance:

- NetworkChangedEvent published;
- active runtime shows warning after network change;
- tray status reflects runtime state.

## 0.0.15 — Diagnostics bundle

Scope:

- diagnostics report;
- redacted export;
- crash report;
- retention policy;
- logs screen foundation.

Acceptance:

- export contains no raw URL/query/cookie data by default;
- retention removes old events.

## 0.0.16 — P0 probes and key service check

Scope:

- DNS probe;
- TCP probe;
- TLS/SNI probe;
- HTTP probe;
- YouTube/Discord/Telegram check;
- ProbeFailureClass.

Acceptance:

- Check Now does not change profile;
- HTTP 403/429/5xx not automatically DPI failure.

## 0.0.17 — Auto Doctor Quick

Scope:

- preflight;
- baseline probes;
- diagnosis;
- candidate selection;
- scoring weights;
- recommendation.

Acceptance:

- bounded timeout;
- table-driven scoring tests;
- no temporary runtime left behind.

## 0.0.18 — Auto Doctor Full and apply

Scope:

- candidate testing;
- temporary runtime sessions;
- candidate matrix;
- apply recommendation with rollback.

Acceptance:

- failed candidate rolls back;
- user sees downtime warning before apply.

## 0.0.19 — Windows integration

Scope:

- tray menu;
- scheduled task autostart design/prototype;
- HVCI/WinDivert diagnostics;
- Windows 11 custom titlebar decision.

Acceptance:

- tray icon status works;
- autostart requires explicit user consent;
- HVCI blocked state is understandable.

## 0.0.20 — Internal MVP candidate

Scope:

- elevated launch;
- dashboard;
- profiles;
- rules;
- runtime start/stop;
- diagnostics;
- Auto Doctor Quick/Full;
- tray;
- basic docs.

Acceptance:

- release checklist passes;
- all P0 tests pass;
- no Windows Service;
- no traffic router;
- no raw bat/cmd architecture.

## P0 backlog

- Generic Host inside app.
- ReactiveUI + UI scheduler.
- Job Objects.
- Global Mutex + lock metadata.
- SQLite WAL.
- AtomicFileWriter.
- SafePathResolver.
- Deterministic RuntimePlanCacheKey.
- Runtime manifest verification.
- OperationGate.
- DiagnosticsRedactor.
- Fake runtime outside production.

## P1 backlog

- Scheduled Task autostart.
- HVCI/Memory Integrity diagnostics.
- RuntimePlanDiff UI.
- Profile pinning.
- Network-aware profile binding.
- Storage retention service.
- SmartScreen/signing documentation.

## P2 backlog

- Runtime updater with rollback.
- Signed strategy packs.
- QUIC probe.
- Advanced DPI lab mode.
- Community profiles.
- Dark theme.
- Native AOT for CLI/tooling only.
