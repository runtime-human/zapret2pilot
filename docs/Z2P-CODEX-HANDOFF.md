# Zapret2Pilot / Z2P — Codex Handoff

Use this file when preparing Codex implementation tasks.

## Global rules

Codex must follow these decisions:

- product name: Zapret2Pilot;
- short name: Z2P;
- main executable: z2p.exe;
- Windows desktop app;
- C# / .NET 10 / Avalonia;
- elevated single-process app;
- no Windows Service;
- no IPC service layer;
- no VPN/proxy/MITM/per-URL traffic router;
- no `.bat` / `.cmd` wrapper architecture;
- UI never starts/kills processes directly;
- Runtime Kernel inside app;
- Generic Host inside Avalonia;
- ReactiveUI + System.Reactive for UI;
- no CommunityToolkit.Mvvm ViewModels;
- SQLite WAL;
- Job Objects for winws2;
- Global Mutex + lock metadata;
- AtomicFileWriter and SafePathResolver;
- deterministic RuntimePlanCacheKey;
- bounded Auto Doctor.

## Standard task format

Every Codex task must include:

1. Goal
2. Scope
3. Non-goals
4. Files to create/change
5. Public interfaces
6. Implementation notes
7. Tests
8. Acceptance criteria
9. Commands to run
10. Commit message

## First implementation task: 0.0.2 repository skeleton

Goal:

Create the initial repository skeleton without runtime implementation.

Scope:

- solution file;
- `src/` and `tests/` layout;
- Directory.Build.props;
- Directory.Packages.props;
- global.json;
- VERSION = 0.0.1 or 0.0.2 depending on release decision;
- .gitignore;
- initial README.

Non-goals:

- no real winws2;
- no RuntimeSupervisor implementation;
- no Auto Doctor;
- no SQLite implementation;
- no updater;
- no Windows Service.

Acceptance criteria:

- `dotnet restore` passes;
- `dotnet build -c Release` passes;
- test projects exist even if tests are minimal;
- no production project contains fake runtime helpers;
- documentation remains consistent with `/docs` canon.

## Second implementation task: Avalonia + ReactiveUI + Generic Host shell

Goal:

Create the first UI shell with mock dashboard data.

Scope:

- Avalonia app;
- ReactiveUI setup;
- `.UseReactiveUI()` integration if applicable;
- Generic Host inside app;
- basic DI;
- main window;
- sidebar;
- dashboard skeleton;
- light theme;
- no duplicate runtime status.

Non-goals:

- no real runtime;
- no storage;
- no process launch.

Acceptance criteria:

- app opens;
- dashboard visible;
- sidebar matches UI canon;
- top-right has only language/settings/window controls;
- no status widget in lower-left sidebar.

## Review rule

After every Codex change, run Red Team review for:

- architecture drift;
- direct runtime calls from UI;
- accidental Windows Service introduction;
- CommunityToolkit/ReactiveUI mixing;
- missing tests;
- unsafe file/path access.
