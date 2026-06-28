# Zapret2Pilot / Z2P — Detailed Roadmap

**Date:** 2026-06-25
**Status:** Active working document
**Scope:** Unified, evidence-based task roadmap integrating two parallel AI reviews for Zapret2Pilot, version `0.0.17` (First Guarded Runtime Launch).
**Audience:** Z2P project owner, brain (planner), and coding agents (scout / coder / reviewer / oracle).

---

## 1. Current state

| Item | Value |
|------|-------|
| Current VERSION | `0.0.17` |
| Active milestone | `0.0.17` — First Guarded Runtime Launch |
| Stack | C# / .NET 10 / Avalonia UI / ReactiveUI |
| Architecture | Privileged single-process Windows desktop application. No Windows Service. |
| Runtime Kernel | Inside `z2p.exe` |
| Baseline build | `dotnet build Zapret2Pilot.slnx -c Release` succeeds, **0 warnings, 0 errors** |
| Documentation canon | `docs/Z2P-CANON.md`, `docs/Z2P-ARCHITECTURE.md`, `docs/Z2P-ROADMAP.md`, `docs/Z2P-DECISION-LOG.md`, `docs/Z2P-CRITICAL-REVIEW.md` |
| Repository | `MrFr3di/zapret2pilot` |
| Solution file | `Zapret2Pilot.slnx` |

### What this document is

A unified, evidence-anchored list of **concrete tasks** required to reach a safe guarded launch of `winws2` from the Runtime Kernel. Each task is referenced to:

- a specific file and line range,
- a scout verification result,
- an architectural canon principle,
- a test or build command.

Tasks are grouped by **priority** and **layer** (Runtime Kernel safety, dependency ingestion, documentation/process, starter implementation packets). They are **not** a substitute for the existing `Z2P-ROADMAP.md`; this document only deepens the next safe steps with verifiable evidence.

### What this document is not

- It is **not** a green light to start `winws2` against real traffic.
- It is **not** an invitation to expand scope. Each task is bounded.
- It does **not** override the existing `Z2P-CANON.md` or `Z2P-DECISION-LOG.md`.

---

## 2. Methodology

Two independent AI reviews were executed in parallel. Each review used the `cwm-roslyn-navigator` tools and additional read-only scout passes. The reviews were cross-checked against:

- `detect_antipatterns` for empty catches, broad catches, sync-over-async, and missing `CancellationToken`.
- `find_callers` / `find_references` for impact analysis.
- `get_diagnostics` for the Release build.
- Manual reading of the `Runtime Kernel` subsystem.

### Scout verification groups and status

| Group | Scope | Tooling | Status |
|-------|-------|---------|--------|
| Scout I | Verified executable integrity gap | `cwm-roslyn-navigator_find_symbol` / `find_references` | Verified |
| Scout II | Redirected stdout/stderr, StopAsync rollback | `cwm-roslyn-navigator_detect_antipatterns` | Verified |
| Scout III | UI thread safety, RuntimeKernelWorker absence | `cwm-roslyn-navigator_get_dependency_graph` | Verified |
| Scout IV | ArgumentList, graceful stop | `cwm-roslyn-navigator_find_references` | Verified |
| Scout A | Analyzers: Meziantou + Roslynator | `cwm-roslyn-navigator_get_project_graph` + repo inspection | Verified |
| Scout B | CsWin32 project extraction | `cwm-roslyn-navigator_get_project_graph` | Verified |
| Scout C | Serilog ingestion plan | `cwm-roslyn-navigator_find_symbol` | Verified |
| Scout D | Verify.XunitV3 + AwesomeAssertions | repo inspection | Verified |
| Scout E | DynamicData | `cwm-roslyn-navigator_find_references` | Verified (defer to P2) |
| Scout F | Microsoft.Extensions.Http.Resilience | `cwm-roslyn-navigator_get_project_graph` | Verified (defer) |
| Scout G | Nerdbank.GitVersioning, BenchmarkDotNet, System.CommandLine, OpenTelemetry, Velopack, SmartPipe.Core | `cwm-roslyn-navigator_get_project_graph` | Verified |
| Scout H | Storage + forbidden dependencies | `cwm-roslyn-navigator_get_project_graph` | Verified |

**Convention:** *Verified* = a scout subagent produced reproducible evidence (file path, line number, snippet) that another scout re-checked. Cross-cited IDs (e.g., `P0-1` ↔ `AP007`) are anchored in the source.

---

# Part 1 — Runtime Kernel Safety Hardening (P0 / P1)

These tasks target the `Zapret2Pilot.Runtime` project. They exist because the Runtime Kernel must not launch a real `winws2` binary until every safety primitive listed below is in place. The hard line: **no real `winws2` launch before P0 tasks are all closed and verified**.

## P0 — Blockers before any real `winws2` launch

### P0-1 — RuntimeProcessHost empty / broad catch blocks

| Field | Value |
|-------|-------|
| ID | P0-1 |
| Priority | **P0** |
| Title | Remove empty and broad `catch` blocks in `RuntimeProcessHost` |
| Problem | 22 catch sites in `RuntimeProcessHost.cs` in total: 18 are empty `catch { }` blocks that swallow exceptions silently, and 4 are specific exception filters (`InvalidOperationException`, `Win32Exception`) in cleanup paths that still should log. Best-effort cleanup paths turn into silent corruption when ownership leases, mutex handles, or job objects misbehave. This is exactly the kind of fault-tolerance gap that hides real kernel bugs. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` — empty / broad catches at approximately lines 445, 446, 567, 583, 598, 612, 632, 682, 692, 703, 737, 741, 750, 754, 763, 795, 838, 850, 860, 896, 908, 923. Detected by `cwm-roslyn-navigator_detect_antipatterns` as `AP005` (broad catch) and `AP007` (empty catch). |
| Scout verification | Scout II — `detect_antipatterns` re-run, all 22 sites reproduced. |
| Recommendation | 1. Inject `ILogger<RuntimeProcessHost>` (depends on Serilog, see P1 in Part 2). 2. Replace every empty `catch { }` with a logged best-effort `catch (Exception ex)` that records severity, exception, and operation. 3. Tighten broad catches to specific expected exception types where possible; for truly defensive cleanup, log a `Warning` and continue. 4. For ownership / mutex / handle cleanup, log `Error` with the `SafeHandle` and ownership state. |
| Files to touch | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`, `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`. |
| Tests to add | Cleanup-path log assertions: assert logger receives a structured event for each catch site, including operation name, severity, and exception. Add a fake-runtime failure case that triggers a known exception in `Stop` and asserts the log content rather than a swallowed void. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeProcessHostTests"` then `dotnet build Zapret2Pilot.slnx -c Release` (expect 0/0). |
| Roadmap alignment | Directly enables the `0.0.17` “First Guarded Runtime Launch” milestone by making kernel cleanup observable. Required for `0.0.18`. |

### P0-2 — `RuntimeOwnershipLease.Dispose` silent thread mismatch

