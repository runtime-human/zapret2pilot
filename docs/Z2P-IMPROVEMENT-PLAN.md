# Z2P Improvement Plan

**Date:** 2026-06-24
**Scope:** Zapret2Pilot (Z2P) repository — `C:\Reposit\zapret2pilot`
**Current VERSION:** `0.0.17`
**Active milestone:** 0.0.17 — *First Guarded Runtime Launch* (`RuntimeProcessHost`)
**Document kind:** Synthesis of scout, oracle, and orchestrator verification findings.
**Status:** Living document. Update after every milestone close-out.

---

## 1. Executive Summary

- Z2P is a single-process, privileged Windows desktop control plane for the upstream `winws2` engine. The architecture is well-scoped; the active milestone (0.0.17) is the first end-to-end guarded runtime launch.
- Baseline `dotnet build Zapret2Pilot.slnx -c Release` is **green (0 warnings, 0 errors)**, but a static antipattern scan of `RuntimeProcessHost.cs` surfaces **22 empty `catch` blocks** plus a critical thread-mismatch hazard in `RuntimeOwnershipLease.Dispose`. These are P0 because they will silently corrupt the first real `winws2` launch.
- The Generic Host is wired with only `MainWindow` and `MainWindowViewModel`; storage, repositories, and all six planned `IHostedService` instances are not yet registered. P1 work unblocks the next two milestones and the transition to production-grade supervision.
- Quality-of-life debt (hardcoded version, duplicated PRAGMAs, dead code, redundant `using System;`) is small but should be paid down before the API surface of `RuntimeProcessHost` stabilizes, since cleanup later will be more expensive.
- Future P3 work — `CrashLoopGuard`, `GlobalExceptionHandler`, `RuntimeHealthMonitor`, `NetworkChangeMonitor`, SQLite connection pooling — is aligned with the Critical Review backlog and is explicitly **out of scope** for the current 0.0.17 milestone.

---

## 2. Current State

| Aspect | Value |
| --- | --- |
| VERSION file | `0.0.17` |
| Active milestone | 0.0.17 *First Guarded Runtime Launch* |
| Headline deliverable | `RuntimeProcessHost` (added in 0.0.17) |
| Build baseline | `dotnet build Zapret2Pilot.slnx -c Release` — succeeds, 0 warnings, 0 errors |
| Static antipattern scan | `cwm-roslyn detect_antipatterns` — critical findings in `RuntimeProcessHost.cs` (AP007, empty catch) |
| Architecture canon | `docs/Z2P-CANON.md`, `docs/Z2P-ARCHITECTURE.md`, `docs/Z2P-ROADMAP.md`, `docs/Z2P-DECISION-LOG.md`, `docs/Z2P-CRITICAL-REVIEW.md` |
| Generic Host usage | Limited — only `MainWindowViewModel` and `MainWindow` registered in `Program.cs` |
| IHostedService implementations | 0 of 6 planned (`RuntimeSupervisorHostedService`, `RuntimeHealthMonitorHostedService`, `RuntimeRecoveryHostedService`, `EventJournalHostedService`, `NetworkChangeMonitorHostedService`, `StorageRetentionHostedService`) — per `Z2P-ARCHITECTURE.md` §4 |
| Storage wiring | Not registered in DI; `SqliteConnectionFactory`, `SqliteDbInitializer`, repositories unreachable at runtime |
| UAC manifest | Absent — relies on external elevation mechanism |

### Current milestone deliverables (already committed) — 0.0.17 baseline state
The 0.0.17 baseline is the set of artifacts that produced the current `VERSION = 0.0.17` and that this plan inherits as the starting point. They are listed here for context only; nothing in this plan touches them, and they are expected to be present in the working tree at HEAD.
- Modified: `README.md`, `VERSION`, `docs/Z2P-IMPLEMENTATION-STATUS.md`, `docs/Z2P-ROADMAP.md`
- Added: `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`
- Added tests: `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeReadinessCheckerTests.cs`, `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostResultTests.cs`, `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`, `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessStartContextTests.cs`
- Modified: `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj`
- Developer-local (untracked, not for commit): `NuGet.config.local`

---

## 3. Methodology

