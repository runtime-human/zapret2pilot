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