| Field | Value |
|-------|-------|
| ID | P0-2 |
| Priority | **P0** |
| Title | Detect and log wrong-thread `Dispose` in `RuntimeOwnershipLease` |
| Problem | The current `Dispose` path assumes the caller is the owner thread. A dispose from another thread can corrupt the `Mutex`’s `SafeWaitHandle` if it is closed from a non-owner thread. The class neither detects nor reports this case. |
| Evidence | `src/Zapret2Pilot.Runtime/Ownership/RuntimeOwnershipLease.cs` lines 51–54 (`Dispose` path). Detected by prior review and Scout III. |
| Scout verification | Scout III — `get_dependency_graph` and `find_callers` confirmed no protection around cross-thread dispose. |
| Recommendation | 1. Capture the thread that created the lease. 2. In `Dispose`, if the current managed thread differs, log a `Critical` event and **do not** call `SafeWaitHandle.Close()` from the foreign thread. 3. Marshal the actual close to the owner thread via the runtime kernel worker (P0-7). 4. Never silently recover. |
| Files to touch | `src/Zapret2Pilot.Runtime/Ownership/RuntimeOwnershipLease.cs`, new `tests/Zapret2Pilot.Runtime.Tests/Ownership/RuntimeOwnershipLeaseTests.cs`. |
| Tests to add | Wrong-thread dispose logs critical and does not corrupt the mutex. Owner-thread dispose still works. Lease expiry (timeout) does not double-dispose. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeOwnershipLeaseTests"`. |
| Roadmap alignment | Required for any privileged runtime. Listed in canon: ownership must be single-threaded. |

### P0-3 — Add UAC manifest (`requireAdministrator`)

| Field | Value |
|-------|-------|
| ID | P0-3 |
| Priority | **P0** |
| Title | Add `app.manifest` to `Zapret2Pilot.App` requesting `requireAdministrator` |
| Problem | The Z2P app must run elevated to manipulate Job Objects, raw sockets, and `winws2`. The repo has no `app.manifest` for `Zapret2Pilot.App`, so the app is launched at the integrity level of Explorer by default. |
| Evidence | No `app.manifest` file present in `src/Zapret2Pilot.App/`. Prior review and Scout III confirmed the gap. |
| Scout verification | Scout III — `glob` of `src/Zapret2Pilot.App/*.manifest` returned no result. |
| Recommendation | 1. Create `src/Zapret2Pilot.App/app.manifest` with `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />`. 2. Reference it in `Zapret2Pilot.App.csproj` via `<ApplicationManifest>app.manifest</ApplicationManifest>`. 3. Verify with `mt.exe` or build output that the elevation level is embedded. 4. Document the UAC prompt in the README. |
| Files to touch | `src/Zapret2Pilot.App/app.manifest` (new), `src/Zapret2Pilot.App/Zapret2Pilot.App.csproj`. |
| Tests to add | Build-time manifest inspection. Optional unit test using `Assembly.GetManifestResourceStream` to confirm the manifest is embedded. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release` and inspect `bin/Release/.../Zapret2Pilot.App.exe.manifest` (or `mt.exe -inputresource:`). |
| Roadmap alignment | Hard prerequisite for any real runtime launch. |

### P0-4 — Verified executable integrity gap

| Field | Value |
|-------|-------|
| ID | P0-4 |
| Priority | **P0** |
| Title | Bind `RuntimeProcessHost` to `ZapretAssetVerifier` output |
| Problem | `RuntimeProcessStartContext` accepts an arbitrary `RuntimeExecutablePath` string. The host launches whatever path the caller passes. The `ZapretAssetVerifier` result is **not** threaded into the launch decision. A malicious or corrupted path could be executed under the elevated token. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessStartContext.cs` line 59 accepts `RuntimeExecutablePath` directly. `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` lines 285, 290–292 launch that path with no verification binding. `ZapretAssetVerificationSummary` and `RuntimeWorkspaceMaterializeResult` are produced by the workspace materializer but are not consumed by the host. Detected by Scout I. |
| Scout verification | Scout I — `find_symbol("RuntimeProcessStartContext")` and `find_callers("StartAsync")` confirmed no verifier coupling. |
| Recommendation | 1. Introduce a new value object `VerifiedRuntimeExecutablePath` that can only be constructed from a passing `ZapretAssetVerificationSummary`. 2. Make `RuntimeProcessStartContext` accept `VerifiedRuntimeExecutablePath` (or equivalent opaque type) instead of a raw string. 3. The workspace materializer is the only place that produces this type. 4. The host launches only what it receives. 5. Provide explicit failure messages when the verification is missing or expired. |
| Files to touch | New `src/Zapret2Pilot.Runtime/Integrity/VerifiedRuntimeExecutablePath.cs`; modify `RuntimeProcessStartContext.cs`, `ZapretAssetVerificationSummary.cs`, `RuntimeWorkspaceMaterializeResult.cs`, `RuntimeProcessHost.cs`; new tests. |
| Tests to add | Host rejects unverified executable. Launched path matches the manifest. Expired verification summary cannot construct `VerifiedRuntimeExecutablePath`. FakeRuntime path remains launchable. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` and `dotnet build Zapret2Pilot.slnx -c Release` (expect 0/0). |
| Roadmap alignment | Direct child of `0.0.17`. Required to be green before any real `winws2` launch. |

### P0-5 — Redirected stdout / stderr never consumed

| Field | Value |
|-------|-------|
| ID | P0-5 |
| Priority | **P0** |
| Title | Disable stdout/stderr redirection in `RuntimeProcessHost` for `0.0.18` |
| Problem | `ProcessStartInfo.RedirectStandardOutput` and `RedirectStandardError` are set to `true`, but the host never subscribes to `OutputDataReceived` / `ErrorDataReceived`. With default buffer sizes, a verbose `winws2` can deadlock on a full pipe. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` sets `RedirectStandardOutput = true` and `RedirectStandardError = true` near lines 290–299. No event handlers are registered. Detected by Scout II. |
| Scout verification | Scout II — `grep` for `OutputDataReceived` and `ErrorDataReceived` returned no usages in the runtime. |
| Recommendation | For `0.0.18`, set both redirects to `false`. Wire output capture as a separate, bounded pump in a later version (e.g., `0.0.19`), with a fixed-size ring buffer, an explicit consumer thread, and a backpressure contract. |
| Files to touch | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`, `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs`. |
| Tests to add | A fake-runtime that emits a configurable volume of stdout/stderr must not deadlock within 30s. Assert the process is reported as `Running` and `Exited` cleanly. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeProcessHostTests"`. |
| Roadmap alignment | Required for `0.0.18`. Sets the stage for the bounded-pump design in `0.0.19`. |

### P0-6 — `StopAsync` rollback after irreversible kill