1. **Scout pass** — read architecture canon (`Z2P-ARCHITECTURE.md`, `Z2P-ROADMAP.md`, `Z2P-CRITICAL-REVIEW.md`), inspect the active work plan (`.opencode/z2p_agent_docs/Z2P_WORK_PLAN.md`), and inventory relevant files/symbols.
2. **Static analysis** — ran `cwm-roslyn-navigator detect_antipatterns` (severity = warning) on the solution. Confirmed critical findings in `RuntimeProcessHost.cs` (AP007 empty-catch; AP005 broad `catch (Exception)` at the same sites; an internal `GetAwaiter().GetResult()` at line 238 that is intentional for mutex thread affinity) and the `Program.cs` entry point (AP002 sync-over-async on `host.StartAsync().GetAwaiter().GetResult()` / `host.StopAsync(...).GetAwaiter().GetResult()`).
3. **Source verification** — read each file referenced by the antipattern detector and confirmed line numbers, exception-filter usage, and absence of `ILogger<T>` injection in `RuntimeProcessHost`.
4. **Build verification** — `dotnet build Zapret2Pilot.slnx -c Release` from the repository root, 0 warnings, 0 errors. Recorded as the safe baseline against which subsequent patches must not regress.
5. **Oracle consultation** — escalated structural concerns (UAC manifest, single-instance, ownership lease semantics) to oracle for verification against Microsoft documentation on Job Objects, `Mutex` ownership, and side-by-side manifest requirements.
6. **Synthesis** — converted the verified findings into a single backlog table, grouped by priority, and mapped each item to a roadmap milestone.

No tests were executed during this synthesis pass; this is a documentation-only change. Tests will be exercised in the corresponding implementation packages.

---

## 4. Improvement Backlog

### Priority legend
- **P0** — blocks first real `winws2` launch. Must ship before 0.0.17 close-out.
- **P1** — next two milestones (0.0.18, 0.0.19).
- **P2** — quality of life; bundle with the first P0/P1 PR that touches the file.
- **P3** — future; tracked but not scheduled.

