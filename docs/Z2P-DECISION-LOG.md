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

## DEC-0024 — Application command bus foundation

Date: 2026-06

Decision:

- Add Application command contracts: `IAppCommand<TResponse>`, `ICommandBus` and `ICommandHandler<TCommand,TResponse>`.
- Use single generic response inference at the command bus call site.
- Resolve handlers through `IServiceProvider.GetService(Type)`.
- Resolve handlers by exact runtime command type and response type.
- Return a failure `Result<TResponse>` when a handler is missing.
- Propagate handler failure results unchanged.
- Defer DI registration extensions until a composition-root-focused patch.

Rationale:

- Application command dispatch is an application-level routing mechanism, not network routing.
- The call site must remain concise: `commandBus.SendAsync(new Command(...), cancellationToken)`.
- The Application layer should not depend on Avalonia, ReactiveUI, `Microsoft.Extensions.Hosting`, SQLite or runtime process APIs.
- `IServiceProvider.GetService(Type)` is enough for this foundation and avoids introducing a DI extension dependency prematurely.

Scope:

- `Zapret2Pilot.Application` command contracts and `CommandBus` only;
- command bus unit tests under `Zapret2Pilot.Application.Tests`;
- no runtime commands, profile commands, UI navigation, storage, Windows Service, IPC, WinDivert or real `winws2` integration.

## DEC-0025 — UI navigation foundation

Date: 2026-06

Decision:

- Add shell UI navigation primitives under `Zapret2Pilot.App`.
- Use `RouteId` as a strongly typed shell route identifier.
- Add `NavigationRouter` and `INavigationRouter` for shell-level route state.
- Add `NavigationPageFactory` and `INavigationPageFactory` for placeholder page creation.
- Represent placeholder pages with `NavigationPageViewModel`.
- Represent sidebar items with ReactiveUI-based `NavigationItemViewModel`.
- Add placeholder routes for Dashboard, Profiles and Settings only.
- Add `IUiScheduler` and `ImmediateUiScheduler` as the initial UI scheduler seam.
- Keep navigation synchronous until a future background/event subscription patch needs real dispatching.

Rationale:

- UI navigation is application/shell routing, not network routing.
- The shell needs a minimal navigation seam before real profile, diagnostics, settings or storage pages are implemented.
- A scheduler seam is required by roadmap, but direct Avalonia Dispatcher usage is premature before background event streams exist.
- ReactiveUI remains the Presentation ViewModel framework.

Scope:

- `Zapret2Pilot.App` navigation, shell ViewModel, XAML and ViewModel tests only;
- no `Zapret2Pilot.Core` changes;
- no `Zapret2Pilot.Application` changes;
- no runtime/profile/storage/process logic;
- no SQLite;
- no Windows Service, IPC, WinDivert or real `winws2` integration.

## DEC-0026 — SQLite storage foundation

Date: 2026-06

Decision:

- Put the SQLite provider and storage implementation in `Zapret2Pilot.Storage`.
- Do not let UI/App talk to SQLite directly.
- Keep `Zapret2Pilot.Core` pure and free from SQLite, file system, Windows APIs and Process dependencies.
- Keep `Zapret2Pilot.Application` free from concrete SQLite provider dependency at this foundation stage.
- Use `Microsoft.Data.Sqlite`, not `System.Data.SQLite`.
- Use file-backed SQLite databases in tests where WAL behavior must be verified.
- Use `SqliteConnectionStringBuilder` for SQLite connection strings.
- Do not use `Cache=Shared` with WAL.
- Do not add EF Core in this patch.
- Do not add ProgramData resolver, SafePathResolver or AtomicFileWriter in this patch.
- Keep settings repository and event journal as skeletons only.

Rationale:

- Storage needs provider-specific SQLite behavior, but Core and UI boundaries must remain clean.
- WAL and related PRAGMA behavior must be tested against a real file-backed database, not an in-memory database.
- `SqliteConnectionStringBuilder` keeps connection string construction explicit and avoids premature custom parsing.
- ProgramData layout, path safety and atomic file writes belong to `0.0.6 — File safety foundation`.
- Repository skeletons establish persistence seams without introducing feature-specific settings models, retention services or UI/log-viewer integration.

Scope:

- `Zapret2Pilot.Storage` project;
- `Zapret2Pilot.Storage.Tests` project;
- SQLite connection factory;
- DB initializer;
- migrations foundation;
- `app_settings` table;
- `event_journal` table;
- settings repository skeleton;
- event journal skeleton;
- file-backed SQLite tests;
- no UI direct SQLite access;
- no Core SQLite/file-system dependency;
- no EF Core, ProgramData resolver, SafePathResolver, AtomicFileWriter, runtime process logic, Windows Service, IPC, WinDivert or real `winws2` integration.

## DEC-0027 — File safety foundation

Date: 2026-06

Decision:

- Add `Zapret2Pilot.Infrastructure` as the owner of initial file safety infrastructure.
- Keep `Zapret2Pilot.Core` free from file system, Windows APIs, SQLite and Process dependencies.
- Keep `Zapret2Pilot.App` free from direct path safety and atomic write ownership.
- Keep `Zapret2Pilot.Storage` SQLite-specific and do not turn it into a general infrastructure bucket.
- Add `AppDataLayout` and `AppDataPathProvider` for the initial app data layout rooted under common application data.
- Add `SafePathResolver` for user/import/generated relative path resolution inside allowed roots.
- Add `AtomicFileWriter` for same-directory temp file writes, flush, atomic replace/move and final readable verification.
- Add `DiagnosticsRedactor` v1 for deterministic package-free diagnostics redaction.
- Redact common key-value secrets: `token`, `api_key` and `password`.
- Redact `Authorization: Bearer ...` values.
- Redact Windows user profile names in `C:\Users\<name>\...` paths.
- Redact URL query and fragment data in diagnostics text.
- Do not add runtime process ownership, profile compiler, Auto Doctor, WinDivert, real `winws2`, Windows Service or IPC logic in this patch.

Rationale:

- File safety primitives are shared by future generated runtime files, diagnostics exports, hostlists, generated configs and lock metadata.
- Core must remain domain-only and dependency-free.
- UI must call application/infrastructure seams rather than own path validation or atomic writes directly.
- Storage remains focused on SQLite provider behavior and persistence primitives.
- Atomic writes need temp files in the same directory as the destination, flushed content and readable verification before higher-risk runtime artifacts are introduced.
- Safe path resolution must reject absolute paths, rooted paths, parent traversal segments, Windows drive-relative paths and NTFS ADS-style colon paths.
- Diagnostics redaction must remove URL query/fragment data and common secrets by default before diagnostics export exists.