| Field | Value |
|-------|-------|
| ID | P0-6 |
| Priority | **P0** |
| Title | Do not roll back to `Running` after process is killed in `StopAsync` |
| Problem | `RuntimeProcessHost.StopAsync` kills the process and then may call `Rollback` on the transaction, which restores `isRunning = true` in `RuntimeTransactionManager`. A subsequent restart finds the kernel thinking it is still running, and invariants collapse. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` — kill path followed by `Rollback`. `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionManager.cs` — `Rollback` re-applies the previous state. Detected by Scout II. |
| Scout verification | Scout II — `find_callers("Rollback")` and `find_callers("Kill")` reproduced the sequence. |
| Recommendation | 1. After an irreversible kill, do **not** rollback the transaction. 2. Report a `Cleanup` warning with the precise failure. 3. Clear host state (`IsRunning = false`, `OwnedHandles = null`). 4. The transaction manager must remain in `Stopped` / `Failed` rather than `Running`. |
| Files to touch | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`, `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionManager.cs`, new tests. |
| Tests to add | After kill, transaction manager state is `Stopped` even if a follow-up lock delete fails. A second `StartAsync` is allowed. A `Rollback` from a kill path is not invoked. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeTransactionManagerTests"` and the host tests. |
| Roadmap alignment | Required for `0.0.18`. Closes a known review finding. |

### P0-7 — UI thread safety and a dedicated `RuntimeKernelWorker`

| Field | Value |
|-------|-------|
| ID | P0-7 |
| Priority | **P0** |
| Title | Introduce a single dedicated runtime thread and a `RuntimeKernelWorker` |
| Problem | The host uses `GetAwaiter().GetResult()` for mutex thread affinity. There is no dedicated runtime thread. `ImmediateUiScheduler` is synchronous. UI code can therefore block on kernel awaits. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` (mutex thread affinity). `src/Zapret2Pilot.App/Program.cs` UI bootstrap. Detected by Scout III. |
| Scout verification | Scout III — `get_dependency_graph("StartAsync")` and `get_dependency_graph("StopAsync")` show sync-over-async paths reaching into UI. |
| Recommendation | 1. Add a `RuntimeKernelWorker` — a single dedicated `Thread` (or `LongRunning` task) that owns all runtime kernel state. 2. Expose an enqueue API: `Enqueue(Func<CancellationToken, Task>)` and `Enqueue<T>(Func<CancellationToken, Task<T>>)`. 3. UI and `Application` layer enqueue asynchronously; the host never blocks. 4. Register the worker as an `IHostedService` and start/stop it via the Generic Host lifecycle. |
| Files to touch | New `src/Zapret2Pilot.Runtime/Hosting/RuntimeKernelWorker.cs`; modify `RuntimeProcessHost.cs`; `src/Zapret2Pilot.App/Program.cs` (register hosted service). |
| Tests to add | Host commands execute on the dedicated thread (assert `Thread.CurrentThread.ManagedThreadId`). UI thread is not blocked for more than 1ms during a kernel call. Cancellation is honored. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` and `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | Required before wiring UI to runtime in `0.0.19`. |

## P1 — Hardening before UI ↔ runtime wiring

### P1-1 — `RuntimeProcessStartContext` doc promise validation

| Field | Value |
|-------|-------|
| ID | P1-1 |
| Priority | **P1** |
| Title | Make `RuntimeProcessStartContext` validations match the XML doc |
| Problem | The XML doc states the host will validate the path on disk, but neither the constructor nor the host currently performs this validation. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessStartContext.cs` (constructor XML doc). Detected by Scout III. |
| Scout verification | Scout III — manual diff between doc and code. |
| Recommendation | Either implement the validation (`File.Exists`, allowed root, verified path binding — see P0-4) **or** fix the doc comment to match current behavior. Prefer implementing. |
| Files to touch | `RuntimeProcessStartContext.cs`, `RuntimeProcessHost.cs`. |
| Tests to add | Constructor rejects non-existent path under allowed root; rejects path outside allowed root. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release`. |
| Roadmap alignment | Tightens the contract that P0-4 also enforces. |

### P1-2 — Use `ProcessStartInfo.ArgumentList`

| Field | Value |
|-------|-------|
| ID | P1-2 |
| Priority | **P1** |
| Title | Pass raw tokens via `ProcessStartInfo.ArgumentList` instead of joining quoted strings |
| Problem | `RuntimeProcessHost.cs` joins pre-quoted tokens into a single `Arguments` string. `WinwsArgumentBuilder` already pre-quotes them. The host double-quotes or, worse, mis-quotes. Native argument parsing depends on the receiving program re-parsing the joined string. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` lines 286–293. `src/Zapret2Pilot.Engine.Zapret2/Compiler/WinwsArgumentBuilder.cs` produces pre-quoted tokens. Detected by Scout IV. |
| Scout verification | Scout IV — `find_references("Arguments")` and `find_references("WinwsArgumentBuilder")`. |
| Recommendation | 1. `WinwsArgumentBuilder.Result.Tokens` must hold raw tokens. 2. `CompiledZapretPlan.Arguments` remains raw. 3. The host uses `startInfo.ArgumentList.Add(token)` for each token. 4. `ArgsContent` (used for `args.txt`) keeps the quoted form. |
| Files to touch | `WinwsArgumentBuilder.cs`, `CompiledZapretPlan.cs`, `RuntimeProcessHost.cs`, `RuntimeProcessHostTests.cs`. |
| Tests to add | Argument list of a known profile produces the expected raw tokens. Round-trip through `ArgumentList` and `ArgsContent` is verified. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Application.Tests -c Release` and runtime tests. |
| Roadmap alignment | Required to fix a long-standing quoting bug. |

### P1-3 — Graceful stop before kill

| Field | Value |
|-------|-------|
| ID | P1-3 |
| Priority | **P1** |
| Title | Send `CTRL_BREAK` / `CTRL_C` to the runtime process before `Kill` |
| Problem | `StopProcess` jumps straight to `Process.Kill(entireProcessTree: true)`. This loses any in-flight cleanup, log flush, or release of sockets that `winws2` might attempt on a normal shutdown. |
| Evidence | `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` line 734. Detected by Scout IV. |
| Scout verification | Scout IV — `find_callers("Kill")` and `find_callers("StopProcess")`. |
| Recommendation | 1. Try `GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, 0)` first, with a bounded wait (e.g., 3 seconds). 2. If the process is still alive, fall back to `Kill(entireProcessTree: true)`. 3. Document the timeout in the API surface. 4. The fallback must remain best-effort and must log a warning. |
| Files to touch | `RuntimeProcessHost.cs`, tests. |
| Tests to add | Fake-runtime exits on `CTRL_BREAK_EVENT` (assert clean exit code, no kill). Fake-runtime that ignores `CTRL_BREAK_EVENT` is killed within the timeout. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~StopProcess"`. |
| Roadmap alignment | Required for clean shutdown semantics in `0.0.18`. |

### P1-4 — `RuntimeTransactionManager` thread safety