| Pri | Item | Evidence | Impact | Recommendation | Verification | Roadmap alignment |
| --- | --- | --- | --- | --- | --- | --- |
| **P0** | 1. `RuntimeProcessHost` empty catch blocks | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` — 22 empty `catch { }` / `catch (Exception) { }` blocks at lines ≈ 445, 446, 567, 583, 598, 612, 632, 682, 692, 703, 737, 741, 750, 754, 763, 795, 838, 850, 860, 896, 908, 923 (AP007). The same file is also flagged by the antipattern scan with AP005 (`catch (Exception)` broad-catch) violations; both classes of issue must be remediated together because broad-catch is a precondition for the empty-catch pattern. | Best-effort cleanup paths swallow real failures (Win32 HANDLE close, Job Object detach, safe-handle dispose). Invisible in production. | Inject `ILogger<RuntimeProcessHost>` via constructor; log every best-effort catch at `Debug` (cleanup) or `Warning` (semantic) level. Preserve current control flow. Tighten broad-catch sites to narrow exception types where the contract is known. | Re-run `detect_antipatterns`; assert AP007 and AP005 counts drop to 0; unit tests for cleanup paths assert log emission. | 0.0.17 — *First Guarded Runtime Launch*. |
| **P0** | 2. `RuntimeOwnershipLease.Dispose` silent thread-mismatch | `src/Zapret2Pilot.Runtime/Ownership/RuntimeOwnershipLease.cs` lines 51–54 return silently when `Environment.CurrentManagedThreadId != ownerManagedThreadId`. | The cross-process `Mutex` is never released; the application permanently holds the global lock, blocking every future launch. | **Do not** release the mutex from a different thread — `Mutex.ReleaseMutex` is only valid on the thread that acquired it, and `SafeWaitHandle.Close()` does not safely transfer ownership and can leave the kernel mutex state in a corrupted, unowned condition that silently blocks every future launch. Recommended remediation, in order of preference: (a) **log a critical warning** on thread mismatch with enough context (owner TID, current TID, lease instance id) so the failure is at least visible; (b) **marshal `Dispose` to the owner thread** (e.g. via a captured `SynchronizationContext` or a dedicated owner-thread message pump) and re-check thread affinity at the start of the marshalled call; (c) **restructure ownership** so cross-thread `Dispose` cannot occur — confine the lease to a single owner thread by construction (e.g. lease is always created and disposed inside one well-known `Thread` / `Task` scheduler). Add a regression test that fails the next launch simulation. | Unit test: wrong-thread dispose must log critical and must not corrupt the mutex. Manual smoke test: start, do not join thread, attempt second launch — must succeed after timeout. | 0.0.17. |
| **P0** | 3. Missing UAC manifest | `Zapret2Pilot.App.csproj` declares no `app.manifest`; no `requestedExecutionLevel` in build output. | Z2P requires admin (WinDivert driver load, Job Object creation). Without a self-declared manifest, elevation is delegated to external mechanisms (broken shortcuts, MSI, etc.). | Add `app.manifest` with `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />`. Embed via `<ApplicationManifest>app.manifest</ApplicationManifest>` in the csproj. | Inspect built `z2p.exe` manifest with `mt -nologo -inputresource:z2p.exe;#1` (or `sigcheck -m`). Confirm `requireAdministrator` present. | 0.0.17 (process hosting prerequisites). |
| **P1** | 4. Sync-over-async in `Program.cs` | `src/Zapret2Pilot.App/Program.cs` lines 20 and 31 — `host.StartAsync().GetAwaiter().GetResult()` and `host.StopAsync(...).GetAwaiter().GetResult()`. | Deadlock risk once any `IHostedService` runs real async work. Masked today because no hosted services are registered. | Convert entry point to `static async Task Main(string[] args)`; use `await host.StartAsync()` / `await host.StopAsync(...)`. Avalonia's `AppMain` is already async-capable. | Manual run; add a hosted service that does a non-trivial `Task.Delay` and confirm no deadlock. | 0.0.18 — *Generic Host Expansion*. |
| **P1** | 5. `RuntimeTransactionManager` thread safety | `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionManager.cs` documents "callers must serialize access". | Once multiple `IHostedService` instances or the UI thread call into the manager concurrently, the journal and rollback paths corrupt. | Wrap public methods with `SemaphoreSlim(1,1)` (preferred for async) or a `lock` block (sync-only). Document the contract change. | Unit tests with `Parallel.For` hammering the public surface; assert journal ordering and rollback integrity. | 0.0.18. |
| **P1** | 6. Storage not wired into App DI | `Program.cs` registers only `MainWindowViewModel` and `MainWindow`. | `SqliteConnectionFactory`, `SqliteDbInitializer`, and repositories are unreachable. Any UI that requests a repository throws at runtime. | In `Program.cs` `Host.CreateDefaultBuilder` chain: `AddSingleton<SqliteConnectionFactory>()`, `AddHostedService<SqliteDbInitializer>()`, `AddSingleton<...Repository>()` for each repository. Pass `IHost` to `App` via `IHostBuilder.ConfigureAvaloniaAppBuilder` if needed. | Smoke test: start app, observe `z2p.db` created with all tables; resolve each repository via DI. | 0.0.18. |
| **P1** | 7. First `IHostedService` implementation | `docs/Z2P-ARCHITECTURE.md` enumerates 6 hosted services; none exist. | No runtime supervision lifecycle yet. `RuntimeProcessHost` is a library; nothing in the host calls it. | Implement `RuntimeSupervisorHostedService` that owns `RuntimeProcessHost` lifecycle: `StartAsync` resolves and starts the runtime, `StopAsync` stops it, observes the runtime's `IObservable<RuntimeEvent>` and forwards to the event journal. | Integration test using a fake `RuntimeProcessHost`; assert lifecycle ordering. | 0.0.18. |
| **P1** | 8. Tests for `RuntimeOwnershipLease` | `tests/Zapret2Pilot.Runtime.Tests` has no `RuntimeOwnershipLeaseTests`. | The thread-mismatch hazard (item 2) is untested. | Add tests for: lease acquisition, lease expiry, wrong-thread dispose (must log + not corrupt), owner-thread dispose (releases mutex). | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter FullyQualifiedName~OwnershipLease` | 0.0.18. |
| **P2** | 9. Hardcoded app version | `MainWindowViewModel.AppVersion = "v0.0.4"` while `VERSION` is `0.0.17`. | UI lies to the user; telemetry and support bundles report the wrong version. | Read `Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion` at startup; expose via `IVersionProvider` registered in DI. | UI snapshot test asserts version reflects `VERSION` file. | 0.0.19. |
| **P2** | 10. PRAGMA duplication | `SqliteConnectionFactory.ConfigureConnection()` and `SqliteDbInitializer` both set the same 4 PRAGMAs (`journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`, `busy_timeout=5000`). | Drift risk: a tuning change in one place silently misses the other. | Centralize in `SqliteConnectionFactory` only. `SqliteDbInitializer` calls into the factory for the schema migration, not raw PRAGMAs. | Unit test asserts only the factory issues PRAGMAs. | 0.0.19. |
| **P2** | 11. Dead code | `ZapretPlanCompiler.GetAssemblyInformationalVersion()` and `RuntimeProcessHost.RunSync(Task)` (void overload) flagged as unreferenced. | Binary bloat, maintenance noise. | Remove both. If `GetAssemblyInformationalVersion` is needed for item 9, reuse the version-provider abstraction instead of duplicating. | `cwm-roslyn find_dead_code` confirms removal. Build remains 0/0. | 0.0.19. |
| **P2** | 12. CS8933 redundant `using System;` | ~98 files with `<ImplicitUsings>enable</ImplicitUsings>` still have explicit `using System;` (count from `grep -l '^using System;' -g '*.cs'` across `src/` and `tests/`). | Analyzer noise; obscures the actual surface of each file. | Project-wide sweep: remove `using System;` where implicit. Suppress the warning at project level if a specific case is needed. | `dotnet build` with `-warnaserror` clean. | 0.0.19. |
| **P3** | 13. `CrashLoopGuard` | `docs/Z2P-CRITICAL-REVIEW.md` #14. | Prevents infinite restart cycles on persistent `winws2` failure. | Track in backlog; design after `RuntimeSupervisorHostedService` is stable. | TBD with oracle. | Post-0.0.19. |
| **P3** | 14. `GlobalExceptionHandler` | `docs/Z2P-CRITICAL-REVIEW.md` #15. | Single funnel for unhandled exceptions; writes to the event journal. | Add `IHostedService` that subscribes to `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`. | TBD. | Post-0.0.19. |
| **P3** | 15. `RuntimeHealthMonitor` | Not sourced from a specific Critical Review section; mandated by `Z2P-ARCHITECTURE.md` §4 (`RuntimeHealthMonitorHostedService`). | Detects stalled `winws2` (no log activity, no heartbeat). | Implemented as `IHostedService`; uses log scraping or named-pipe heartbeat (architecture decision pending). | TBD. | Post-0.0.19. |
| **P3** | 16. `NetworkChangeMonitor` | `docs/Z2P-CRITICAL-REVIEW.md` #13. | Re-validates asset reachability and re-runs asset verifier on network change. | `IHostedService` + `NetworkChange.NetworkAddressChanged` from `System.Net.NetworkInformation`. | TBD. | Post-0.0.19. |
| **P3** | 17. SQLite connection pooling | Architecture decision needed. | Event journal throughput under high log volume. | Evaluate `Microsoft.Data.Sqlite` pooling options; default is per-connection. If insufficient, introduce a small pool keyed by connection-string hash. | Benchmark harness. | Post-0.0.19. |

> **Note on `RuntimeProcessHost.cs` self-contained sync-over-async.** Beyond the empty-catch/cleanup work in P0 #1, the same file contains internal sync-over-async that is **currently by design** but must be revisited before concurrent callers arrive:
>
> - `RunSync<T>(Task<T>)` at line 968 and `RunSync(Task)` at line 975 — private static helpers that wrap `task.GetAwaiter().GetResult()`.
> - An additional inline `materializeTask.GetAwaiter().GetResult()` at line 238.
>
> The rationale (per the XML doc on `RunSync`) is mutex thread affinity: the ownership lease binds to `Environment.CurrentManagedThreadId`, and an `await` may resume on a different thread, which would corrupt `RuntimeOwnershipLease.Dispose` cleanup paths. While the start pipeline runs single-threaded this is safe. **Action:** before any concurrent path (e.g. `RuntimeSupervisorHostedService` callbacks — P1 #7) calls into `RuntimeProcessHost.Start`, audit these `RunSync` sites and either (a) constrain the call site to the owner thread, or (b) move to a fully async pipeline and redesign `RuntimeOwnershipLease` to be thread-agnostic. This is **not** P0 today; it is a precondition for P1 #7.

---

## 5. Reference Implementation Notes

- **Upstream zapret2** (`.opencode/zapret2-master`) is Linux-first. The Windows binary it ships is `winws2`, which uses **WinDivert** for packet interception. Z2P does not reimplement packet filtering; it acts as a **Windows desktop control plane** that supervises `winws2` as a child process. This separation is intentional and must be preserved.
- **Asset manifest + SHA-256 verification** is the right approach for `winws2.exe`. The asset verifier (planned, not yet implemented) is the source of truth for binary integrity before each launch.
- **Process containment** must use Windows **Job Objects** with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, configured so that killing the supervisor (`z2p.exe`) deterministically tears down `winws2`. This is a non-negotiable safety property and is the reason `RuntimeProcessHost` is preferred over `Process.Start` + `WaitForExit`.
- **Single-instance + cross-process lock** uses a **Global Mutex** with explicit `ownerManagedThreadId` capture so that ownership transfer between hot-reload and recovery paths is observable. The lease abstraction (`RuntimeOwnershipLease`) is the only sanctioned way to acquire the mutex.
- **Recovery metadata** is persisted via a small file guarded by an `OS-level file lock` (not SQLite), so a crashed instance can be detected and recovered from the next launch.

---

## 6. Risks and Blockers

| Risk | Source | Mitigation |
| --- | --- | --- |
| Silent cleanup failures mask a real `winws2` failure in production. | P0 #1, P0 #2 | Items 1 and 2 must ship together with `RuntimeProcessHost`. |
| First real launch may take down the user's network if `winws2` is misconfigured. | Architectural | P3 `CrashLoopGuard` + `RuntimeHealthMonitor` are mitigation, not optional. Until they exist, do **not** enable auto-restart. |
| Adding a hosted service that does real async work will deadlock the entry point. | P1 #4 | Convert `Main` to `async Task` before registering the first non-trivial hosted service (item 7). |
| `RuntimeTransactionManager` corruption if UI and supervisor call concurrently. | P1 #5 | Either item 5 ships before any UI binds to the journal, or UI binding is deferred. |
| Hidden version mismatch between `VERSION` file, assembly metadata, and UI. | P2 #9 | Single source of truth (`AssemblyInformationalVersion`) before 0.0.19. |
| Future net-new `winws2` features (per-site filter, per-URL routing) tempting scope creep. | Z2P-CANON §5.1 | Per-URL routing is explicitly forbidden by the canon. Any such request must be re-routed through the canon update process. |

---

## 7. Next Safe Tasks (1–3 starter tasks)

These are the minimum work items that unblock the rest of the backlog. Each is a single PR-sized change.

1. **Logging in `RuntimeProcessHost` (P0 #1)**
   - Inject `ILogger<RuntimeProcessHost>`; replace each empty `catch` with a structured log call.
   - Add or extend `RuntimeProcessHostTests` to assert that cleanup paths emit the expected log levels.
   - Files: `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`, `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`.
   - Verification: `dotnet build -c Release` (0/0); `dotnet test .../Runtime.Tests -c Release --filter FullyQualifiedName~RuntimeProcessHost`; `detect_antipatterns` AP007 and AP005 counts = 0.

2. **`RuntimeOwnershipLease` thread-mismatch fix + tests (P0 #2 + P1 #8)**
   - Log critical on wrong-thread `Dispose`; **do not** call `Mutex.SafeWaitHandle.Close()` to release the mutex from another thread — this is unsafe and can leave the kernel mutex in a corrupted state. Marshal `Dispose` to the owner thread (preferred) or restructure ownership so cross-thread `Dispose` is impossible.
   - Add `RuntimeOwnershipLeaseTests` covering: acquisition, expiry, wrong-thread dispose (must log critical and must not release the mutex), owner-thread dispose (must release the mutex).
   - Files: `src/Zapret2Pilot.Runtime/Ownership/RuntimeOwnershipLease.cs`, `tests/Zapret2Pilot.Runtime.Tests/Ownership/RuntimeOwnershipLeaseTests.cs` (new).
   - Verification: tests green; manual second-launch smoke test succeeds after first instance aborts.

3. **UAC manifest (P0 #3)**
   - Add `src/Zapret2Pilot.App/app.manifest` with `requireAdministrator`.
   - Update `Zapret2Pilot.App.csproj` with `<ApplicationManifest>app.manifest</ApplicationManifest>`.
   - Verification: build succeeds; `mt -nologo -inputresource:z2p.exe;#1` shows `requireAdministrator`.

> Do **not** batch all three into one PR. Land them in the order above; each is independently testable and reversible.

---

## 8. Reviewer Focus

- Confirm the empty-catch fix does not change control flow (no swallowed exceptions should now be re-thrown silently — verify by reading the diff, not by description).
- Confirm `RuntimeOwnershipLease` does not introduce a use-after-release on the `Mutex` handle. Pay special attention to any change that touches `SafeWaitHandle`.
- Confirm the UAC manifest change does not break the build on developer machines without admin (manifest presence does not require admin to build; only to run).
- Cross-check that no item expands scope beyond what is stated. In particular, the P0 batch must **not** introduce a `CrashLoopGuard` or `GlobalExceptionHandler` — those are P3 and must remain separate.
