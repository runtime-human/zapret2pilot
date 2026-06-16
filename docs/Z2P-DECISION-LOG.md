# Zapret2Pilot / Z2P — Decision Log

This is a lightweight decision log. Full ADR files can be created later under `docs/adr/`.

## DEC-0001 — Product identity

Date: 2026-06

Decision:

- Product name: Zapret2Pilot.
- Short name: Z2P.
- Main executable: z2p.exe.

Rationale:

- Full name is clear for UI/docs.
- Short name is practical for executables, commands, files and logs.

## DEC-0002 — No Windows Service in MVP

Date: 2026-06

Decision:

- Do not use Windows Service in the initial architecture.
- Use elevated single-process desktop app.

Rationale:

- Simpler installation and implementation.
- No service lifecycle/IPC complexity in MVP.

Consequence:

- UAC appears on each full app launch.
- Runtime Kernel must compensate with process safety, Job Objects, mutex ownership and recovery.

## DEC-0003 — Elevated single-process app

Decision:

- `z2p.exe` runs elevated.
- While app is open or in tray, runtime operations do not require repeated UAC.

Consequence:

- Elevated UI has higher security risk.
- Must use command allowlist, SafePathResolver, AtomicFileWriter, trust levels and no arbitrary execution.

## DEC-0004 — Generic Host inside app

Decision:

- Use `Microsoft.Extensions.Hosting` inside Avalonia app.
- This is not a Windows Service.

Rationale:

- Structured lifecycle.
- DI/config/logging.
- Clean `IHostedService` startup/shutdown.

## DEC-0005 — ReactiveUI for Presentation Layer

Decision:

- Use ReactiveUI + System.Reactive for UI ViewModels.
- Do not mix with CommunityToolkit.Mvvm ViewModels.

Rationale:

- Z2P is event-heavy.
- Runtime/health/log/diagnostic streams map naturally to observables.
- Scheduler-aware UI updates are important for Avalonia.

## DEC-0006 — Runtime Kernel inside z2p.exe

Decision:

- Runtime lifecycle is centralized in Runtime Kernel.

Includes:

- RuntimeSupervisor;
- RuntimeProcessHost;
- RuntimeTransactionManager;
- RuntimeKernelStateStore;
- RuntimeOwnership primitives;
- CrashLoopGuard.

## DEC-0007 — Job Objects are mandatory

Decision:

- RuntimeProcessHost must use Windows Job Objects with kill-on-close behavior.

Rationale:

- Prevent orphan `winws2.exe` after `z2p.exe` crash.

## DEC-0008 — Runtime ownership = Mutex + metadata lock

Decision:

- Atomic ownership uses Global Mutex.
- Lock file is metadata only.

Mutex:

```text
Global\Z2P_RUNTIME_OWNER_v1
```

Lock:

```text
C:\ProgramData\Zapret2Pilot\runtime\z2p-runtime.lock
```

## DEC-0009 — No network router inside Z2P

Decision:

- Z2P uses NavigationRouter, CommandBus, EventRouter and ProfileSelector.
- Z2P does not implement TrafficRouter, UrlRouter, PacketRouter, ProxyRouter or VPN-like router.

Rationale:

- zapret2/winws2 owns packet/runtime behavior.
- Z2P owns management, profiles, diagnostics and UI.

## DEC-0010 — Raw winws2 args are generated artifacts

Decision:

```text
ProfileDocument → ProfileDefinition → CompiledZapretPlan → winws2 args
```

Raw args are not source of truth.

## DEC-0011 — Deterministic RuntimePlanCache

Decision:

- RuntimePlanCacheKey is content-addressed.
- Cache by ProfileId only is forbidden.

Inputs:

- profile document hash;
- strategy pack hash;
- hostlist fingerprints;
- runtime manifest hash;
- compiler version;
- compiler options hash.

## DEC-0012 — SQLite WAL

Decision:

SQLite initializer must apply:

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

## DEC-0013 — Atomic generated files

Decision:

- All generated runtime files use AtomicFileWriter.
- Generated files are placed under ProgramData, not Program Files.

## DEC-0014 — SafePathResolver

Decision:

- All imported/user paths pass through SafePathResolver.
- Paths must remain inside allowed roots.

## DEC-0015 — Auto Doctor is bounded

Decision:

- Auto Doctor has Quick and Full modes.
- It is not a full DPI checker.
- It does not inspect every URL.
- It does not do MITM.

## DEC-0016 — Privacy-first diagnostics

Decision:

- No cloud telemetry.
- No browsing history.
- No URL query params.
- Diagnostics export is redacted by default.

## DEC-0017 — Fake runtime test-only

Decision:

- Fake runtime support must not live in production runtime project.
- Use `Zapret2Pilot.Testing` or test tool project.

## DEC-0018 — Scheduled Task autostart is P1

Decision:

- MVP does not promise UAC-free autostart.
- Future autostart may use Windows Task Scheduler after explicit user consent.

## DEC-0019 — Main UI title/status hierarchy

Decision:

- No duplicated runtime statuses in sidebar/topbar/dashboard.
- Main runtime status appears in dashboard status card.
- Tray icon separately shows runtime state.

## DEC-0020 — First milestone excludes real winws2

Decision:

- First implementation milestone is skeleton only.
- No real runtime launch before Runtime Kernel safety primitives exist.

## DEC-0021 — Initial solution foundation

Date: 2026-06

Decision:

- Initialize repository code with a classic `.sln` solution file.
- Use `net10.0` for initial Core/Application projects.
- Use Central Package Management via `Directory.Packages.props`.
- Use xUnit v3 for initial unit tests.
- Keep `0.0.1-a` build-only: no UI, runtime, storage or probing scope.

Rationale:

- `.sln` is stable and easy to review.
- `0.0.1-a` must establish restore/build/test foundation without introducing Avalonia, runtime process handling, SQLite or Auto Doctor risk.

## DEC-0022 — Avalonia + ReactiveUI shell baseline

Date: 2026-06

Decision:

- Add `Zapret2Pilot.App` as the Avalonia desktop shell project.
- Use `ReactiveUI.Avalonia`, not deprecated `Avalonia.ReactiveUI`.
- Use ReactiveUI ViewModels and System.Reactive commands for the shell baseline.
- Build and start `Microsoft.Extensions.Hosting` inside the Avalonia app process.
- Keep `0.0.1-b` UI-only with mock/design-time dashboard data.

## DEC-0023 — Core primitives baseline

Date: 2026-06

Decision:

- Add Core result primitives: `Result`, `Result<T>`, `Unit`, `ErrorInfo`, `ErrorSeverity` and `ErrorCategory`.
- Keep generic result factories on non-generic `Result`.
- Do not expose public static factory methods on `Result<T>`.
- Use `Unit.Instance => default`.
- Implement typed IDs as standalone `sealed record class` types.
- Do not introduce a `StringId` base class.
- Do not manually declare `operator ==` or `operator !=` on typed IDs.

Rationale:

- Core primitives must remain dependency-free and analyzer-safe under `TreatWarningsAsErrors=true`.
- Non-generic `Result` factories avoid CA1000 static members on generic types.
- Standalone typed ID records avoid invalid `default(struct)` states and keep public APIs explicit.
- Record-synthesized equality is accepted as language behavior; manual reference-type equality operators are not used.

Scope:

- `Zapret2Pilot.Core` only;
- unit tests under `Zapret2Pilot.Core.Tests`;
- no UI, Application, runtime, SQLite, Windows Service, WinDivert or real `winws2` integration.