| Field | Value |
|-------|-------|
| ID | P1-4 |
| Priority | **P1** |
| Title | Add internal serialization to `RuntimeTransactionManager` |
| Problem | The class documents that callers must serialize access, but concurrent callers (UI thread, runtime worker) are about to appear. |
| Evidence | `src/Zapret2Pilot.Runtime/Transactions/RuntimeTransactionManager.cs` (XML doc and methods). From the prior improvement plan. |
| Scout verification | Re-validated against the current state of the file. |
| Recommendation | Add a `SemaphoreSlim(1, 1)` or `lock` around all mutating methods. Provide an `Async` overload when the caller is on the runtime worker. Keep the public API stable. |
| Files to touch | `RuntimeTransactionManager.cs`, tests. |
| Tests to add | Concurrent transitions from N threads converge to a single state. `Begin` rejects while another transition is in progress. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release`. |
| Roadmap alignment | Required before P0-7 worker is wired. |

### P1-5 — Sync-over-async in `Program.cs`

| Field | Value |
|-------|-------|
| ID | P1-5 |
| Priority | **P1** |
| Title | Convert `Program.Main` to `async Task Main` |
| Problem | `Program.cs` uses `GetAwaiter().GetResult()` on host `Start` and `Stop`. This blocks the UI thread and risks deadlocks. |
| Evidence | `src/Zapret2Pilot.App/Program.cs` lines 20 and 31. From the prior improvement plan. |
| Scout verification | Re-validated. |
| Recommendation | Convert `Main` to `async Task Main`. Use `await host.StartAsync()` and `await host.StopAsync()`. Ensure the Avalonia lifetime remains correct. |
| Files to touch | `Program.cs`. |
| Tests to add | Manual smoke run; build must remain 0/0. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | Required for `0.0.18` and P0-7. |

### P1-6 — Storage not wired into App DI

| Field | Value |
|-------|-------|
| ID | P1-6 |
| Priority | **P1** |
| Title | Register SQLite repositories in the Generic Host |
| Problem | `Program.cs` registers only `MainWindowViewModel` and `MainWindow`. The storage layer (`SqliteConnectionFactory`, `SqliteDbInitializer`, repositories) is not in the container. |
| Evidence | `src/Zapret2Pilot.App/Program.cs`. From the prior improvement plan. |
| Scout verification | Re-validated. |
| Recommendation | Register `SqliteConnectionFactory`, `SqliteDbInitializer`, and the relevant repositories as singletons. Initialize the database in a hosted service. |
| Files to touch | `Program.cs`, storage projects, possibly a new `StorageInitializationHostedService`. |
| Tests to add | Container resolves all required services. Database file is created on first run. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release` and a manual run. |
| Roadmap alignment | Required for `0.0.19` UI screens that need data. |

### P1-7 — First `IHostedService` implementation

| Field | Value |
|-------|-------|
| ID | P1-7 |
| Priority | **P1** |
| Title | Implement `RuntimeSupervisorHostedService` |
| Problem | 0 of 6 planned hosted services exist. The runtime kernel has no lifecycle owner. |
| Evidence | `cwm-roslyn-navigator_get_project_graph` shows no `IHostedService` implementations. From the prior improvement plan. |
| Scout verification | Re-validated. |
| Recommendation | Implement `RuntimeSupervisorHostedService` that owns the runtime kernel lifecycle. Register it in `Program.cs`. Do **not** start `winws2` here yet — keep it as a supervisor scaffold. |
| Files to touch | New `src/Zapret2Pilot.Runtime/Hosting/RuntimeSupervisorHostedService.cs`, `Program.cs`. |
| Tests to add | Host start/stop invokes supervisor start/stop. The supervisor is idempotent. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | Required for `0.0.18`. |

### P1-8 — Tests for `RuntimeOwnershipLease`

| Field | Value |
|-------|-------|
| ID | P1-8 |
| Priority | **P1** |
| Title | Add `RuntimeOwnershipLeaseTests` |
| Problem | There is no test class for `RuntimeOwnershipLease`. Acquisition, expiry, and thread mismatch are untested. |
| Evidence | Missing `tests/Zapret2Pilot.Runtime.Tests/Ownership/RuntimeOwnershipLeaseTests.cs`. From the prior improvement plan. |
| Scout verification | `glob` of `tests/Zapret2Pilot.Runtime.Tests/**/*Ownership*` returns nothing. |
| Recommendation | Add tests covering: (a) successful acquisition, (b) timeout / expiry, (c) wrong-thread dispose (asserts log and does not corrupt mutex — see P0-2), (d) owner-thread dispose. |
| Files to touch | New `RuntimeOwnershipLeaseTests.cs`. |
| Tests to add | As above. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release`. |
| Roadmap alignment | Required for `0.0.18`. |

---

# Part 2 — Dependency Roadmap (P0 / P1 / P2 / P3)

This part lists new dependencies and version bumps. Each item includes rationale, scope, and a clear “no” for risks identified by the canon. The baseline is .NET 10 / Avalonia 12.0.4 / Roslyn 4.x.

## P0/P1 — analyzers and safe framework bumps

### D-A1 — Meziantou.Analyzer (P0/P1)

| Field | Value |
|-------|-------|
| ID | D-A1 |
| Priority | **P0/P1** |
| Title | Add `Meziantou.Analyzer` repo-wide |
| Problem | The repo currently has no third-party analyzers enabled. The canon explicitly lists Meziantou among baseline analyzers. |
| Evidence | `Directory.Packages.props` lacks the package. `.editorconfig` has no phased severity. Scout A. |
| Scout verification | Scout A — `cwm-roslyn-navigator_get_project_graph` and `Directory.Packages.props` inspection. |
| Recommendation | 1. Add `Meziantou.Analyzer` to `Directory.Packages.props` with `PrivateAssets="all"`. 2. Phase the severity: start as `suggestion`, ramp to `warning` after one milestone. 3. Document in `Z2P-DECISION-LOG.md`. |
| Files to touch | `Directory.Packages.props`, `Directory.Build.props`, `.editorconfig`. |
| Tests to add | Build remains 0/0; failing analyzer rules are tracked in the decision log. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P0/P1 chore. |

### D-A2 — Roslynator.Analyzers (P0/P1)

| Field | Value |
|-------|-------|
| ID | D-A2 |
| Priority | **P0/P1** |
| Title | Add `Roslynator.Analyzers` 4.15.0 repo-wide |
| Problem | No Roslynator rules active. Style and code-quality rules are not enforced. |
| Evidence | Same as D-A1. Scout A. |
| Scout verification | Scout A. |
| Recommendation | Add `Roslynator.Analyzers` 4.15.0, `PrivateAssets="all"`. Phase severity. Do not enable formatting rules that conflict with `.editorconfig`. |
| Files to touch | `Directory.Packages.props`, `Directory.Build.props`, `.editorconfig`. |
| Tests to add | Build remains 0/0. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P0/P1 chore. |

### D-A3 — Avalonia 12.0.4 → 12.0.5 (P0/P1)

| Field | Value |
|-------|-------|
| ID | D-A3 |
| Priority | **P0/P1** |
| Title | Bump Avalonia to 12.0.5 |
| Problem | Patch version behind. Fixes are available. |
| Evidence | `Directory.Packages.props` pins 12.0.4. Scout A. |
| Scout verification | Scout A. |
| Recommendation | Bump to 12.0.5. Keep ReactiveUI version aligned. No public API change expected. |
| Files to touch | `Directory.Packages.props`. |
| Tests to add | Build remains 0/0. UI smoke test. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P0/P1 chore PR. |

### D-B1 — Microsoft.Windows.CsWin32 (P0/P1)

| Field | Value |
|-------|-------|
| ID | D-B1 |
| Priority | **P0/P1** |
| Title | Extract Win32 P/Invoke into a new `Zapret2Pilot.Platform.Windows` project via `Microsoft.Windows.CsWin32` |
| Problem | Win32 calls are currently scattered. The canon requires the Core project to stay dependency-free. |
| Evidence | `src/Zapret2Pilot.Runtime/Windows/WindowsJobObjectNativeMethods.cs` and `JobObjectNativeApi.cs`. Scout B. |
| Scout verification | Scout B. |
| Recommendation | 1. Create new `src/Zapret2Pilot.Platform.Windows/Zapret2Pilot.Platform.Windows.csproj`. 2. Add `Microsoft.Windows.CsWin32` and an initial `NativeMethods.txt` with at least: `CreateJobObject`, `SetInformationJobObject`, `AssignProcessToJobObject`, `CloseHandle`. 3. Migrate the existing `WindowsJobObjectNativeMethods.cs` to use the generated source. 4. `JobObjectNativeApi.cs` and `RuntimeJobObject.cs` depend on the new project. 5. Update `Zapret2Pilot.slnx` and `Directory.Packages.props`. 6. Keep `PrivateAssets="all"` for CsWin32. |
| Files to touch | New `Zapret2Pilot.Platform.Windows` project, `NativeMethods.txt`, migrated files, solution, props. |
| Tests to add | Existing `RuntimeJobObject` tests must still pass. Add a smoke test that creates and assigns a real job object in a Windows-only test category. |
| Verification commands | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release` and `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | Required for clean isolation of Win32 surface. |

### D-C1 — Serilog (P0/P1)

| Field | Value |
|-------|-------|
| ID | D-C1 |
| Priority | **P0/P1** |
| Title | Wire Serilog into the Generic Host |
| Problem | The Runtime Kernel lacks structured logging. P0-1, P0-2, P0-6 all require `ILogger` integration. |
| Evidence | No Serilog packages in `Directory.Packages.props`. Scout C. |
| Scout verification | Scout C. |
| Recommendation | 1. Add `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`, `Serilog.Formatting.Compact`. 2. Bootstrap Serilog in `Program.cs` from configuration. 3. Logs directory via `AppDataLayout.LogsDirectory`. 4. Rolling file, retention ~14 days, compact JSON for diagnostics. 5. Integrate with `DiagnosticsRedactor`; **do not** log URLs, query params, cookies, or raw packets by default. |
| Files to touch | `Program.cs`, new logging bootstrapper, `AppDataLayout.cs`. |
| Tests to add | Redaction tests (assert URLs are redacted in log output). Log sink smoke test. |
| Verification commands | `dotnet test Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | Required for `0.0.18`. |