Scope:

- `Zapret2Pilot.Infrastructure` project;
- `Zapret2Pilot.Infrastructure.Tests` project;
- app data layout abstraction;
- safe path resolver;
- atomic text writer;
- diagnostics redactor v1;
- unit tests for layout creation, path traversal prevention, atomic writes and diagnostics redaction;
- no Core, Application, App, Storage, runtime, profile compiler, Auto Doctor, Windows Service, IPC, WinDivert or real `winws2` integration.

## DEC-0028 — Nullable CommandLine in IProcessSnapshot

Date: 2026-06

Decision:

- `IProcessSnapshot.CommandLine` is typed as `string?` (nullable).
- The production `WindowsProcessSystemAccessor` always returns `null` for `CommandLine` in the `0.0.10` milestone.
- The detector treats `null` `CommandLine` as the new typed status
  `RuntimeOwnershipVerificationStatus.CommandLineUnverifiable` instead of failing the run.
- Real cross-process command-line retrieval is deferred to a later roadmap step (currently `0.0.17`) and must use `NtQueryInformationProcess` or WMI with explicit oracle approval.

Rationale:

- The managed `System.Diagnostics.Process` API does not expose another process's command line on Windows.
- Adding a `string` (non-nullable) `CommandLine` would force a placeholder, an empty string, or an exception — all of which would leak the limitation into the public seam and into the detector.
- A nullable `CommandLine` keeps the seam honest, lets the detector express the new "cannot verify" status explicitly, and prevents `CommandLineHashMismatch` from being used as a misleading catch-all.
- `NtQueryInformationProcess` requires `unsafe` P/Invoke (or a native helper) and pulls in additional attack surface; this is a high-impact change that must not be bundled into the `0.0.10` detector implementation.
- The `0.0.10` milestone delivers a real, testable detector for PID, process name, executable path, plan hash and start time. Command-line verification is intentionally the only step that is structurally present but unverified on Windows until `0.0.17`.

Consequence:

- `WindowsProcessSystemAccessor` carries a `TODO(0.0.17)` comment on the `null` `CommandLine` accessor.
- `RuntimeOwnershipVerificationStatus.CommandLineUnverifiable` is a terminal, typed status — it is not a fallback to `CommandLineHashMismatch` or `Unknown`.
- The runtime lock metadata still records `CommandLineHash` from the owner process; it is only the verification of that hash that is currently unverified.
- The new decision must be revisited before `0.0.17` is started.

## DEC-0029 — ISafePathResolver abstraction in Core

Date: 2026-06

Decision:

- Add a minimal `ISafePathResolver` abstraction to `Zapret2Pilot.Core.FileSystem`.
- Keep the concrete `SafePathResolver` implementation in `Zapret2Pilot.Infrastructure.FileSystem` and have it implement the Core interface.
- `Zapret2Pilot.Infrastructure` gains a `ProjectReference` to `Zapret2Pilot.Core` so the implementation can be expressed in terms of the abstraction.
- `Zapret2Pilot.Engine.Zapret2` depends on `Zapret2Pilot.Core` only, takes `ISafePathResolver` through constructor injection, and never references `Zapret2Pilot.Infrastructure`.

Rationale:

- The `Engine.Zapret2` adapter must verify the integrity of runtime assets on disk before any profile can be compiled or any `winws2` process can be launched. That requires resolving relative asset paths against an allowed root.
- Putting the path-resolver contract in `Core` keeps the engine adapter free of `Infrastructure` and of file-system APIs, preserves the existing `Core` ↔ `Infrastructure` direction (Core has no file-system dependency), and lets the engine be unit-tested with a fake resolver.
- Keeping the concrete implementation in `Infrastructure` preserves the ownership rule that "file safety primitives live in `Infrastructure`" and avoids duplicating path validation in two places.
- A single-method interface (`ResolveFilePath(string)`) is sufficient for the asset verification use case; broader path APIs (e.g. directory enumeration, atomic write) remain the responsibility of `Infrastructure` and are not pulled into `Core`.

Consequence:

- `Zapret2Pilot.Infrastructure` is no longer a leaf project in the dependency graph: it now references `Zapret2Pilot.Core`.
- The asset verifier (`Zapret2Pilot.Engine.Zapret2.Assets.ZapretAssetVerifier`) accepts an `ISafePathResolver` through its constructor and is fully unit-testable with a fake resolver.
- The production wiring of `SafePathResolver` into `Zapret2Pilot.Runtime` is intentionally out of scope for `0.0.11` and will land with the kernel-host wiring in a later milestone.

## DEC-0030 — Runtime Transaction Model

Date: 2026-06

Decision:

- `RuntimeTransactionManager` is the single source of truth for runtime state.
- `IsRunning`, `IsOwned` and `IsHealthy` may only be flipped through the transaction API (`Begin`, `Commit`, `Rollback`).
- After an irreversible kill (process terminated, Job Object drained, mutex released), the transaction is **never** rolled back to `Running`; cleanup failures are logged and the host state is cleared.
- The transaction remains in `Stopped` or `Failed` until the next `Begin` succeeds.

Rationale:

