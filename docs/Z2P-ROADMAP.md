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

Status: Implemented.

Implemented in:

- `Result`;
- `Result<T>`;
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
  - `EventId`;
- unit tests for result primitives and typed IDs;
- `VERSION = 0.0.2`;
- README status update.

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

Status: Implemented.

Implemented in:

- `IAppCommand<TResponse>`;
- `ICommandBus` with single generic response inference;
- `ICommandHandler<TCommand,TResponse>`;
- `CommandBus` backed by `IServiceProvider.GetService(Type)`;
- exact runtime command type handler resolution;
- missing handler failure result;
- command bus tests for successful dispatch, missing handler, null command and handler failure propagation;
- `VERSION = 0.0.3`;
- README status update.

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

Status: Implemented.

Implemented in:

- `RouteId` strongly typed shell route identifier;
- `INavigationRouter`;
- `NavigationRouter`;
- `INavigationPageFactory`;
- `NavigationPageFactory`;
- `NavigationPageViewModel` placeholder page representation;
- `NavigationItemViewModel` ReactiveUI sidebar item model;
- dashboard/profile/settings placeholder routes;
- `IUiScheduler` abstraction;
- `ImmediateUiScheduler` synchronous initial scheduler;
- shell XAML bindings for route switching;
- ViewModel tests for route selection, unknown route behavior and scheduler behavior;
- `VERSION = 0.0.4`;
- README status update.

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

Status: Implemented.

Implemented in:

- `Zapret2Pilot.Storage` project;
- `Zapret2Pilot.Storage.Tests` project;
- `Microsoft.Data.Sqlite` storage provider;
- SQLite connection factory;
- DB initializer;
- migrations foundation;
- `app_settings` table;
- `event_journal` table;
- settings repository skeleton;
- event journal skeleton;
- file-backed SQLite tests;
- required SQLite PRAGMA:
  - `journal_mode=WAL`;
  - `busy_timeout=5000`;
  - `synchronous=NORMAL`;
  - `foreign_keys=ON`;
- `VERSION = 0.0.5`;
- README status update.

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

Status: Implemented.

Implemented in:

- `Zapret2Pilot.Infrastructure` project;
- `Zapret2Pilot.Infrastructure.Tests` project;
- `AppDataLayout`;
- `AppDataPathProvider`;
- `SafePathResolver`;
- `AtomicFileWriter`;
- `DiagnosticsRedactor`;
- tests for app data layout creation;
- tests for path traversal prevention;
- tests for atomic text writes;
- tests for diagnostics redaction;
- `VERSION = 0.0.6`;
- README status update.

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

## 0.0.8 — Job Objects foundation

Status: Implemented.

Scope:

- Windows Job Object primitive;
- close-time child process cleanup configuration;
- safe native handle lifetime;
- unsupported-platform guard;
- focused tests for supported and unsupported behavior.

Acceptance:

- primitive can be created and disposed safely on Windows;
- native handle ownership is deterministic;
- unsupported platforms return controlled behavior;
- production runtime code still has no real process-launch integration.

## 0.0.9 — Job Assignment Foundation

Status: Implemented.

Scope:

- `AssignProcessToJobObject` P/Invoke seam;
- `IRuntimeJobObjectProcessAssigner` abstraction;
- `RuntimeJobObjectProcessAssigner` production implementation;
- `RuntimeProcessHandle` process-handle wrapper;
- fake-native `IJobObjectNativeApi` seam for unit tests;
- platform guards for non-Windows hosts;
- result model that maps native error codes to typed statuses;
- focused unit tests covering all rejection paths and error mapping.

Acceptance:

- `dotnet build` and `dotnet test` pass;
- no real process is launched in any unit test;
- production runtime code carries no `Process.Start`, `Process.Kill`, `winws2`, `WinDivert`, or `WindowsService` references;
- handle lifetime remains owned by `RuntimeJobObject`; `SafeProcessHandle` is never exposed publicly.

## 0.0.10 — Runtime Detector Implementation

Status: Implemented.

Scope:

- `IProcessSnapshot` immutable process introspection seam (nullable `CommandLine`);
- `IProcessSystemAccessor` testable seam with a Windows production accessor (`WindowsProcessSystemAccessor`);
- `RuntimeOwnershipDetector` real verification implementation: PID, process name, executable path, command-line hash, plan hash and process start time (±5 s);
- `RuntimeOwnershipVerificationResult` factory methods for all mismatch statuses and `CommandLineUnverifiable`;
- `RuntimeOwnershipVerificationStatus.CommandLineUnverifiable = 8`;
- `IRuntimeOwnershipDetector.Verify(RuntimeLockMetadata, string expectedPlanHash)` signature;
- `FakeProcessSystemAccessor` and focused `RuntimeOwnershipDetector` unit tests;
- contract tests updated to the new two-argument signature;
- `DEC-0028 — Nullable CommandLine in IProcessSnapshot`.

Acceptance:

- `dotnet build` and `dotnet test` pass on the whole solution;
- no real process is launched in any unit test;
- `WindowsProcessSystemAccessor` does NOT retrieve another process's real command line (returns `null` with a `TODO(0.0.17)` marker);
- no new NuGet packages and no unsafe P/Invoke for process introspection;
- verification order matches the specification exactly;
- `IProcessSnapshot.CommandLine` is `string?` and is never coerced to non-null.

## 0.0.11 — Runtime Asset Manifest

Status: Implemented.

Scope:

- `Zapret2Pilot.Engine.Zapret2` project: a Zapret2 engine adapter that owns runtime asset verification, profile compilation and `winws2` argument building (no real process launch yet);
- runtime asset manifest model: `ZapretAssetManifest`, `ZapretRuntimeAsset`, `AssetKind` and `ZapretAssetVerificationSummary`;
- `ZapretAssetVerifier` that verifies presence, path safety and SHA-256 hash of every asset declared in the manifest and returns a `Result<ZapretAssetVerificationSummary>`;
- `ISafePathResolver` minimal abstraction in `Zapret2Pilot.Core.FileSystem`, implemented by `Zapret2Pilot.Infrastructure.FileSystem.SafePathResolver`, so that `Engine.Zapret2` stays free of `Infrastructure` and remains unit-testable with a fake resolver;
- `Zapret2Pilot.Engine.Zapret2.Tests` with focused tests for: valid manifest, missing executable, hash mismatch, and path escape attempt;
- `DEC-0029 — ISafePathResolver abstraction in Core`.

Acceptance:

- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole solution;
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole solution;
- no real `winws2` process is launched by `Engine.Zapret2` or by its tests;
- the asset verifier uses `ISafePathResolver` from Core, not a concrete Infrastructure type;
- no new NuGet packages.