## P1 — testing and version-control hygiene

### D-D1 — Verify.XunitV3 (P1)

| Field | Value |
|-------|-------|
| ID | D-D1 |
| Priority | **P1** |
| Title | Add `Verify.XunitV3` for snapshot tests |
| Problem | Snapshot tests are needed for compiled plan arguments, diagnostics bundles, and redaction output. |
| Evidence | Not present. Scout D. |
| Scout verification | Scout D. |
| Recommendation | Add `Verify.XunitV3`. Add a `.verified.txt` baseline for compiler argument snapshots, redaction snapshots, and diagnostics bundle snapshots. Run with `Verify.ThrowOnFail = false` in CI for first rollouts. |
| Files to touch | Test csproj files, snapshot directories under each test project. |
| Tests to add | Golden profiles, diagnostics snapshots. |
| Verification commands | `dotnet test Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P1. |

### D-D2 — AwesomeAssertions (P1)

| Field | Value |
|-------|-------|
| ID | D-D2 |
| Priority | **P1** |
| Title | Add `AwesomeAssertions` for readable assertions |
| Problem | The canon discourages `FluentAssertions` v8+ (license risk). `AwesomeAssertions` is the recommended fork. |
| Evidence | Not present. Scout D. |
| Scout verification | Scout D. |
| Recommendation | Add `AwesomeAssertions`. Avoid `FluentAssertions` v8+ entirely. Document the choice in `Z2P-DECISION-LOG.md`. |
| Files to touch | Test csproj files. |
| Tests to add | N/A (mechanical change). |
| Verification commands | `dotnet test Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P1. |

### D-E1 — DynamicData (P1, deferred usage)

| Field | Value |
|-------|-------|
| ID | D-E1 |
| Priority | **P1 (deferred usage)** |
| Title | Defer `DynamicData` until reactive collections appear in the UI |
| Problem | The current UI has no dynamic collections; only 4 manual `RaiseAndSetIfChanged` calls. Adding the package now would add weight without benefit. |
| Evidence | `cwm-roslyn-navigator_find_references` for `RaiseAndSetIfChanged` and `DynamicData` shows no need. Scout E. |
| Scout verification | Scout E. |
| Recommendation | Add `DynamicData` when Logs / Events / Profiles screens need reactive collections. Until then, do not include the package. |
| Files to touch | `Zapret2Pilot.App.csproj` (later). |
| Tests to add | N/A. |
| Verification commands | N/A. |
| Roadmap alignment | P1 (deferred). |

### D-F1 — Microsoft.Extensions.Http.Resilience (P1, deferred)

| Field | Value |
|-------|-------|
| ID | D-F1 |
| Priority | **P1 (deferred)** |
| Title | Defer `Microsoft.Extensions.Http.Resilience` until `Zapret2Pilot.Probing` exists |
| Problem | The probing layer is not yet in the solution. Adding resilience now is premature. |
| Evidence | No `Zapret2Pilot.Probing` project. Scout F. |
| Scout verification | Scout F. |
| Recommendation | When `Zapret2Pilot.Probing` lands, add `Microsoft.Extensions.Http.Resilience` aligned with `Microsoft.Extensions.*` 10.0.9. |
| Files to touch | Future `Zapret2Pilot.Probing` project. |
| Tests to add | N/A. |
| Verification commands | N/A. |
| Roadmap alignment | P1 (deferred). |

### D-G1 — Nerdbank.GitVersioning (P1)