- Rolling back a transaction that corresponds to a killed process would silently leave the kernel thinking it is still running, with no live Job Object, no live process and no live mutex. A subsequent `StartAsync` would corrupt the host state machine.
- The decision codifies the rule "no rollback after irreversible kill" introduced in `0.0.18-B` (Critical Review #28).

Consequence:

- `StopAsync` failure paths must log a `Cleanup` warning with the precise failure and clear host state, then leave the transaction in `Stopped` / `Failed`.
- Subsequent `StartAsync` calls must succeed: the host must not be wedged by a previous kill.
- Tests in `Zapret2Pilot.Runtime.Tests` cover this invariant.

## DEC-0031 — FakeRuntime Gate

Date: 2026-06

Decision:

- No real `winws2` process may be launched from the Runtime Kernel until the FakeRuntime gate is green.
- The FakeRuntime gate is the suite of tests in `Zapret2Pilot.Testing.FakeRuntime` plus the tests in `Zapret2Pilot.Runtime.Tests` that exercise `RuntimeProcessHost` against `FakeRuntime`.
- The gate is the contract referenced by `0.0.17` and `0.0.18-A/B/C`: every P0 finding that touches the host must be reproducible against `FakeRuntime` before any real `winws2` launch.

Rationale:

- `winws2` is a privileged process that manipulates WinDivert, raw sockets and process state. Launching it against real network traffic without a known-good test target is exactly the failure mode the canon exists to prevent.
- `FakeRuntime` provides a deterministic, hermetic process for the host to launch, observe, kill and assert against. It is the only acceptable test target for the kernel in 0.0.18.
- The gate also blocks premature wins: every safety primitive (Job Object, VerifiedRuntimeExecutablePath, no rollback after kill) is exercised against `FakeRuntime` first.

Consequence:

- The 0.0.18 milestone is a "no real winws2" milestone by definition.
- Any future PR that tries to launch real `winws2` must be gated on a new decision that updates this entry and the canon.
- `Zapret2Pilot.Testing.FakeRuntime` is the test-only project allowed to expose a fake `winws2`. Production code must not import it.

## DEC-0032 — Verified Executable Launch

Date: 2026-06

Decision:

- `RuntimeProcessHost` may only launch paths that are an instance of `VerifiedRuntimeExecutablePath`.
- `VerifiedRuntimeExecutablePath` is a value object that can only be constructed from a passing `ZapretAssetVerificationSummary` (typically produced by `ZapretAssetVerifier`).
- The workspace materializer is the only place that produces this value object in production code.
- An expired or missing verification summary must produce an explicit failure, not a silent fallback to a raw `string` path.

Rationale:

- Before `0.0.18-A`, the host accepted any `RuntimeExecutablePath` string and launched it under the elevated token. A malicious or corrupted path could be executed.
- Binding the launch path to the verifier output makes "you can only launch what you just verified" a structural property of the type system, not a convention that callers may forget.
- The decision implements Critical Review #26 (P0-4) and is the foundation of any future real `winws2` launch.

Consequence:

- `RuntimeProcessStartContext` accepts `VerifiedRuntimeExecutablePath` (or an equivalent opaque type) instead of a raw `string`.
- Tests cover: host refuses an unverified path; launched path matches the manifest; expired verification summary cannot construct `VerifiedRuntimeExecutablePath`; `FakeRuntime` paths remain launchable.

## DEC-0033 — Runtime Kernel Single-Thread Worker

Date: 2026-06

Decision:

- All Runtime Kernel state mutations happen on a single dedicated worker thread owned by `RuntimeKernelWorker`.
- `RuntimeKernelWorker` exposes `Enqueue(Func<CancellationToken, Task>)` and `Enqueue<T>(Func<CancellationToken, Task<T>>)` for callers.
- The UI and the `Application` layer enqueue kernel work asynchronously; they never `Wait`, `Result` or otherwise block on a kernel future.
- The worker is registered as an `IHostedService` and started/stopped by the Generic Host lifecycle in `0.0.19`.

Rationale:

- The kernel owns the `Mutex` SafeHandle, the Job Object handle, the running process and the transaction state. None of these can be mutated from arbitrary threads without causing `SafeHandle` corruption, transaction drift or UI freezes.
- A dedicated worker thread makes "the kernel thread" a single, named, debuggable entity. It also makes thread-affinity exceptions (e.g. a non-owner-thread `Dispose`) detectable and reportable.
- The decision implements Critical Review #29 (P0-7).

Consequence:

- UI code must not call kernel APIs from the UI thread directly. It enqueues work to the worker and observes results through observables that marshal back to the UI scheduler.
- The worker owns the owner thread for `RuntimeOwnershipLease` and the canonical place where Job Object handles live.
- Manual `Thread.Sleep` / spin-wait paths in the host are replaced with `Task`-based awaits off the worker.

## DEC-0034 — Async Main & Non-Blocking Kernel Integration

Date: 2026-06

Decision:

- `Zapret2Pilot.App/Program.cs` must use `public static async Task<int> Main(string[] args)` and carry `[STAThread]`. The Generic Host's `StartAsync` and `StopAsync` are awaited; sync-over-async via `GetAwaiter().GetResult()` on the entry-point path is forbidden.
- All Runtime Kernel work issued from the UI / Application layer is enqueued through `RuntimeKernelWorker.Enqueue` (or `Enqueue<T>`). Callers observe the returned `Task`; they do not block the UI thread.
- The Generic Host owns the `RuntimeKernelWorker` lifetime: the worker is registered as a singleton and as an `IHostedService` via `AddRuntimeKernelWorker`. The worker is started and stopped by the host in the same way as every other `IHostedService`.
- `RuntimeProcessHost` is registered as a singleton via the new `AddRuntimeProcessHost` DI extension in `Zapret2Pilot.Runtime.DependencyInjection.RuntimeServiceCollectionExtensions`. The extension registers every constructor dependency (mutex, lock file store, stale lock recovery, workspace materializer, transaction manager, job object process assigner, host) as a singleton; the runtime directory is shared via a singleton `AppDataLayout` so factory lambdas stay free of captured locals, and the extension does NOT call `AppDataLayout.EnsureCreated()` (registration is pure).
- The order of registrations in `AppHost.Build` is `AddRuntimeKernelStateStore` → `AddRuntimeKernelWorker` → `AddRuntimeProcessHost`, so the process host resolves the same `RuntimeKernelWorker` instance the host starts.

Rationale:

- Sync-over-async on the UI thread is the exact failure mode Critical Review #29 (P0-7) was opened for. A blocking entry point makes any future "enqueue from UI" a freeze-the-UI bug.
- A dedicated worker thread is necessary for the ownership-mutex / Job Object thread affinity `RuntimeProcessHost` already documents in 0.0.20 packet 2. The host must therefore be reachable through the same DI container the rest of the kernel uses, not as a hard-coded singleton inside the entry point.
- Registering the worker as an `IHostedService` gives us deterministic, ordered start / stop semantics for free. The host's `StartAsync` returns only after the worker thread has been created, and the worker's `StopAsync` is awaited during shutdown.
- Keeping the `AddRuntimeProcessHost` extension pure (no `EnsureCreated` call) preserves the rule "DI registration is configuration, not side-effecting I/O". The kernel may decide when to materialise the layout on disk; the DI container never does.

Consequence:

- The UI / Application layer may NOT call `RuntimeProcessHost.StartAsync` / `StopAsync` (or any other kernel API) directly. It MUST go through `RuntimeKernelWorker.Enqueue` so the call lands on the dedicated worker thread and never blocks the UI thread.
- A new xUnit test in `Zapret2Pilot.Runtime.Tests.Hosting.RuntimeKernelWorkerUiNonBlockingTests` enforces the non-blocking contract: a work item that awaits 100 ms inside the worker does not delay the `Enqueue` call on the caller thread by more than 5 ms.
- `Program.Main` is `async Task<int>`; the Avalonia classic-desktop lifetime is started AFTER `host.StartAsync()` returns so the host services are live for the entire UI lifetime.
- Critical Review #29 (P0-7) is marked **Resolved** in `docs/Z2P-CRITICAL-REVIEW.md`.

## DEC-0035 — CrashLoopGuard

Date: 2026-07

Decision:

- `Zapret2Pilot.Runtime.Guard.CrashLoopGuard` is a pure in-memory safety primitive that owns the "do not restart yet" / "the runtime is irrecoverable" decision for the Runtime Kernel.
- The guard is a passive primitive: it owns no I/O, no process and no P/Invoke. A real restart supervisor (out-of-scope for `0.0.22`) is the only thing that may call into the guard.
- `CrashLoopGuardOptions` is a `sealed record` with constructor validation. Defaults: `BaseBackoff = 2 seconds`, `MaxBackoff = 5 minutes`, `StabilityWindow = 60 seconds`, `MaxConsecutiveFailures = 10`.
- The backoff formula is `min(BaseBackoff * 2^(consecutiveFailures - 1), MaxBackoff)`. The doubling saturates at `MaxBackoff` so the wait never exceeds the cap regardless of the failure count.
- Permanent lockout uses a strict `>` comparison against `MaxConsecutiveFailures`. With the default of 10, the 11th consecutive failure is the first to be rejected as a permanent lockout rather than a bounded backoff. Time passing on its own cannot escape permanent lockout — only an explicit `Reset()` call does.
- A successful start does not immediately reset the counter. The next `Check` call that runs at least `StabilityWindow` after the most recent event (success or failure, whichever is later) is the one that actually clears the counter and the failure timestamp. This is the "stability window" rule.
- `CrashLoopGuard` is registered as a singleton through the new `AddCrashLoopGuard(this IServiceCollection)` extension in `Zapret2Pilot.Runtime.DependencyInjection.RuntimeServiceCollectionExtensions`. The guard is intentionally NOT registered as an `IHostedService`: it owns no background timer, no resources to dispose and is a passive primitive that supervisors query on demand.
- `Program.cs` (`AppHost.Build`) calls `AddCrashLoopGuard` immediately after `AddRuntimeHealthMonitor` so the guard is part of the same composition root the rest of the Runtime Kernel uses.

Rationale:

- Critical Review finding #14 (P0) calls for a crash-loop backoff primitive that uses exponential backoff and resets the counter only after the runtime has been stable for a stability window. The decision codifies the rule and picks the exact rule for the boundary cases.
- The guard is a primitive, not a supervisor. Wiring it into `RuntimeProcessHost`, `RuntimeHealthMonitor` or any other kernel component is a future decision that needs its own design (when should the supervisor query the guard, what to do on a permanent lockout, how to surface the verdict to the user, etc.). Bounding `0.0.22` to the primitive itself keeps the milestone reviewable and the safety contract explicit.
- The "time since the most recent event" interpretation of the stability window (instead of the literal "time since the last success") is the only one that survives a `success → failure` interleaving without letting the system appear healthy just because a recent success exists. A fresh failure must always restart the stability window.
- The strict `>` boundary on the permanent lockout is documented in `CrashLoopGuard`'s XML doc and is tested by `RecordFailure_ExceedsMaxConsecutiveFailures_PermanentLockout`. It is the conservative choice (one more retry past the threshold) and matches the typical "N retries before giving up" semantic.
- The pure in-memory constraint (no I/O, no process, no P/Invoke) keeps the guard unit-testable, keeps the kernel free of hidden state machines and lets a future supervisor decide whether to persist the counter in SQLite as a separate concern.

Consequence:

- The 0.0.22 milestone adds `CrashLoopGuard` (and its `Options` / `Result` / `ICrashLoopGuard` companion types) under `Zapret2Pilot.Runtime.Guard`, registers it as a singleton, and provides a dedicated test suite (25 new tests in `tests/Zapret2Pilot.Runtime.Tests/Guard/`) that exercises every documented behavior including a concurrent stress test.
- The guard is NOT consumed by any other runtime component in this milestone. No existing type (`RuntimeProcessHost`, `RuntimeHealthMonitor`, `RuntimeKernelWorker`, `RuntimeTransactionManager`, `RuntimeKernelStateStore`, `RuntimeOwnershipMutex`, etc.) is modified.
- Critical Review finding #14 is marked **Resolved** in `docs/Z2P-CRITICAL-REVIEW.md`.
- `docs/Z2P-ROADMAP.md` gains a new `0.0.22 — Runtime Crash Loop Guard` section.
- A future milestone (not in scope for `0.0.22`) will design how the guard integrates with the rest of the Runtime Kernel — the integration itself, the supervisor restart loop, the SQLite persistence of the counter across restarts, the UI / dashboard exposure of the guard verdict and the user-facing recovery flow. That future milestone is gated on its own oracle review.

## DEC-0036 — RuntimeSupervisor

Date: 2026-07

Decision:

- Add the public contract `IRuntimeSupervisor` and the implementation `RuntimeSupervisor` under `Zapret2Pilot.Runtime.Supervisor` (`src/Zapret2Pilot.Runtime/Supervisor/`), together with the supporting types `RuntimeSupervisorState` (immutable snapshot) and `RuntimeSupervisorStatus` (the lifecycle enum: `Stopped`, `Starting`, `Running`, `Stopping`, `StartBlocked`).
- `RuntimeSupervisor` owns the runtime start / stop state machine. It is the only component that calls `IRuntimeProcessHost.StartAsync` / `StopAsync` on behalf of the application. The UI / Application layer must not call `IRuntimeProcessHost` directly; it observes `IRuntimeSupervisor` instead.
- `RuntimeSupervisor` implements `IHostedService` so the Generic Host starts and stops it together with the rest of the Runtime Kernel. The Generic Host owns the lifetime; the supervisor is a singleton.
- Every `StartAsync` call is preceded by `ICrashLoopGuard.Check`. A guard block is surfaced as a `RuntimeSupervisorStatus.StartBlocked` snapshot with the `CrashLoopGuardResult` carried in `RuntimeSupervisorState.GuardResult` and a typed `Result.Failure` returned to the caller — `IRuntimeProcessHost` is not touched.
- Failed starts call `ICrashLoopGuard.RecordFailure`. An `Exited` health snapshot (the load-bearing signal for an unexpected runtime crash) also calls `ICrashLoopGuard.RecordFailure` and triggers an automatic `StopAsync`.
- A transition into `RuntimeHealthState.Healthy` (the load-bearing signal for a successful start) calls `ICrashLoopGuard.RecordSuccess`.
- `RuntimeSupervisor` subscribes to `IRuntimeHealthMonitor.SnapshotChanged` on `IHostedService.StartAsync` and unsubscribes on `IHostedService.StopAsync`. The subscription is the single integration point with the health monitor.
- `RuntimeSupervisor` publishes immutable `RuntimeSupervisorState` snapshots through `CurrentState` and through a hot `StateChanged` observable backed by a `BehaviorSubject<RuntimeSupervisorState>`, so subscribers always see the most recent value plus every subsequent transition.
- `RuntimeSupervisor` is registered via the new `AddRuntimeSupervisor(this IServiceCollection)` extension in `Zapret2Pilot.Runtime.DependencyInjection.RuntimeServiceCollectionExtensions`. The extension registers `RuntimeSupervisor` as a singleton and the same instance under `IRuntimeSupervisor` and `IHostedService` (one instance, three service descriptors).
- `Program.cs` (`AppHost.Build`) calls `AddRuntimeSupervisor` immediately after `AddCrashLoopGuard` so the supervisor can resolve the same singleton guard the rest of the kernel uses.
- Start / stop calls are serialised on a private semaphore. A re-entrant `StartAsync` returns a typed `RuntimeSupervisorAlreadyRunning` failure rather than blocking or racing; an idle `StopAsync` is a successful no-op.
- View-model integration: `MainWindowViewModel` subscribes to `IRuntimeSupervisor.StateChanged` on the UI scheduler and surfaces `StartBlocked` through `LastAction` so the Avalonia UI can show a "too many crashes, retry in N seconds" / "permanent lockout" message.

Rationale:

- This decision closes Critical Review finding #14 (P0) by integrating the `CrashLoopGuard` primitive into the actual start / stop / exit flow. The 0.0.22 decision delivered the guard; 0.0.23 wires it in.
- A dedicated supervisor centralises the runtime lifecycle so the UI / Application layer interacts with a typed, observable contract instead of calling the process host directly. The Avalonia UI stays on the UI scheduler while the kernel runs on its dedicated worker thread; the observable is the bridge.
- Serialising start / stop on a private semaphore keeps the state machine in a single, named thread of control. Concurrent callers get a typed failure result rather than blocking, which is the only safe answer for an `IHostedService` exposed to the UI.
- The hot `StateChanged` observable (backed by `BehaviorSubject`) is the canonical place for any future feature (UI Start / Stop button, dashboard runtime card, automatic restart, Auto Doctor) to learn what the supervisor is doing without coupling to its private state.

Consequence:

- The UI / Application layer must not call `IRuntimeProcessHost` directly. It observes `IRuntimeSupervisor` and delegates all start / stop requests to it.
- Future restart logic, automatic restart, apply-profile and any other lifecycle-triggering feature must be coordinated through the supervisor; ad-hoc `IRuntimeProcessHost` callers are forbidden.
- `RuntimeSupervisor` does NOT launch a real `winws2` process in 0.0.23. The integration is exercised against fake `IRuntimeProcessHost` and `IRuntimeHealthMonitor` collaborators; a real launch is gated on an explicit, oracle-approved milestone.
- The supervisor is covered by `tests/Zapret2Pilot.Runtime.Tests/Supervisor/RuntimeSupervisorTests.cs` (focused unit tests using a shared `FakeClock` for deterministic time), by the DI test `AddRuntimeSupervisorRegistersSupervisorAsHostedService` in `RuntimeServiceCollectionExtensionsTests`, and by the view-model test `StartBlocked_UpdatesLastAction` in `MainWindowViewModelTests` (using `FakeRuntimeSupervisor` in `tests/Zapret2Pilot.App.ViewModelTests/`).
- `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md` gain the 0.0.23 `RuntimeSupervisor` bullets and the new focused-test commands.

---

## v6 master plan — accepted critical corrections

The decisions below record the critical corrections accepted from
`docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3 (v5 corrections adopted
by v6) and one v6 §0.4 / §7 supporting decision. Each entry is
intentionally concise: it pins the v6 reference, the corrected
constraint and the immediate consequence for the next milestone.
Per-milestone implementation details belong in
`docs/Z2P-IMPLEMENTATION-STATUS.md`; this log records the *decision*
and the corrected invariant, not the work breakdown.

## DEC-0037 — SQLite native runtime is a P0 release blocker

Date: 2026-07-04

Decision:

- Z2P must not ship with the deprecated/vulnerable native SQLite
  package currently suppressed through `NuGetAuditSuppress`.
- Storage migrates to `Microsoft.Data.Sqlite.Core` plus a
  controlled, version-pinned native `sqlite3.dll` selected from
  the latest officially fixed stable SQLite branch at the time of
  the `0.0.27` implementation and re-verified before RC.
- Production Z2P refuses to open the database for writes when the
  resolved native SQLite version is unknown or below the recorded
  minimum (current review baseline: `SQLite 3.51.3+` or an
  officially maintained backport containing the same WAL-reset
  corruption fix).
- Startup verifies the actual native version via
  `SELECT sqlite_version()`, `PRAGMA compile_options`,
  `PRAGMA journal_mode`, `PRAGMA foreign_keys` and refuses to
  continue on an unsafe result.
- A dedicated `Z2P.STORAGE.SQLITE.UNSAFE_VERSION` error is
  produced; `NuGetAuditSuppress` for this dependency is removed
  before real-runtime approval.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 1 and §17.1.

Consequence:

- `0.0.27` cannot be marked `Implemented` while the
  `NuGetAuditSuppress` for the current native SQLite package
  remains. Removal of the suppression is part of that milestone.
- The compiled native `sqlite3.dll` is shipped as a Z2P-controlled
  native binary: name, version, source, archive hash, build
  provenance, license and final shipped hash are recorded in the
  release provenance (v6 §24.4).

## DEC-0038 — Portable staging covers the whole elevated application

Date: 2026-07-04

Decision:

- The portable package root is untrusted. A elevated managed
  application cannot reliably verify its own portable directory
  after the managed host has already loaded native/managed files
  from it.
- The portable production flow is:
  untrusted portable folder → minimal signed bootstrapper →
  complete app payload verification → protected versioned app
  staging → re-verification by final handles → launch staged
  elevated `z2p.exe` → runtime bundle verification / staging.
- Staging `winws2` alone is insufficient; staging the whole
  elevated `z2p.exe` payload is mandatory.
- The public portable artifact is a ZIP with `z2p-portable.exe`
  as the only supported entry point; `payload\z2p.exe` from the
  ZIP root is not a supported entry point. Main app verifies the
  bootstrap handoff / `AppPayloadId` and rejects unsafe direct
  portable launch in Stable flavor.
- Public portable ZIP is forbidden before `0.0.42` (v6 §52).
  The `0.0.28` spike only proves the architecture; the public
  artifact is produced in `0.0.42`.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 2, §10.4, §10.5, §10.6, §29 Scope J, §31
Scope C, §45.

Consequence:

- `Zapret2Pilot.PortableBootstrapper` is a separate
  NativeAOT-published, signed, single native executable and the
  only portable entry point. It performs package-root detection,
  embedded package identity check, manifest size / hash /
  signature, full file inventory, DLL allowlist, UAC relaunch,
  protected versioned staging, ACL setup, final staged file
  identity verification and allowlisted bootstrap handoff.
- The bootstrapper does not run `winws2`, does not open SQLite,
  does not import profiles and does not load remote code.

## DEC-0039 — Single Runtime authority: RuntimeKernelLoop

Date: 2026-07-04

Decision:

- The current `RuntimeSupervisor.SemaphoreSlim` and the generic
  `RuntimeKernelWorker.Channel` are merged into a single
  lifecycle authority named `RuntimeKernelLoop`. There is exactly
  one serialization layer; there is no separate semaphore /
  channel state machine pair.
- `RuntimeKernelLoop` owns: the bounded
  `Channel<RuntimeKernelCommand>`, a single reader, a dedicated
  named thread, the pure reducer and explicit effect intents.
- `IRuntimeSupervisor` becomes an Application-facing façade over
  the loop, not a second state machine.
- Operations carry explicit `OperationId` / `Generation`; effects
  return completions that are applied only when the operation id,
  generation and current state still match. Stale completions are
  recorded as `IgnoredStaleCompletion` events and do not mutate
  state.
- Cancellation before an irreversible boundary returns
  `Cancelled`; cancellation after an irreversible boundary
  produces `RollbackRequired` / `RecoveryRequired`, not an
  ordinary `Cancelled`.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 3, §5, §7.4, §27.

Consequence:

- `0.0.24` is the first milestone that ships
  `RuntimeKernelLoop` and the first milestone after which a
  real `winws2` is allowed (gated by `0.0.24`–`0.0.28` per
  v6 §26.1 and §32).
- The pre-`0.0.24` `RuntimeSupervisor` and
  `RuntimeKernelWorker` designs are marked *superseded*; the
  earlier "awaited delegates preserve dedicated-thread affinity"
  claim is removed from the architecture document.

## DEC-0040 — Secure process creation is the only production launcher

Date: 2026-07-04

Decision:

- Production runtime launch is the secure `STARTUPINFOEX` path:
  `CreateJobObjectW` with
  `SetInformationJobObject(KILL_ON_JOB_CLOSE)` →
  `InitializeProcThreadAttributeList` →
  `PROC_THREAD_ATTRIBUTE_JOB_LIST` (optionally
  `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`) → `CreateProcessW` with
  `EXTENDED_STARTUPINFO_PRESENT`, `CREATE_SUSPENDED` and
  `CREATE_UNICODE_ENVIRONMENT` → validate returned process
  identity → register process-handle wait → start bounded log
  pumps → `ResumeThread`.
- Fallback (`CREATE_SUSPENDED` → `AssignProcessToJobObject` →
  verify assignment → `ResumeThread`) is allowed only after an
  explicit compatibility decision and tests.
- `Process.Start` → `AssignProcessToJobObject` after execution
  began is forbidden. The production runtime launch path does
  not use `System.Diagnostics.Process.Start`.
- The launcher accepts only `VerifiedRuntimeBundle`, raw
  argument tokens, a controlled working directory, a minimal
  allowlisted environment block and an explicit `OperationId` /
  `Generation` / `PlanHash` / `RuntimeSessionId`. It does not
  accept an arbitrary environment dictionary or an unverified
  path.
- Windows command-line construction uses
  `WindowsCommandLineEncoder`, `ArgsFileSerializer` and
  `EnvironmentBlockBuilder`; `string.Join(" ", args)` is
  forbidden.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 4, §14.

Consequence:

- `0.0.26` ships the secure launcher and the
  `Zapret2Pilot.Platform.Windows` boundary. `0.0.29` is the first
  milestone allowed to launch a real `winws2`, and only behind
  the secure launcher.
- The runtime/Windows architecture test in v6 §20.9 fails the
  build if `Process.Start` appears in the production runtime
  launch path or if P/Invoke appears outside the Windows
  boundary.

## DEC-0041 — Compiler cache contract: explicit version axes and typed canonical hash

Date: 2026-07-04

Decision:

- The compiler cache key is computed from a typed
  `CanonicalHashWriter` (length-prefixed UTF-8 / binary values,
  deterministic ordering, explicit null/default semantics,
  version header) and the cache input explicitly records:
  `CanonicalizationVersion`, `CompilerCompatibilityVersion`,
  `CompilerOptionsVersion`, `ProfileDocumentHash`,
  `StrategyPackHashes`, `HostlistFingerprints` and
  `RuntimeBundleManifestHash`.
- Delimiter-based string concatenation of compiler inputs is
  forbidden.
- Compatibility between ProfileDocument, StrategyPack, Runtime
  Bundle and compiler is decided by the explicit version axes,
  not by the app informational version.
- Profile / Strategy / Bundle / Compiler / Canonicalization
  versions live in `Zapret2Pilot.Core` (or
  `Engine.Zapret2` for the compiler) and are referenced by
  every cache key, manifest and Evidence Pack.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 5, §11.3, §31 Scope E / F, §34 Scope E / F,
§7.1.

Consequence:

- The current compiler placeholder behavior (delimiter-based
  canonicalization, hostlist placeholder, executable path
  placeholder, no documented compiler version in the cache key)
  is marked *to be replaced* and is removed from the approved
  runtime flow at the `0.0.31` milestone.

## DEC-0042 — Restricted Generic Host configuration sources

Date: 2026-07-04

Decision:

- Security-critical options (deployment flavor, application
  staging root, runtime bundle root, runtime executable policy,
  trusted TUF root, repository base URIs, signing publisher
  policy, allowed Strategy Packs, developer runtime gate, driver
  policy) are sourced only from compiled constants, signed
  embedded resources, installer-created trusted machine
  configuration and the verified staged app manifest.
- Arbitrary environment variables, ordinary `appsettings`
  overrides, user settings, imported profiles, untrusted CLI and
  working-directory files cannot change these options.
- The CLI is allowlisted (`--safe-mode`,
  `--collect-diagnostics`, `--verify-runtime-bundle` in Stable
  build). Unknown arguments produce a typed bootstrap error and
  are not interpreted.
- Security-critical options use sealed records / classes,
  source-generated options validation and `ValidateOnStart`.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 6, §16.1, §16.2, §16.4, §16.5, §28
Scope A.

Consequence:

- `0.0.25` migrates to `Host.CreateApplicationBuilder` and
  builds the explicit configuration graph above. The
  pre-`0.0.24` "environment variable can override runtime root"
  and "user settings can override TUF root" patterns are
  removed from the codebase and the documentation.

## DEC-0043 — Windows-specific target framework split

Date: 2026-07-04

Decision:

- `Zapret2Pilot.Core`, `Zapret2Pilot.Application`,
  `Zapret2Pilot.Engine.Zapret2`, `Zapret2Pilot.Storage` and the
  platform-neutral portion of `Zapret2Pilot.Infrastructure` keep
  the generic `net10.0` target framework.
- `Zapret2Pilot.Platform.Windows`, `Zapret2Pilot.Runtime` and
  `Zapret2Pilot.App` use the explicit Windows target framework
  `net10.0-windows10.0.26100.0`.
- `Zapret2Pilot.PortableBootstrapper` ships as a Windows-specific
  NativeAOT target.
- Platform compatibility analyzer (CA1416) is enabled and
  treated as a build-time gate.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 7, §13.1, §16.6, §28 Scope C.

Consequence:

- The architecture test "App references
  `System.Diagnostics.Process`" and "PInvoke outside Windows
  boundary" (v6 §13.4, §20.9) becomes a CI failure when
  violated. The current `Runtime.Windows` namespace is treated
  as a migration boundary until the dedicated
  `Zapret2Pilot.Platform.Windows` project is extracted in
  `0.0.26`.
- If the final supported Windows baseline changes before
  release, the TFM / minimum OS build is updated by DEC and
  compatibility tests (v6 §16.6).

## DEC-0044 — Typed feature facades replace the reflection CommandBus

Date: 2026-07-04

Decision:

- The current reflection-based `CommandBus` is a temporary
  bootstrap artifact. It is replaced by compile-time typed
  feature facades:
  `IRuntimeUseCases`, `IProfileUseCases`, `IRulesUseCases`,
  `IAutoDoctorUseCases`, `IDiagnosticsUseCases`,
  `IRuntimeUpdateUseCases`, `IDataManagementUseCases`, plus
  `ICompatibilityUseCases` and `IDashboardQueries`.
- Each use-case method returns `Task<Result<T>>` /
  `Task<Result<Unit>>` with a typed request record, carries
  `OperationId`, deadline and cancellation semantics, and is
  decorated explicitly with validation, correlation, structured
  logging, application operation policy, deadline and
  authorization / trust classification.
- No `IServiceProvider.GetService(Type)` and no
  `MethodInfo.Invoke` in the runtime critical path. No MediatR
  dependency in MVP.
- Static `AppHost.Services` and `IServiceProvider` access from
  `App` / `ViewModels` is removed. The composition graph is
  validated in tests.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 8, §7.1, §7.2, §28 Scope D / E, §30
DashboardSnapshot.

Consequence:

- `0.0.25` introduces the feature-facade surface and removes
  the static service locator. `0.0.30` migrates the runtime
  critical flow off the reflection `CommandBus` and is the
  first milestone after which the runtime critical path
  contains no `MethodInfo.Invoke`.

## DEC-0045 — CI is a security boundary

Date: 2026-07-04

Decision:

- CI is treated as a security boundary for the Z2P supply
  chain, not just a build / test runner.
- Every GitHub Action is pinned to a full commit SHA. Mutable
  major tags are not accepted in the release workflow.
- Each workflow job has minimal `permissions`, a concurrency
  group that cancels stale PR runs, an explicit job timeout and
  no secrets in untrusted PR jobs.
- Release tags are protected / immutable by policy. The release
  environment is protected.
- Pull-request workflow runs (in this order) `dotnet format
  --verify-no-changes`, `dotnet restore --locked-mode`, Release
  build, unit tests, reducer / property tests, architecture
  conformance, storage tests, FakeRuntime tests, headless UI,
  NuGet audit, dependency review where available, CodeQL / SARIF
  where available and evidence artifact upload.
- CODEOWNERS cover Runtime, Windows, Storage, Updater,
  Installer and workflows.
- NuGet audit runs in `NuGetAuditMode=all` (or current
  equivalent). Security-critical / native audit suppressions
  cannot survive into Stable without a documented independent
  risk acceptance; the current SQLite suppression is explicitly
  not accepted for `0.1.0` (DEC-0037).

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 9, §23.2, §24.

Consequence:

- The pre-`v6` "actions referenced by mutable major tags" CI
  posture is replaced in `0.0.25` (action pin + permissions +
  format gate) and `0.0.27` (locked restore + audit hardening)
  per the v6 §24 plan.
- The architecture / policy / threat / attack-corpus tests in
  v6 §20.8, §20.9, §20.10, §24.7 are the load-bearing
  acceptance for this decision and are added milestone by
  milestone.

## DEC-0046 — Error, cancellation and deadline contracts are systemic

Date: 2026-07-04

Decision:

- Every long-running operation has an `OperationId`, a
  `RequestedAtUtc`, a monotonic `Deadline`, a
  `CancellationReason` and an explicit `IrreversibleBoundary`.
- Cancellation reasons are typed: `UserRequested`,
  `HostShutdown`, `Timeout`, `Superseded`, `SafetyAbort`.
- Cancellation before an irreversible boundary returns
  `Cancelled`. Cancellation after an irreversible boundary
  (process created, old runtime stopped, durable commit
  partially performed) returns `RollbackRequired` /
  `RecoveryRequired`, not an ordinary `Cancelled`.
- Durations are measured through a monotonic `TimeProvider`
  (`GetTimestamp`, `GetElapsedTime`). Wall-clock time is used
  only for persistence and UI timestamps; `DateTime.Now` is
  not used for duration logic.
- Errors use a single central catalog
  `Z2P.<AREA>.<SUBSYSTEM>.<CONDITION>` with typed descriptor
  fields (`Code`, `Category`, `Severity`,
  `UserMessageResourceKey`, `TechnicalDetail`, `CorrelationId`,
  `RecoveryAction`, `Retryability`, `IsSecurityRelevant`).
- Expected failures use `Result<T>`. Unexpected exceptions are
  logged with a correlation ID and are not shown to the user as
  raw `Exception.Message`; a state-integrity-unknown
  exception moves the affected subsystem to a controlled
  `Faulted` / `RecoveryRequired` state instead of continuing
  blind.
- Every mutating Application operation has an idempotency /
  operation identity: a repeat request with the same
  `OperationId` returns the persisted / in-flight outcome and
  does not start a second process, transaction or activation.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 10, §7.3, §7.4, §7.5, §27 Scope G, §28
Scope I.

Consequence:

- `0.0.24` introduces the `RuntimeKernelLoop` operation / generation /
  irreversible-boundary semantics and the typed error catalog
  foundation. `0.0.25` extends the catalog across Application
  use cases.
- The architecture / observable contracts above are added to the
  global exception policy and to the
  `ApplicationOperationCoordinator` (v6 §7.2) without
  per-feature exception.

## DEC-0047 — Runtime update uses explicit leases

Date: 2026-07-04

Decision:

- Runtime bundles, runtime workspaces, downloaded artifacts and
  app payloads are subject to explicit typed leases
  (`RuntimeBundleLease`, `RuntimeWorkspaceLease`,
  `DownloadedArtifactLease`, `AppPayloadLease`) carrying
  `LeaseId`, `BundleId` / payload id, `LeaseKind`,
  `OwnerOperationId`, `AcquiredAtUtc` and
  `ExpiresAtUtc` / explicit release.
- Lease kinds: `RunningRuntime`, `CandidateValidation`,
  `Activation`, `Rollback`, `DiagnosticsExport`, `Recovery`.
- The bundle / app / workspace garbage collector cannot delete
  an artifact that still holds an active lease.
- `Current` / `PreviousKnownGood` / `Candidate` / `Probation`
  bundles cannot be deleted while they are in those roles or
  while they are referenced by an active runtime process,
  activation, rollback, recovery or diagnostics lease.
- Runtime / activation / recovery leases are durable; a short
  diagnostics lease may be in-memory if cleanup is safe after
  crash. Startup recovery removes stale leases only after
  reconciliation.
- Deletion is a state machine
  (`Retired → Deleting → Deleted`) with recheck, handle close
  and absence verification; crash during deletion is reconciled
  at startup. "Best effort directory delete" outside the
  lifecycle state machine is forbidden.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 11, §11.7, §11.8, §12.16, §12.30, §41,
§44.

Consequence:

- `0.0.40` introduces the typed lease model and the
  garbage-collection protocol. `0.0.41` activates it across
  download, extraction, activation, rollback and recovery.
- The pre-`v6` "delete the old bundle once the new one is
  unpacked" / "delete the staging folder on cancel" heuristics
  are removed from the codebase and replaced by the explicit
  lease / lifecycle machine.

## DEC-0048 — TUF client is a separate security-critical subsystem

Date: 2026-07-04

Decision:

- Runtime update trust protocol is the official TUF
  specification `1.0.x` (current at v6 baseline: `1.0.34`)
  through a documented Z2P Runtime Repository POUF. "Catalog
  JSON + one signature" is not an acceptable substitute.
- The Z2P POUF is recorded in
  `docs/Z2P-RUNTIME-UPDATE-POUF.md` and explicitly defines the
  supported specification version, metadata format /
  canonicalization, accepted key types and curves, role
  thresholds, consistent-snapshot naming, maximum metadata
  sizes, maximum delegation depth, root rotation procedure,
  trusted-time / high-water behavior, error handling and
  repository / channel separation.
- Stable client trusts only the stable repository root. The
  development build embeds a separate development root and
  cannot publish itself as Stable. Stable cannot select
  Development root via setting, environment variable or CLI.
- A .NET TUF client is implemented either as a maintained,
  security-reviewed third-party .NET client evaluated against
  the documented requirements or as a narrow Z2P-owned client
  that follows the official detailed TUF client workflow
  exactly for the documented POUF.
- A custom client uses `System.Text.Json` source-generated DTOs
  for bounded parsing, platform cryptography with fixed
  accepted algorithms (`ECDsa`, ECDSA P-256), high-water
  metadata persistence, threshold signature and length / hash
  verification, sequential root rotation, and is exercised
  against the official `python-tuf` test repository and the
  rollback / freeze / mix-and-match / fast-forward /
  malformed-metadata corpus. A custom client receives a
  focused independent security review before Stable.
- Release infrastructure may use the current pinned official
  `python-tuf` reference tooling wrapped in a small Z2P-owned
  layer; tooling upgrades are treated as a security-sensitive
  change. User machines do not require Python.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §0.3
critical correction 12, §12, §43, §44, §24.12.

Consequence:

- `0.0.40` ships the POUF document, the .NET TUF client and
  the metadata check flow (no target download / activation in
  this milestone; only catalog trust and compatibility
  resolver). `0.0.41` adds target download, activation and
  rollback.
- The TUF client, key operations, repository operations and
  update extraction / activation are listed in v6 §46 as
  separate security review sign-offs for the release
  candidate. Independent review of the canonicalization layer
  is a Stable release gate.

## DEC-0049 — ApplicationOperationCoordinator for use-case conflicts

Date: 2026-07-04

Decision:

- An `ApplicationOperationCoordinator` is the single component
  that resolves Application-level conflicts between competing
  use cases. It is not a second Runtime Kernel and does not
  own process state.
- The coordinator classifies a requested use case as
  `Allowed`, `Rejected`, `Queued`, `RequiresConfirmation` or
  `CancelsExisting`, and issues a bounded
  `ApplicationOperationLease`
  (`OperationId`, `OperationKind`, `AcquiredAtUtc`,
  `Deadline`).
- Baseline conflict matrix (v6 §7.2):
  `ApplyProfile` conflicts with `AutoDoctor`;
  `RuntimeBundleActivation` conflicts with
  `Running` / `Apply` / `Doctor`;
  `DiagnosticsExport`, `ProfileImport` and
  `KeyServicesCheck` are allowed while Running;
  `DataCleanup` is blocked during export / runtime / update.
- The coordinator works together with the Runtime Kernel
  command types and the Update state machine. It never owns
  process / job / mutex handles; those remain the Runtime
  Kernel's responsibility.

v6 reference: `docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md` §7.2,
§12.16, §27 Scope J, §35.

Consequence:

- The coordinator is introduced in `0.0.24` together with
  `RuntimeKernelLoop`. Its lease is the bridge between
  Application use cases (start / stop / apply / doctor /
  diagnostics / data) and the runtime / update / data
  lifecycle state machines.
- Per the v6 §0.4 ecosystem optimization, the coordinator
  enforces a single automation owner: `User`, `AutoDoctor`,
  `Autopilot`, `Recovery` or `None` / `RuntimeInternalExperimental`
  (Stable excludes the last). Overlapping automation intent
  is rejected.