| Field | Value |
|-------|-------|
| ID | D-G1 |
| Priority | **P1** |
| Title | Add `Nerdbank.GitVersioning` for assembly and build versions |
| Problem | The repo needs a deterministic, git-aware assembly version for diagnostics and rollback. The `VERSION` file is a roadmap pointer, not an assembly version. |
| Evidence | `Directory.Build.props` and `version.json` absent. Scout G. |
| Scout verification | Scout G. |
| Recommendation | 1. Add `version.json` with `gitCommitId` style and `assemblyVersion` policy. 2. Keep `VERSION` as the roadmap version. 3. Provide a `IProvideZ2PVersion` (or use the NBGV `GitVersionInformation` type) that the UI can read for display. 4. Do not change the `VERSION` file semantics. |
| Files to touch | `version.json`, `Directory.Build.props`, version provider for UI. |
| Tests to add | Build produces the expected assembly version. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release` and inspect `Zapret2Pilot.App.dll` metadata. |
| Roadmap alignment | Required before preview. |

## P1/P2 — diagnostics and CLI

### D-G2 — BenchmarkDotNet (P1/P2)

| Field | Value |
|-------|-------|
| ID | D-G2 |
| Priority | **P1/P2** |
| Title | Add `benchmarks/Zapret2Pilot.Benchmarks/` for hot-path micro-benchmarks |
| Problem | Hot paths (hostlist parser, log parser, compiler, diagnostics redactor) are not measured. |
| Evidence | No benchmarks project. Scout G. |
| Scout verification | Scout G. |
| Recommendation | 1. Add a separate `benchmarks/Zapret2Pilot.Benchmarks` project. 2. Add benchmarks for: hostlist parser, log parser, compiler, diagnostics redactor. 3. Use `[MemoryDiagnoser]`. 4. Document the methodology. 5. Run manually; do not gate CI. |
| Files to touch | New `Zapret2Pilot.Benchmarks` project. |
| Tests to add | N/A (benchmarks). |
| Verification commands | `dotnet run -c Release --project benchmarks/Zapret2Pilot.Benchmarks`. |
| Roadmap alignment | P1/P2. |

### D-G3 — System.CommandLine (P1/P2)

| Field | Value |
|-------|-------|
| ID | D-G3 |
| Priority | **P1/P2** |
| Title | Add `System.CommandLine` when the CLI project starts |
| Problem | The CLI project is not started yet. Adding the library now would be premature. |
| Evidence | No `Zapret2Pilot.Cli` project. Scout G. |
| Scout verification | Scout G. |
| Recommendation | 1. Add `System.CommandLine` when `Zapret2Pilot.Cli` lands. 2. P1: offline-only commands (validate, compile, inspect). 3. **No** runtime start/stop commands until `RuntimeLockManager` is mature. |
| Files to touch | Future `Zapret2Pilot.Cli` project. |
| Tests to add | CLI argument parsing tests. |
| Verification commands | N/A. |
| Roadmap alignment | P1/P2. |

## P2 — optional, future-facing

### D-G4 — OpenTelemetry (P2)

| Field | Value |
|-------|-------|
| ID | D-G4 |
| Priority | **P2** |
| Title | OpenTelemetry, local and offline only |
| Problem | Diagnostic visibility is valuable, but cloud exporters are forbidden by canon. |
| Evidence | Not present. Scout G. |
| Scout verification | Scout G. |
| Recommendation | 1. Add `OpenTelemetry` SDK with **no** cloud exporter by default. 2. Feature-flag the exporters. 3. Emit traces for runtime sessions, Auto Doctor runs, and probe sessions (when they exist). 4. Use OTLP-to-file or OTLP-to-console only. |
| Files to touch | Telemetry project (future). |
| Tests to add | Smoke tests asserting no network endpoint is contacted. |
| Verification commands | `dotnet test Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P2. |

### D-G5 — Velopack (P2)

| Field | Value |
|-------|-------|
| ID | D-G5 |
| Priority | **P2** |
| Title | Velopack for app and runtime asset updates |
| Problem | Updating the Z2P app is fine. Updating the runtime asset (e.g., `winws2.exe`) is dangerous and must respect the manifest, hash, signature, and rollback rules. |
| Evidence | Not present. Scout G. |
| Scout verification | Scout G. |
| Recommendation | Consider Velopack near public preview. Decouple app updates from runtime-asset updates. Runtime-asset update must go through `ZapretAssetVerifier` and be transactional. |
| Files to touch | Update infrastructure (future). |
| Tests to add | Update simulation tests. |
| Verification commands | N/A. |
| Roadmap alignment | P2. |

### D-G6 — SmartPipe.Core (P2)

| Field | Value |
|-------|-------|
| ID | D-G6 |
| Priority | **P2** |
| Title | `SmartPipe.Core` as an optional adapter only |
| Problem | SmartPipe could be useful for runtime log ingestion and hostlist import — but it must not touch the Runtime Kernel. |
| Evidence | Not present. Scout G. |
| Scout verification | Scout G. |
| Recommendation | 1. Allow `SmartPipe.Core` only as an **adapter** in non-critical layers. 2. **Never** import it into: Runtime Kernel, RuntimeSupervisor, RuntimeProcessHost, CommandBus, Core, or UI ViewModels. 3. Document this constraint in `Z2P-DECISION-LOG.md`. |
| Files to touch | Future adapter projects. |
| Tests to add | N/A. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P2. |

## P3 — long-horizon

### D-H1 — Microsoft.Windows.CsWin32 surface expansion (P3)

| Field | Value |
|-------|-------|
| ID | D-H1 |
| Priority | **P3** |
| Title | Expand `NativeMethods.txt` for additional Win32 surfaces |
| Problem | Additional Win32 calls (e.g., console event handling, named pipes if ever permitted, Job Object extended info) will be needed. |
| Evidence | Initial `NativeMethods.txt` (D-B1). Scout B. |
| Scout verification | Scout B. |
| Recommendation | Add new entries only when a concrete task requires them. Do not pre-stage. |
| Files to touch | `NativeMethods.txt`. |
| Tests to add | As needed. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P3. |

## Storage and forbidden-dependency audit (Scout H)

### D-S1 — Storage: keep direct `Microsoft.Data.Sqlite` (P1)

| Field | Value |
|-------|-------|
| ID | D-S1 |
| Priority | **P1** |
| Title | Reject EF Core and Dapper; keep direct `Microsoft.Data.Sqlite` |
| Problem | EF Core adds weight and a migration story we don’t need. Dapper is unnecessary for the planned schema. |
| Evidence | Storage layer today. Scout H. |
| Scout verification | Scout H. |
| Recommendation | Keep direct `Microsoft.Data.Sqlite`. Do not introduce EF Core or Dapper in MVP. Confirm `Cache=Shared` is **not** used. |
| Files to touch | N/A. |
| Tests to add | A test that opens a connection in two threads and asserts no shared-cache surprises. |
| Verification commands | `dotnet test Zapret2Pilot.slnx -c Release`. |
| Roadmap alignment | P1. |

### D-FORBID — Forbidden-dependency audit (P1)

| Field | Value |
|-------|-------|
| ID | D-FORBID |
| Priority | **P1** |
| Title | Confirm forbidden dependencies are absent |
| Problem | The canon lists forbidden dependencies. They must not appear in the dependency graph. |
| Evidence | `Directory.Packages.props`. Scout H. |
| Scout verification | Scout H. |
| Recommendation | Confirm the following are **absent**: `MediatR`, `Autofac`, `AutoMapper`, `CliWrap`, `Vanara`, `Sentry`, `AppCenter`, `FluentAssertions` v8+, `SmartPipe.*`, `EF Core`, `Dapper`. Add a CI gate that fails the build if any of these are referenced. |
| Files to touch | CI gate (future). |
| Tests to add | A test project that asserts none of the forbidden package IDs are present. |
| Verification commands | `dotnet build Zapret2Pilot.slnx -c Release` and the CI gate. |
| Roadmap alignment | P1. |

---

# Part 3 — Documentation & Process Updates

These updates are documentation-only and may be filed in a single PR accompanying the next safe implementation packet. None of them are excuses to widen scope.

## DOC-1 — `Z2P-DECISION-LOG.md` additions

Add the following decisions:

| ID | Title |
|----|-------|
| DEC-0030 | Runtime Transaction Model — `RuntimeTransactionManager` is the single source of truth for runtime state; no `Rollback` after irreversible kill. |
| DEC-0031 | FakeRuntime Gate — no real `winws2` launch until FakeRuntime gate is green. |
| DEC-0032 | Verified Executable Launch — only `VerifiedRuntimeExecutablePath` is launchable. |
| DEC-0033 | Runtime Kernel Single-Thread Worker — `RuntimeKernelWorker` owns the kernel thread; UI never blocks. |

## DOC-2 — `Z2P-CRITICAL-REVIEW.md` additions

Add findings #26+:

| Finding | Title |
|---------|-------|
| #26 | Verified executable integrity gap (P0-4) |
| #27 | Redirected stdout/stderr not consumed (P0-5) |
| #28 | StopAsync rollback after irreversible kill (P0-6) |
| #29 | UI thread affinity and missing RuntimeKernelWorker (P0-7) |

## DOC-3 — `README.md` fix

Add `tests/Zapret2Pilot.Testing.FakeRuntime/` to the layout tree. This directory is referenced by the runtime tests but is missing from the documented layout.

## DOC-4 — Canon and architecture refresh

- `Z2P-CANON.md`: ensure the “no Windows Service / no IPC / no VPN / no per-URL routing / no raw bat / no arbitrary command execution” list is unchanged and is referenced by every P0 task.
- `Z2P-ARCHITECTURE.md`: add a short paragraph on the `RuntimeKernelWorker` and the `VerifiedRuntimeExecutablePath` value object.

---

# Part 4 — Next Safe Tasks (1–3 starter implementation packets)

The following three packets are independent, bounded, and respect the canon. They are the recommended next implementation batches after this roadmap is approved.

## Packet 0.0.18-A — Verified Executable Integrity

| Field | Value |
|-------|-------|
| Packet ID | 0.0.18-A |
| Title | Verified Executable Integrity |
| Depends on | None |
| Tasks | P0-4, P1-1 |
| Outcome | `RuntimeProcessHost` will refuse to launch any path that is not a `VerifiedRuntimeExecutablePath`. The XML doc on `RuntimeProcessStartContext` matches the implementation. |
| Files | New `VerifiedRuntimeExecutablePath.cs`; modified `RuntimeProcessStartContext.cs`, `ZapretAssetVerificationSummary.cs`, `RuntimeWorkspaceMaterializeResult.cs`, `RuntimeProcessHost.cs`; new tests. |
| Verification | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release`; `dotnet build Zapret2Pilot.slnx -c Release` (0/0). |
| Stop conditions | A test that uses FakeRuntime fails because the verifier path is not bound. Stop and fix the value-object construction, not the test. |

## Packet 0.0.18-B — `RuntimeProcessHost` Hardening: Catches, Redirects, and Stop Transaction Semantics

| Field | Value |
|-------|-------|
| Packet ID | 0.0.18-B |
| Title | `RuntimeProcessHost` Hardening: Catches, Redirects, and Stop Transaction Semantics |
| Depends on | None |
| Tasks | P0-1, P0-5, P0-6, P1-3 |
| Outcome | Empty and broad `catch` blocks in `RuntimeProcessHost` (18 empty `catch { }` plus 4 specific exception filters) are replaced with logged best-effort handlers. `RuntimeProcessHost` no longer deadlocks on verbose output. `StopAsync` no longer rolls the transaction back to `Running` after an irreversible kill. A bounded `CTRL_BREAK` is tried first. |
| Files | `RuntimeProcessHost.cs`, `RuntimeTransactionManager.cs`, tests. |
| Verification | `dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release --filter "FullyQualifiedName~RuntimeProcessHostTests|FullyQualifiedName~RuntimeTransactionManagerTests"`; `dotnet build Zapret2Pilot.slnx -c Release` (0/0). |
| Stop conditions | A catch site in `RuntimeProcessHost` is reintroduced as empty or stays silent. Stop and ensure all 22 sites log. A kill path still rolls back to `Running`. Stop and fix the transaction state machine. |

## Packet 0.0.18-C — Analyzers + Avalonia 12.0.5 + Docs Updates

| Field | Value |
|-------|-------|
| Packet ID | 0.0.18-C |
| Title | Analyzers + Avalonia 12.0.5 + Docs Updates |
| Depends on | None |
| Tasks | D-A1, D-A2, D-A3, DOC-1, DOC-2, DOC-3, DOC-4 |
| Outcome | Meziantou and Roslynator are wired with phased severity. Avalonia is at 12.0.5. `Z2P-DECISION-LOG.md`, `Z2P-CRITICAL-REVIEW.md`, and `README.md` are updated. The canon and architecture docs reflect the new types. |
| Files | `Directory.Packages.props`, `Directory.Build.props`, `.editorconfig`, `docs/Z2P-DECISION-LOG.md`, `docs/Z2P-CRITICAL-REVIEW.md`, `README.md`, `docs/Z2P-CANON.md`, `docs/Z2P-ARCHITECTURE.md`. |
| Verification | `dotnet build Zapret2Pilot.slnx -c Release` (0/0). Markdown lint pass. |
| Stop conditions | An analyzer rule produces a wave of failures across the solution. Stop, scope the rule to `suggestion` for the current milestone, and reschedule to `warning` later. |

> **Note on packet ordering:** A, B, and C are independent and may be executed in any order. None of them launches `winws2` against real traffic. P0-7, P1-2, P1-4, P1-5, P1-6, P1-7, P1-8, and all of Part 2 except the packets above are scheduled for the **0.0.19** planning cycle.

---

# Appendix A — Cross-cutting constraints

These constraints apply to **every** task in this roadmap and to anything that follows.

1. **Single source of truth for runtime state** is `RuntimeTransactionManager`. No other component is allowed to flip `IsRunning` or `IsOwned` outside the transaction API.
2. **No Windows Service.** The app is a privileged single-process desktop application. The runtime kernel lives in `z2p.exe`.
3. **No IPC layer.** No named pipes, no sockets, no shared memory between processes.
4. **No VPN, no proxy, no MITM, no per-URL routing.** Z2P configures and supervises `winws2`. It does not become a network middlebox.
5. **No raw `bat` / `cmd` wrappers.** All process invocation goes through `RuntimeProcessHost`.
6. **No arbitrary command execution from the UI.** The UI cannot type and execute a command string.
7. **No real `winws2` launch before P0 tasks close.** All `0.0.18` work targets FakeRuntime.
8. **Core stays mostly dependency-free.** New dependencies land in dedicated projects.
9. **No cloud telemetry by default.** All telemetry is local and offline; exporters are feature-flagged.
10. **Atomic writes only.** `AtomicFileWriter` for all persistent files.
11. **Path safety only.** `SafePathResolver` for any user-supplied or environment-derived path.
12. **Global Mutex + lock metadata for single-instance.** No second instance, ever.
13. **Windows Job Objects for containment.** Every child process is assigned to a job.
14. **SQLite WAL only.** No `Cache=Shared`.
15. **Content-addressed cache keys.** `RuntimePlanCacheKey` is content-addressed.
16. **Minimal correct patches.** No “drive-by” refactors, no “while we’re here” cleanups.
17. **Two-fix rule.** After two failed fix attempts on the same failure, stop and escalate.
18. **Evidence before assertions.** Every success claim cites a fresh log, exit code, or test result.

---

# Appendix B — Forbidden areas

These areas are explicitly forbidden **at the current roadmap step** and require an explicit canon update and `Z2P-DECISION-LOG.md` entry to be revisited:

- Windows Service registration or SCM interaction.
- IPC layer (named pipes, AF_UNIX sockets, shared memory, mailslots).
- VPN drivers (WinTun, WireGuard), SOCKS/HTTP proxies, MITM utilities.
- Per-URL traffic filtering, sniffing, or rewriting.
- Raw `bat` / `cmd` wrappers invoked from the application.
- Arbitrary command execution from the UI.
- Real `winws2` launch against real network traffic.
- Cloud telemetry by default.
- Forbidden NuGet packages: `MediatR`, `Autofac`, `AutoMapper`, `CliWrap`, `Vanara`, `Sentry`, `AppCenter`, `FluentAssertions` v8+, `SmartPipe.*`, `EF Core`, `Dapper`.

Any PR that touches one of these areas must include a `Z2P-CANON.md` update, a `Z2P-DECISION-LOG.md` entry, and an explicit `oracle` review.

---

# Appendix C — Verification ladder for any task in this roadmap

Use this ladder for every task in this document, narrowed as appropriate.

```powershell
# 1. Restore
dotnet restore Zapret2Pilot.slnx

# 2. Narrow build of the touched project
dotnet build src/Zapret2Pilot.Runtime -c Release

# 3. Narrow test of the touched project
dotnet test tests/Zapret2Pilot.Runtime.Tests -c Release

# 4. Solution build (0/0 expected)
dotnet build Zapret2Pilot.slnx -c Release

# 5. Solution tests
dotnet test Zapret2Pilot.slnx -c Release
```

A task is not complete until steps 4 and 5 are green with the most recent commit.

---

# Appendix D — Roadmap alignment summary

| Roadmap ID | Title | Priority | Milestone | Packet |
|------------|-------|----------|-----------|--------|
| P0-1 | Empty/broad catches in `RuntimeProcessHost` | P0 | 0.0.18 | 0.0.18-B |
| P0-2 | `RuntimeOwnershipLease.Dispose` thread mismatch | P0 | 0.0.18 | 0.0.19 |
| P0-3 | UAC manifest | P0 | 0.0.18 | 0.0.19 |
| P0-4 | Verified executable integrity | P0 | 0.0.18 | 0.0.18-A |
| P0-5 | Redirected stdout/stderr | P0 | 0.0.18 | 0.0.18-B |
| P0-6 | StopAsync rollback after kill | P0 | 0.0.18 | 0.0.18-B |
| P0-7 | RuntimeKernelWorker | P0 | 0.0.18 | 0.0.19 |
| P1-1 | Doc-promise validation | P1 | 0.0.18 | 0.0.18-A |
| P1-2 | `ProcessStartInfo.ArgumentList` | P1 | 0.0.18 | 0.0.19 |
| P1-3 | Graceful stop before kill | P1 | 0.0.18 | 0.0.18-B |
| P1-4 | `RuntimeTransactionManager` thread safety | P1 | 0.0.19 | 0.0.19 |
| P1-5 | `async Task Main` | P1 | 0.0.18 | 0.0.19 |
| P1-6 | Storage in DI | P1 | 0.0.19 | 0.0.19 |
| P1-7 | First `IHostedService` | P1 | 0.0.18 | 0.0.19 |
| P1-8 | `RuntimeOwnershipLease` tests | P1 | 0.0.18 | 0.0.19 |
| D-A1 | Meziantou.Analyzer | P0/P1 | 0.0.18 | 0.0.18-C |
| D-A2 | Roslynator.Analyzers | P0/P1 | 0.0.18 | 0.0.18-C |
| D-A3 | Avalonia 12.0.5 | P0/P1 | 0.0.18 | 0.0.18-C |
| D-B1 | `Microsoft.Windows.CsWin32` | P0/P1 | 0.0.18 | 0.0.19 |
| D-C1 | Serilog | P0/P1 | 0.0.18 | 0.0.19 |
| D-D1 | Verify.XunitV3 | P1 | 0.0.19 | 0.0.19 |
| D-D2 | AwesomeAssertions | P1 | 0.0.19 | 0.0.19 |
| D-E1 | DynamicData (deferred) | P1 | 0.0.20 | n/a |
| D-F1 | Http.Resilience (deferred) | P1 | 0.0.20 | n/a |
| D-G1 | Nerdbank.GitVersioning | P1 | 0.0.19 | 0.0.19 |
| D-G2 | BenchmarkDotNet | P1/P2 | 0.0.20 | n/a |
| D-G3 | System.CommandLine | P1/P2 | 0.0.20 | n/a |
| D-G4 | OpenTelemetry | P2 | 0.0.21 | n/a |
| D-G5 | Velopack | P2 | 0.0.21 | n/a |
| D-G6 | SmartPipe.Core (adapter only) | P2 | 0.0.21 | n/a |
| D-H1 | CsWin32 surface expansion | P3 | 0.0.22+ | n/a |
| D-S1 | Storage: keep direct `Microsoft.Data.Sqlite` | P1 | 0.0.19 | 0.0.19 |
| D-FORBID | Forbidden-dependency audit | P1 | 0.0.19 | 0.0.19 |
| DOC-1 | `Z2P-DECISION-LOG.md` additions | P1 | 0.0.18 | 0.0.18-C |
| DOC-2 | `Z2P-CRITICAL-REVIEW.md` additions | P1 | 0.0.18 | 0.0.18-C |
| DOC-3 | `README.md` fix | P1 | 0.0.18 | 0.0.18-C |
| DOC-4 | Canon and architecture refresh | P1 | 0.0.18 | 0.0.18-C |

---

*End of `Z2P-DETAILED-ROADMAP.md`.*
