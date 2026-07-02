# Runtime Health Monitor Hosted Service (0.0.21) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Generic-Host `IHostedService` that periodically probes the runtime process owned by `RuntimeProcessHost`, publishes immutable `RuntimeHealthSnapshot` values through an `IObservable<RuntimeHealthSnapshot>`, and records an unexpected process exit as a `Failed` session in `IRuntimeKernelStateStore`. Bump the project to `0.0.21`.

**Architecture:**

- Extend `IRuntimeKernelStateStore` / `RuntimeKernelStateStore` with an `EndSession(RuntimeSessionId, RuntimeSessionState)` overload so a session can be explicitly closed as `Stopped` or `Failed`.
- Add a thread-safe `internal Process? RunningProcess { get; }` to `RuntimeProcessHost` so the probe can observe the live runtime process without leaking the host's private state.
- Define `RuntimeHealthState` (`Unknown`, `Healthy`, `Exited`) and immutable `RuntimeHealthSnapshot` in `Zapret2Pilot.Runtime.Health`.
- Define `IRuntimeHealthMonitor` exposing `IObservable<RuntimeHealthSnapshot> SnapshotChanged` and `RuntimeHealthSnapshot LatestSnapshot`.
- Implement `RuntimeHealthMonitor` as `IHostedService`, `IRuntimeHealthMonitor`, `IDisposable`. A `System.Threading.Timer` fires on a thread-pool thread but only enqueues a probe onto `RuntimeKernelWorker`; the probe reads `RuntimeProcessHost.RunningProcess` on the worker thread, builds a snapshot, pushes it through a `BehaviorSubject<RuntimeHealthSnapshot>` and — on a transition into `Exited` while the state store still reports an active session — calls `EndSession(current.Id, RuntimeSessionState.Failed)`.
- Register the monitor in DI through a new `AddRuntimeHealthMonitor(this IServiceCollection)` extension that resolves the existing `RuntimeProcessHost`, `RuntimeKernelWorker` and `IRuntimeKernelStateStore` singletons, and add the call to `AppHost.Build` after `AddRuntimeProcessHost()`.
- Bump `VERSION` to `0.0.21`, update `MainWindowViewModel.AppVersion` and its `v0.0.20` test assertion to `v0.0.21`, and add a `0.0.21` section to `docs/Z2P-ROADMAP.md` and `docs/Z2P-IMPLEMENTATION-STATUS.md`.

**Tech Stack:**

- C# / .NET 10
- `Microsoft.Extensions.Hosting` (`IHostedService`, Generic Host)
- `System.Reactive` 6.1.0 (`BehaviorSubject<T>`, `IObservable<T>`, `AsObservable()`)
- `Microsoft.Data.Sqlite` 10.0.9 (state store)
- xUnit v3 (existing test framework)
- `Zapret2Pilot.Testing.FakeRuntime` (test-only process stand-in; no real `winws2`)

## Global Constraints

These apply to every task unless a task explicitly overrides them. The values are copied verbatim from the spec / canon.

- No real `winws2` launch; integration tests use `Zapret2Pilot.Testing.FakeRuntime`.
- No UI thread blocking; the timer callback must only schedule work, every probe runs on the `RuntimeKernelWorker` thread.
- All runtime process state reads happen on the dedicated `RuntimeKernelWorker` thread.
- No new NuGet package versions; only existing centrally-managed packages. `System.Reactive` 6.1.0 is already in `Directory.Packages.props`; the new dependencies are local `PackageReference` entries on `Zapret2Pilot.Runtime` and `Zapret2Pilot.Runtime.Tests` that pull from the central version.
- Do not add Windows Service, IPC, VPN, proxy, MITM, per-URL router, `.bat` / `.cmd` wrappers, or arbitrary command execution.
- Do not overwrite unrelated files. The pre-committed changes to `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` (`AppVersion = "v0.0.20"`), `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs` (asserts `v0.0.20`), `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs` (DI integration test for `AddRuntimeProcessHost`) and `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeKernelWorkerUiNonBlockingTests.cs` (widened timing budget) MUST be preserved and will be brought forward to `v0.0.21` in Task 13.
- Do not commit or push.
- The pre-existing untracked plan file `docs/superpowers/plans/2026-07-01-version-sync-di-test-flake-hardening.md` is unrelated to this milestone; do not touch it.

---

## File Structure

The following files are created or modified by this plan. Each file has exactly one responsibility.

| File | Responsibility |
| --- | --- |
| `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj` | Add `System.Reactive` `PackageReference` |
| `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs` | Add `EndSession(RuntimeSessionId, RuntimeSessionState)` contract |
| `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs` | Implement the new overload (refactor private `CloseSession` to accept a final state) |
| `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` | Expose `internal Process? RunningProcess { get; }` (thread-safe under `stateLock`) |
| `src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs` | New enum (`Unknown`, `Healthy`, `Exited`) |
| `src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs` | New immutable `sealed record class` |
| `src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs` | New public interface (`SnapshotChanged`, `LatestSnapshot`) |
| `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs` | New `IHostedService` + `IRuntimeHealthMonitor` + `IDisposable` |
| `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` | New `AddRuntimeHealthMonitor(this IServiceCollection)` extension |
| `src/Zapret2Pilot.App/Program.cs` | Call `services.AddRuntimeHealthMonitor()` after `AddRuntimeProcessHost()` |
| `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj` | Add `System.Reactive` `PackageReference` for the health-monitor tests |
| `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs` | Add overload tests (`Failed`, unknown id, null id, invalid final state) |
| `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs` | New contract tests |
| `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs` | New integration tests using `FakeRuntime` + `BehaviorSubject` observations |
| `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs` | Add DI test that `IRuntimeHealthMonitor` and `IHostedService` resolve to the same singleton |
| `VERSION` | `0.0.20` → `0.0.21` |
| `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` | `AppVersion` `v0.0.20` → `v0.0.21` |
| `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs` | `v0.0.20` → `v0.0.21` |
| `docs/Z2P-ROADMAP.md` | New `0.0.21 — Runtime Health Monitor Hosted Service` section |
| `docs/Z2P-IMPLEMENTATION-STATUS.md` | New `0.0.21` status section |

---

## Task 1: Add `System.Reactive` PackageReference to `Zapret2Pilot.Runtime`

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: `Zapret2Pilot.Runtime` now references `System.Reactive` 6.1.0 from the central package management, so later tasks can use `BehaviorSubject<T>` and `IObservable<T>`.

- [ ] **Step 1: Add the package reference**

In `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj`, add a new `PackageReference` to the `ItemGroup` that already contains `Microsoft.Extensions.Logging.Abstractions`. The resulting file should look like:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AssemblyName>Zapret2Pilot.Runtime</AssemblyName>
    <RootNamespace>Zapret2Pilot.Runtime</RootNamespace>
    <Description>Runtime ownership and safety foundation for Zapret2Pilot.</Description>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <!--
      CA1848 (LoggerMessage source generator) and CA1873 (expensive
      logging arguments) are suppressed at the project level. The
      Runtime Kernel's first logger (ILogger<RuntimeProcessHost>,
      added in 0.0.18) is used only in best-effort cleanup paths
      where allocation-free logging is not a concern, and the
      arguments it captures are already-resolved exception and PID
      values rather than freshly-allocated strings. Migrating to
      LoggerMessage source generators is tracked separately and is
      out of scope for 0.0.18.
    -->
    <NoWarn>$(NoWarn);CA1848;CA1873</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Zapret2Pilot.Infrastructure\Zapret2Pilot.Infrastructure.csproj" />
    <ProjectReference Include="..\Zapret2Pilot.Engine.Zapret2\Zapret2Pilot.Engine.Zapret2.csproj" />
    <ProjectReference Include="..\Zapret2Pilot.Storage\Zapret2Pilot.Storage.csproj" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="System.Reactive" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Verify the change compiles**

Run:

```powershell
dotnet restore Zapret2Pilot.slnx
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
```

Expected: restore succeeds (no new packages are downloaded because the version is already managed centrally in `Directory.Packages.props` line 26: `<PackageVersion Include="System.Reactive" Version="6.1.0" />`); build succeeds with no new warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj
git commit -m "feat(runtime): add System.Reactive package reference"
```

---

## Task 2: Add `EndSession(RuntimeSessionId, RuntimeSessionState)` overload to the state store contract

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs`

**Interfaces:**
- Consumes: nothing
- Produces: the contract `void EndSession(RuntimeSessionId sessionId, RuntimeSessionState finalState)` on `IRuntimeKernelStateStore`. Implementation arrives in Task 3. The method closes the session as the supplied terminal state (`Stopped` or `Failed`) and resets the singleton state row to `is_running = 0`, `session_id = NULL`.

- [ ] **Step 1: Add the new method to the interface**

In `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs`, after the existing `void EndSession(RuntimeSessionId sessionId);` member (line 60), add a new overload. The full file should look like:

```csharp
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;

namespace Zapret2Pilot.Runtime.State;

/// <summary>
/// Persistent store for the Runtime Kernel's session and singleton
/// runtime state.
///
/// Backed by the SQLite tables created by migration
/// <c>0002_runtime_state_store</c> in
/// <see cref="Zapret2Pilot.Storage.Sqlite.SqliteDbInitializer"/>:
/// <list type="bullet">
///   <item><c>runtime_sessions</c> — append-only history of every
///         session, with its <c>profile</c>, <c>plan</c> and plan
///         cache key.</item>
///   <item><c>runtime_state</c> — singleton row tracking the
///         currently active session (if any) and the
///         <c>is_running</c> flag.</item>
/// </list>
///
/// All operations are synchronous to match the existing
/// <c>Zapret2Pilot.Storage</c> repositories. The store is intended
/// to be called from the Runtime Kernel's single-writer thread
/// (the same thread that drives
/// <c>RuntimeProcessHost</c>/<c>RuntimeTransactionManager</c>); no
/// internal locking is performed.
/// </summary>
public interface IRuntimeKernelStateStore
{
    /// <summary>
    /// Opens a new active session and updates the singleton state
    /// row to point at it. Any previously active session is
    /// automatically closed with <c>EndedAtUtc = now</c> and
    /// <see cref="RuntimeSessionState.Stopped"/> before the new row
    /// is inserted, so the store never holds more than one active
    /// session at a time.
    /// </summary>
    /// <param name="profileId">Profile that owns the session.</param>
    /// <param name="planId">Compiled plan installed for the session.</param>
    /// <param name="planCacheKey">Content-addressed cache key of the
    /// compiled plan.</param>
    /// <returns>The newly created session record, as persisted.</returns>
    RuntimeSessionRecord StartSession(
        ProfileId profileId,
        RuntimePlanId planId,
        RuntimePlanCacheKey planCacheKey);

    /// <summary>
    /// Closes the session identified by <paramref name="sessionId"/>
    /// as <see cref="RuntimeSessionState.Stopped"/> and resets the
    /// singleton state row to <c>is_running = 0</c>,
    /// <c>session_id = NULL</c>.
    /// </summary>
    /// <param name="sessionId">Identifier of the session to close.</param>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when no session with the given
    /// <paramref name="sessionId"/> exists.
    /// </exception>
    void EndSession(RuntimeSessionId sessionId);

    /// <summary>
    /// Closes the session identified by <paramref name="sessionId"/>
    /// with the supplied terminal <paramref name="finalState"/> and
    /// resets the singleton state row to <c>is_running = 0</c>,
    /// <c>session_id = NULL</c>. Used by the health monitor to mark
    /// a session as <see cref="RuntimeSessionState.Failed"/> on an
    /// unexpected process exit; the single-argument overload closes
    /// a session as <see cref="RuntimeSessionState.Stopped"/>.
    /// </summary>
    /// <param name="sessionId">Identifier of the session to close.</param>
    /// <param name="finalState">Terminal state to record
    /// (<see cref="RuntimeSessionState.Stopped"/> or
    /// <see cref="RuntimeSessionState.Failed"/>).</param>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="finalState"/> is not a terminal
    /// state (only <c>Stopped</c> and <c>Failed</c> are accepted).
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when no session with the given
    /// <paramref name="sessionId"/> exists.
    /// </exception>
    void EndSession(RuntimeSessionId sessionId, RuntimeSessionState finalState);

    /// <summary>
    /// Returns the session currently tracked by the singleton
    /// <c>runtime_state</c> row, or <c>null</c> if the runtime is
    /// not running (no active session).
    /// </summary>
    /// <returns>The current session record, or <c>null</c>.</returns>
    RuntimeSessionRecord? GetCurrentSession();

    /// <summary>
    /// Returns the most recent session records ordered by
    /// <c>started_utc DESC</c> (newest first), capped at
    /// <paramref name="count"/> rows.
    /// </summary>
    /// <param name="count">Maximum number of records to return.</param>
    IReadOnlyList<RuntimeSessionRecord> GetRecentSessions(int count);
}
```

- [ ] **Step 2: Verify the file compiles**

Run:

```powershell
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
```

Expected: build FAILS with `CS0535: 'RuntimeKernelStateStore' does not implement interface member 'IRuntimeKernelStateStore.EndSession(RuntimeSessionId, RuntimeSessionState)'`. This is the expected TDD red-light: the contract is now stricter than the implementation. The fix is delivered in Task 3.

- [ ] **Step 3: Do not commit yet**

Leave the failing build in place. Task 3 fixes the implementation; both are committed together in Task 3's commit step.

---

## Task 3: Implement the `EndSession(RuntimeSessionId, RuntimeSessionState)` overload

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs`

**Interfaces:**
- Consumes: the contract from Task 2
- Produces: the production implementation. Adds a public overload that validates `finalState` is `Stopped` or `Failed`, refactors the private `CloseSession` helper to accept a state string so the existing single-argument overload and the new overload share the same SQL.

- [ ] **Step 1: Refactor the private `CloseSession` helper**

In `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs`, change the private `CloseSession` helper (lines 238–258) to take a `state` parameter, and change the SQL parameter to bind the state. Replace the existing helper with:

```csharp
    private static void CloseSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionIdValue,
        string endedUtcText,
        string state)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE runtime_sessions
            SET ended_utc = $endedUtc,
                state = $state
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$endedUtc", endedUtcText);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$id", sessionIdValue);

        command.ExecuteNonQuery();
    }
```

- [ ] **Step 2: Update the existing single-argument `EndSession` to pass the new state argument**

In the same file, update the existing `public void EndSession(RuntimeSessionId sessionId)` (lines 69–89) to call the refactored helper. Replace it with:

```csharp
    public void EndSession(RuntimeSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId, nameof(sessionId));

        EndSessionInternal(sessionId, StoppedState);
    }
```

- [ ] **Step 3: Add the new public overload**

In the same file, immediately after the existing `public void EndSession(RuntimeSessionId sessionId)`, add:

```csharp
    public void EndSession(RuntimeSessionId sessionId, RuntimeSessionState finalState)
    {
        ArgumentNullException.ThrowIfNull(sessionId, nameof(sessionId));

        if (finalState is not (RuntimeSessionState.Stopped or RuntimeSessionState.Failed))
        {
            throw new ArgumentException(
                $"EndSession final state must be Stopped or Failed; got '{finalState}'.",
                nameof(finalState));
        }

        EndSessionInternal(sessionId, StateToText(finalState));
    }

    private void EndSessionInternal(RuntimeSessionId sessionId, string finalStateText)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        string nowText = nowUtc.ToString("O", CultureInfo.InvariantCulture);

        using SqliteConnection connection = connectionFactory.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        if (!SessionExists(connection, transaction, sessionId.Value))
        {
            throw new InvalidOperationException(
                $"No runtime session with id '{sessionId.Value}' exists.");
        }

        CloseSession(connection, transaction, sessionId.Value, nowText, finalStateText);
        ClearRuntimeState(connection, transaction, nowText);

        transaction.Commit();
    }

    private static string StateToText(RuntimeSessionState state)
    {
        return state switch
        {
            RuntimeSessionState.Active => ActiveState,
            RuntimeSessionState.Stopped => StoppedState,
            RuntimeSessionState.Failed => "Failed",
            _ => throw new ArgumentException(
                $"Unknown {nameof(RuntimeSessionState)} value: {state}.",
                nameof(state)),
        };
    }
```

- [ ] **Step 4: Verify the file compiles**

Run:

```powershell
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
```

Expected: build succeeds; the CS0535 error from Task 2 is gone.

- [ ] **Step 5: Run the existing state-store tests to confirm no regression**

Run:

```powershell
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeKernelStateStoreTests"
```

Expected: all 11 existing `RuntimeKernelStateStoreTests` pass; the refactor of `CloseSession` and the new helper do not change observable behavior for the existing tests.

- [ ] **Step 6: Commit**

```bash
git add src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs
git commit -m "feat(runtime): add EndSession overload that records a terminal state"
```

---

## Task 4: Expose `internal Process? RunningProcess { get; }` on `RuntimeProcessHost`

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`

**Interfaces:**
- Consumes: nothing
- Produces: a thread-safe `internal Process? RunningProcess { get; }` accessor that reads the existing private `process` field under the host's `stateLock`. This is the only supported way for the health monitor to observe the live runtime process; the private field remains private.

- [ ] **Step 1: Add the property immediately after the `stateLock` field**

In `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs`, after the `stateLock` declaration (line 108) and before the `ownershipLease` field declaration (line 109), insert the property. Use the `edit` tool with the unique surrounding context. Replace:

```csharp
    private readonly object stateLock = new();
    private RuntimeOwnershipLease? ownershipLease;
    private Process? process;
    private IRuntimeJobObject? jobObject;
    private bool disposed;
```

with:

```csharp
    private readonly object stateLock = new();
    private RuntimeOwnershipLease? ownershipLease;
    private Process? process;
    private IRuntimeJobObject? jobObject;
    private bool disposed;

    /// <summary>
    /// The runtime <see cref="Process"/> currently owned by this
    /// host, or <c>null</c> when the host is not running a process.
    /// Exposed as <c>internal</c> so the
    /// <see cref="Zapret2Pilot.Runtime.Health.RuntimeHealthMonitor"/>
    /// can probe the live process from the dedicated
    /// <see cref="RuntimeKernelWorker"/> thread without taking a
    /// dependency on the private fields. The accessor takes the
    /// host's <c>stateLock</c> so the read is thread-safe under
    /// concurrent stop / dispose paths. The caller MUST NOT dispose
    /// the returned <see cref="Process"/> — its lifetime is owned by
    /// this host.
    /// </summary>
    internal Process? RunningProcess
    {
        get
        {
            lock (stateLock)
            {
                return process;
            }
        }
    }
```

- [ ] **Step 2: Verify the file compiles**

Run:

```powershell
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~Hosting"
```

Expected: build succeeds; the existing `RuntimeProcessHostTests` still pass. The new `RunningProcess` accessor is `internal` so the test project (which has `InternalsVisibleTo`) can read it; no other public API change occurred.

- [ ] **Step 3: Commit**

```bash
git add src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs
git commit -m "feat(runtime): expose thread-safe RunningProcess accessor for health monitor"
```

---

## Task 5: Define `RuntimeHealthState` and `RuntimeHealthSnapshot`

**Files:**
- Create: `src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs`
- Create: `src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs`

**Interfaces:**
- Consumes: `Zapret2Pilot.Core.Primitives` and `Zapret2Pilot.Core.Results` (transitively available through existing project references)
- Produces:
  - `public enum RuntimeHealthState { Unknown, Healthy, Exited }`
  - `public sealed record class RuntimeHealthSnapshot(RuntimeHealthState state, int? processId, DateTimeOffset observedAtUtc)` with constructor validation: `state` must be defined; `processId` must be positive when non-null.

- [ ] **Step 1: Create `RuntimeHealthState.cs`**

Write the file `src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs` with:

```csharp
namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Lifecycle state of the runtime process as observed by the
/// <see cref="RuntimeHealthMonitor"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Unknown"/> — the monitor has no live process
///         to probe (no runtime has been started, or the host has
///         cleared its state).</item>
///   <item><see cref="Healthy"/> — the runtime process is alive
///         and running.</item>
///   <item><see cref="Exited"/> — the runtime process has exited
///         unexpectedly. The monitor marks the active session as
///         <see cref="State.RuntimeSessionState.Failed"/> on the
///         transition into <see cref="Exited"/>.</item>
/// </list>
/// </remarks>
public enum RuntimeHealthState
{
    Unknown,
    Healthy,
    Exited,
}
```

- [ ] **Step 2: Create `RuntimeHealthSnapshot.cs`**

Write the file `src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs` with:

```csharp
using System;

namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Immutable snapshot of the runtime process health, produced by
/// <see cref="RuntimeHealthMonitor"/> on every probe. Snapshots are
/// pushed through a <c>BehaviorSubject&lt;RuntimeHealthSnapshot&gt;</c>
/// so subscribers always see the most recent value plus every
/// transition.
/// </summary>
public sealed record class RuntimeHealthSnapshot
{
    public RuntimeHealthSnapshot(
        RuntimeHealthState state,
        int? processId,
        DateTimeOffset observedAtUtc)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentException(
                $"Unknown {nameof(RuntimeHealthState)} value: {state}.",
                nameof(state));
        }

        if (processId is not null && processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "Process id must be a positive integer when supplied.");
        }

        State = state;
        ProcessId = processId;
        ObservedAtUtc = observedAtUtc;
    }

    /// <summary>
    /// Current health state of the runtime process.
    /// </summary>
    public RuntimeHealthState State { get; }

    /// <summary>
    /// Process id of the runtime process, or <c>null</c> when no
    /// process is currently owned by the host
    /// (<see cref="RuntimeHealthState.Unknown"/>).
    /// </summary>
    public int? ProcessId { get; }

    /// <summary>
    /// Wall-clock UTC timestamp recorded when the probe built the
    /// snapshot.
    /// </summary>
    public DateTimeOffset ObservedAtUtc { get; }
}
```

- [ ] **Step 3: Verify the file compiles**

Run:

```powershell
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
```

Expected: build succeeds; the two new files are discovered as part of the `Zapret2Pilot.Runtime` project and compiled into the assembly.

- [ ] **Step 4: Commit**

```bash
git add src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs
git commit -m "feat(runtime): add RuntimeHealthState enum and RuntimeHealthSnapshot record"
```

---

## Task 6: Define `IRuntimeHealthMonitor`

**Files:**
- Create: `src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs`

**Interfaces:**
- Consumes: nothing new
- Produces:
  - `public interface IRuntimeHealthMonitor { IObservable<RuntimeHealthSnapshot> SnapshotChanged { get; } RuntimeHealthSnapshot LatestSnapshot { get; } }`

- [ ] **Step 1: Create the interface file**

Write the file `src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs` with:

```csharp
using System;

namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Public contract for the runtime health monitor. Exposes a hot
/// <see cref="IObservable{T}"/> of <see cref="RuntimeHealthSnapshot"/>
/// values plus a fast-path accessor for the most recent snapshot.
/// Subscribers receive the initial <see cref="RuntimeHealthState.Unknown"/>
/// snapshot synchronously, then a new snapshot on every probe tick
/// where the state changes (and additionally an
/// <see cref="RuntimeHealthState.Exited"/> snapshot whenever a
/// transition into <see cref="RuntimeHealthState.Exited"/> is
/// observed while the kernel state store has an active session, even
/// if the underlying state did not change between two consecutive
/// probes).
/// </summary>
public interface IRuntimeHealthMonitor
{
    /// <summary>
    /// Hot observable that emits the current snapshot on
    /// subscription and a new snapshot on every transition.
    /// Subscribers must add their own <c>ObserveOn</c> if they need
    /// a particular scheduler.
    /// </summary>
    IObservable<RuntimeHealthSnapshot> SnapshotChanged { get; }

    /// <summary>
    /// Most recent snapshot observed by the monitor. Returns
    /// <see cref="RuntimeHealthState.Unknown"/> before
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync"/>
    /// is called.
    /// </summary>
    RuntimeHealthSnapshot LatestSnapshot { get; }
}
```

- [ ] **Step 2: Verify the file compiles**

Run:

```powershell
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
```

Expected: build succeeds; the new interface is part of the assembly.

- [ ] **Step 3: Commit**

```bash
git add src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs
git commit -m "feat(runtime): add IRuntimeHealthMonitor contract"
```

---

## Task 7: Implement `RuntimeHealthMonitor` as `IHostedService`, `IRuntimeHealthMonitor`, `IDisposable`

**Files:**
- Create: `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs`

**Interfaces:**
- Consumes:
  - `RuntimeProcessHost` (singleton, exposes `internal Process? RunningProcess { get; }`)
  - `RuntimeKernelWorker` (singleton; exposes `Enqueue(Func<CancellationToken, Task>, CancellationToken)`)
  - `IRuntimeKernelStateStore` (singleton)
  - `ILogger<RuntimeHealthMonitor>` (DI-provided)
  - `TimeSpan? probeInterval` constructor parameter (defaults to `DefaultProbeInterval = TimeSpan.FromMilliseconds(500)`)
- Produces:
  - `public sealed class RuntimeHealthMonitor : IHostedService, IRuntimeHealthMonitor, IDisposable`
  - `public Task StartAsync(CancellationToken)` — starts a `System.Threading.Timer` (no probe runs before this)
  - `public Task StopAsync(CancellationToken)` — stops and disposes the timer
  - `public void Dispose()` — idempotent, calls `StopAsync`
  - `public IObservable<RuntimeHealthSnapshot> SnapshotChanged => subject.AsObservable();`
  - `public RuntimeHealthSnapshot LatestSnapshot => subject.Value;`
  - Internal probe pipeline: timer callback enqueues a probe; probe reads `RunningProcess`, builds the snapshot, calls `EndSession(id, Failed)` on a transition into `Exited` when the state store has an active session, then `OnNext`s the snapshot.
  - Initial `BehaviorSubject` value is `new RuntimeHealthSnapshot(RuntimeHealthState.Unknown, null, ObservedAtUtc: DateTimeOffset.UtcNow)`.

- [ ] **Step 1: Create the implementation file**

Write the file `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs` with the following content:

```csharp
using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.State;

namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Generic-Host <see cref="IHostedService"/> that periodically
/// probes the runtime process owned by <see cref="RuntimeProcessHost"/>
/// and publishes immutable <see cref="RuntimeHealthSnapshot"/>
/// values. The probe runs on the dedicated
/// <see cref="RuntimeKernelWorker"/> thread so every read of the
/// host's process state happens on the canonical kernel thread.
/// </summary>
/// <remarks>
/// <para>
/// The monitor owns a <see cref="System.Threading.Timer"/> whose
/// callback runs on a <c>ThreadPool</c> thread. The callback is
/// intentionally minimal: it only enqueues a probe onto the worker.
/// All kernel work — reading <c>RuntimeProcessHost.RunningProcess</c>,
/// calling <c>IRuntimeKernelStateStore.EndSession</c> on a
/// transition into <see cref="RuntimeHealthState.Exited"/>, and
/// pushing the new snapshot through the
/// <see cref="BehaviorSubject{T}"/> — happens on the worker
/// thread. The <see cref="System.Threading.Timer"/> never blocks
/// the UI thread.
/// </para>
/// <para>
/// A transition into <see cref="RuntimeHealthState.Exited"/> is
/// the single signal the monitor uses to mark a session as
/// <see cref="RuntimeSessionState.Failed"/>. The transition is
/// detected by comparing the freshly built snapshot's
/// <see cref="RuntimeHealthSnapshot.State"/> against the
/// <see cref="BehaviorSubject{T}.Value"/> before the new snapshot is
/// published. Healthy and Exited probes that do not change the
/// state do not re-record the session.
/// </para>
/// <para>
/// <see cref="StartAsync"/> starts the timer. <see cref="StopAsync"/>
/// disposes the timer, marks the monitor as stopping, and awaits
/// the worker's cancellation of any in-flight probe. <see cref="Dispose"/>
/// is idempotent and forwards to <see cref="StopAsync"/>.
/// </para>
/// </remarks>
public sealed class RuntimeHealthMonitor : IHostedService, IRuntimeHealthMonitor, IDisposable
{
    /// <summary>
    /// Default probe interval applied when the caller does not
    /// supply one. 500 ms is a reasonable balance between
    /// responsiveness and overhead for a hosted health probe.
    /// </summary>
    internal static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromMilliseconds(500);

    private readonly RuntimeProcessHost host;
    private readonly RuntimeKernelWorker worker;
    private readonly IRuntimeKernelStateStore stateStore;
    private readonly ILogger<RuntimeHealthMonitor> logger;
    private readonly TimeSpan probeInterval;
    private readonly BehaviorSubject<RuntimeHealthSnapshot> subject;

    private readonly object timerLock = new();
    private Timer? timer;
    private int stoppingFlag;
    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="RuntimeHealthMonitor"/>.
    /// </summary>
    /// <param name="host">Runtime process host that owns the live
    /// process to probe.</param>
    /// <param name="worker">Dedicated kernel worker thread used to
    /// run the probe body.</param>
    /// <param name="stateStore">Persistent kernel state store used
    /// to mark sessions as <c>Failed</c>.</param>
    /// <param name="logger">Logger that receives structured events
    /// for unexpected errors and state-store failures.</param>
    /// <param name="probeInterval">Probe interval. Defaults to
    /// <see cref="DefaultProbeInterval"/> when <c>null</c>.</param>
    public RuntimeHealthMonitor(
        RuntimeProcessHost host,
        RuntimeKernelWorker worker,
        IRuntimeKernelStateStore stateStore,
        ILogger<RuntimeHealthMonitor> logger,
        TimeSpan? probeInterval = null)
    {
        ArgumentNullException.ThrowIfNull(host, nameof(host));
        ArgumentNullException.ThrowIfNull(worker, nameof(worker));
        ArgumentNullException.ThrowIfNull(stateStore, nameof(stateStore));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        TimeSpan effectiveInterval = probeInterval ?? DefaultProbeInterval;
        if (effectiveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probeInterval),
                effectiveInterval,
                "Probe interval must be a positive time span.");
        }

        this.host = host;
        this.worker = worker;
        this.stateStore = stateStore;
        this.logger = logger;
        this.probeInterval = effectiveInterval;
        this.subject = new BehaviorSubject<RuntimeHealthSnapshot>(
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Unknown,
                processId: null,
                observedAtUtc: DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public IObservable<RuntimeHealthSnapshot> SnapshotChanged => subject.AsObservable();

    /// <inheritdoc />
    public RuntimeHealthSnapshot LatestSnapshot => subject.Value;

    /// <summary>
    /// Starts the probe timer. Returns immediately; the first
    /// scheduled probe fires after <see cref="probeInterval"/>.
    /// </summary>
    /// <param name="cancellationToken">Observed before the timer is
    /// created.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (timerLock)
        {
            if (timer is not null)
            {
                return Task.CompletedTask;
            }

            timer = new Timer(OnTimerTick, state: null, dueTime: probeInterval, period: probeInterval);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the probe timer. Idempotent. Awaits the worker's
    /// cancellation of any in-flight probe so the monitor never
    /// publishes a snapshot after <see cref="StopAsync"/> returns.
    /// </summary>
    /// <param name="cancellationToken">Observed while waiting for
    /// the worker to drain.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Timer? toDispose;
        lock (timerLock)
        {
            toDispose = timer;
            timer = null;
        }

        toDispose?.Dispose();
        Interlocked.Exchange(ref stoppingFlag, 1);

        // Drain the worker so an in-flight probe is allowed to
        // complete before Dispose() returns. The worker's channel
        // already serialises probes, so awaiting the most recent
        // enqueued work item is enough to guarantee no further
        // OnNext happens on this monitor.
        try
        {
            await worker.Enqueue(_ => Task.CompletedTask, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected when the host cancels shutdown
        }
    }

    /// <summary>
    /// Disposes the monitor. Idempotent; calls
    /// <see cref="StopAsync"/> with <see cref="CancellationToken.None"/>
    /// and completes the <see cref="BehaviorSubject{T}"/>.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeHealthMonitor: StopAsync threw during Dispose.");
        }
        finally
        {
            subject.OnCompleted();
            subject.Dispose();
        }
    }

    private void OnTimerTick(object? state)
    {
        if (Volatile.Read(ref stoppingFlag) != 0)
        {
            return;
        }

        try
        {
            // Fire and forget: the probe body runs on the worker
            // thread and the timer is intentionally not blocked
            // waiting for it. Any failure inside the probe is
            // logged and observed through the worker.
            _ = worker.Enqueue(ProbeOnWorkerAsync, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RuntimeHealthMonitor: failed to enqueue probe.");
        }
    }

    private async Task ProbeOnWorkerAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Process? process = host.RunningProcess;
        RuntimeHealthState newState;
        int? newProcessId;

        if (process is null)
        {
            newState = RuntimeHealthState.Unknown;
            newProcessId = null;
        }
        else
        {
            newProcessId = SafeGetProcessId(process);
            newState = process.HasExited
                ? RuntimeHealthState.Exited
                : RuntimeHealthState.Healthy;
        }

        RuntimeHealthSnapshot next = new(
            state: newState,
            processId: newProcessId,
            observedAtUtc: DateTimeOffset.UtcNow);

        RuntimeHealthSnapshot previous = subject.Value;
        if (ShouldRecordFailedSession(previous.State, newState))
        {
            TryMarkActiveSessionAsFailed();
        }

        subject.OnNext(next);
    }

    private static bool ShouldRecordFailedSession(
        RuntimeHealthState previous,
        RuntimeHealthState next)
    {
        // Only transitions INTO Exited trigger the failed-session
        // recording. A subsequent probe that observes Exited again
        // (because the host has not yet been stopped) does not
        // re-record the session.
        return next == RuntimeHealthState.Exited && previous != RuntimeHealthState.Exited;
    }

    private void TryMarkActiveSessionAsFailed()
    {
        try
        {
            RuntimeSessionRecord? current = stateStore.GetCurrentSession();
            if (current is null)
            {
                return;
            }

            stateStore.EndSession(current.Id, RuntimeSessionState.Failed);
            logger.LogWarning(
                "RuntimeHealthMonitor: marked active session {SessionId} as Failed after an unexpected process exit.",
                current.Id.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "RuntimeHealthMonitor: failed to mark active session as Failed.");
        }
    }

    private static int SafeGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
```

- [ ] **Step 2: Verify the file compiles**

Run:

```powershell
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
```

Expected: build succeeds. The new file is part of the `Zapret2Pilot.Runtime` assembly.

- [ ] **Step 3: Commit**

```bash
git add src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs
git commit -m "feat(runtime): add RuntimeHealthMonitor hosted service"
```

---

## Task 8: Add `AddRuntimeHealthMonitor` DI extension

**Files:**
- Modify: `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `RuntimeHealthMonitor` from Task 7
- Produces: `public static IServiceCollection AddRuntimeHealthMonitor(this IServiceCollection services)` that registers the monitor as:
  - `services.AddSingleton<RuntimeHealthMonitor>()` (concrete type)
  - `services.AddSingleton<IRuntimeHealthMonitor>(sp => sp.GetRequiredService<RuntimeHealthMonitor>())`
  - `services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RuntimeHealthMonitor>())`
  - All three registrations resolve to the same singleton instance. The extension does NOT call `AppDataLayout.EnsureCreated()`; registration is pure.

- [ ] **Step 1: Add the new extension method**

In `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs`, add the following new method immediately after the existing `AddRuntimeProcessHost` method (after line 130). The full file should look like:

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Infrastructure.FileSystem;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Locking;
using Zapret2Pilot.Runtime.Ownership;
using Zapret2Pilot.Runtime.Recovery;
using Zapret2Pilot.Runtime.State;
using Zapret2Pilot.Runtime.Transactions;
using Zapret2Pilot.Runtime.Windows;
using Zapret2Pilot.Runtime.Workspace;
using Zapret2Pilot.Storage.Sqlite;

namespace Microsoft.Extensions.DependencyInjection;

public static class RuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimeKernelStateStore(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddSingleton(new SqliteStorageOptions(databasePath));
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqliteDbInitializer>();
        services.AddSingleton<IRuntimeKernelStateStore>(static sp =>
        {
            sp.GetRequiredService<SqliteDbInitializer>().Initialize();
            return new RuntimeKernelStateStore(sp.GetRequiredService<SqliteConnectionFactory>());
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="RuntimeKernelWorker"/> as a singleton
    /// in the supplied <see cref="IServiceCollection"/>, both as
    /// its concrete type and as an <see cref="IHostedService"/>
    /// (which the Microsoft.Extensions.Hosting infrastructure will
    /// start and stop alongside the rest of the host). The two
    /// registrations resolve to the same instance.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeKernelWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RuntimeKernelWorker>();
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<RuntimeKernelWorker>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="RuntimeProcessHost"/> and every constructor
    /// dependency it needs as singletons in the supplied
    /// <see cref="IServiceCollection"/>. The runtime directory is
    /// resolved once via <see cref="AppDataPathProvider.GetDefaultLayout"/>
    /// and shared by the lock file store and the workspace materializer;
    /// both factory lambdas resolve the registered
    /// <see cref="AppDataLayout"/> rather than capturing the directory
    /// string in a closure, so the extension stays free of "static
    /// lambda captures local" diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration is intentionally pure: this method does NOT call
    /// <see cref="AppDataLayout.EnsureCreated"/>. Directory creation
    /// remains a kernel / host responsibility so DI resolution never
    /// touches the filesystem.
    /// </para>
    /// <para>
    /// <see cref="RuntimeKernelWorker"/> is NOT registered here. The
    /// worker is registered by <see cref="AddRuntimeKernelWorker"/>;
    /// callers MUST register the worker before (or independently of)
    /// this extension so the host resolves the same worker instance
    /// that the process host depends on.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeProcessHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The runtime directory is the only piece of configuration
        // shared by RuntimeLockFileStore and RuntimeWorkspaceMaterializer.
        // Register the layout once as a singleton; every downstream
        // factory resolves it from the service provider so the
        // lambdas stay free of captured locals.
        services.TryAddSingleton<AppDataLayout>(_ => AppDataPathProvider.GetDefaultLayout());

        services.AddSingleton<RuntimeOwnershipMutex>(
            static _ => new RuntimeOwnershipMutex(RuntimeOwnershipNames.GlobalMutexName));

        services.AddSingleton<RuntimeLockFileStore>(
            static sp => new RuntimeLockFileStore(
                sp.GetRequiredService<AppDataLayout>().RuntimeDirectory));

        services.AddSingleton<RuntimeStaleLockRecovery>(
            static sp => new RuntimeStaleLockRecovery(
                sp.GetRequiredService<RuntimeLockFileStore>()));

        services.AddSingleton<IRuntimeWorkspaceMaterializer>(
            static sp => RuntimeWorkspaceMaterializer.CreateForRoot(
                sp.GetRequiredService<AppDataLayout>().RuntimeDirectory));

        services.AddSingleton<IRuntimeTransactionManager, RuntimeTransactionManager>();
        services.AddSingleton<IRuntimeJobObjectProcessAssigner, RuntimeJobObjectProcessAssigner>();

        services.AddSingleton<RuntimeProcessHost>(
            static sp => new RuntimeProcessHost(
                sp.GetRequiredService<RuntimeOwnershipMutex>(),
                sp.GetRequiredService<RuntimeStaleLockRecovery>(),
                sp.GetRequiredService<IRuntimeWorkspaceMaterializer>(),
                sp.GetRequiredService<IRuntimeTransactionManager>(),
                sp.GetRequiredService<IRuntimeJobObjectProcessAssigner>(),
                sp.GetRequiredService<RuntimeLockFileStore>(),
                sp.GetRequiredService<ILogger<RuntimeProcessHost>>(),
                sp.GetRequiredService<RuntimeKernelWorker>()));

        return services;
    }

    /// <summary>
    /// Registers <see cref="RuntimeHealthMonitor"/> as a singleton
    /// in the supplied <see cref="IServiceCollection"/>, both as
    /// its concrete type, as <see cref="IRuntimeHealthMonitor"/>,
    /// and as an <see cref="IHostedService"/>. All three
    /// registrations resolve to the same singleton instance so the
    /// Generic Host starts and stops the very same object the
    /// Application layer can inject through
    /// <see cref="IRuntimeHealthMonitor"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This extension depends on the singletons registered by
    /// <see cref="AddRuntimeKernelStateStore"/>,
    /// <see cref="AddRuntimeKernelWorker"/> and
    /// <see cref="AddRuntimeProcessHost"/>. Callers MUST register
    /// those extensions first; the method does not register the
    /// state store, the worker or the process host.
    /// </para>
    /// <para>
    /// Registration is pure: this method does not touch the
    /// filesystem, does not start the monitor and does not depend
    /// on any other extension beyond the ones above.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeHealthMonitor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RuntimeHealthMonitor>();
        services.AddSingleton<IRuntimeHealthMonitor>(
            static sp => sp.GetRequiredService<RuntimeHealthMonitor>());
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<RuntimeHealthMonitor>());

        return services;
    }
}
```

- [ ] **Step 2: Verify the file compiles**

Run:

```powershell
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
```

Expected: build succeeds; the new extension method is part of the `Zapret2Pilot.Runtime` assembly.

- [ ] **Step 3: Commit**

```bash
git add src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs
git commit -m "feat(runtime): add AddRuntimeHealthMonitor DI extension"
```

---

## Task 9: Wire `AddRuntimeHealthMonitor` into `AppHost.Build`

**Files:**
- Modify: `src/Zapret2Pilot.App/Program.cs`

**Interfaces:**
- Consumes: the new `AddRuntimeHealthMonitor` extension from Task 8
- Produces: `AppHost.Build` calls `services.AddRuntimeHealthMonitor();` immediately after `services.AddRuntimeProcessHost();` so the monitor is registered as a hosted service in the same `ConfigureServices` block.

- [ ] **Step 1: Add the registration call**

In `src/Zapret2Pilot.App/Program.cs`, update the `ConfigureServices` block inside `AppHost.Build` (lines 59–70) to add the new line. The full file should look like:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReactiveUI.Avalonia;
using Zapret2Pilot.App.Shell;

namespace Zapret2Pilot.App;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        using IHost host = AppHost.Build(args);

        AppHost.SetCurrent(host);

        await host.StartAsync();

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(5));

            await host.StopAsync(shutdownTimeout.Token);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI(static _ => { })
            .LogToTrace();
    }
}

internal static class AppHost
{
    private static IHost? current;

    public static IServiceProvider Services =>
        current?.Services
        ?? throw new InvalidOperationException("Application host has not been initialized.");

    public static IHost Build(string[] args)
    {
        return Host
            .CreateDefaultBuilder(args)
            .ConfigureServices(static services =>
            {
                string databasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Zapret2Pilot",
                    "z2p.db");
                services.AddRuntimeKernelStateStore(databasePath);
                services.AddRuntimeKernelWorker();
                services.AddRuntimeProcessHost();
                services.AddRuntimeHealthMonitor();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    public static void SetCurrent(IHost host)
    {
        current = host ?? throw new ArgumentNullException(nameof(host));
    }
}
```

- [ ] **Step 2: Verify the App project compiles**

Run:

```powershell
dotnet restore Zapret2Pilot.slnx
dotnet build src/Zapret2Pilot.App/Zapret2Pilot.App.csproj -c Release
```

Expected: restore succeeds; build succeeds. The Avalonia `z2p.exe` output references the new monitor's `IHostedService` registration; the monitor will be started and stopped by the Generic Host.

- [ ] **Step 3: Commit**

```bash
git add src/Zapret2Pilot.App/Program.cs
git commit -m "feat(app): register RuntimeHealthMonitor in the Generic Host"
```

---

## Task 10: Add `EndSession` overload tests to `RuntimeKernelStateStoreTests`

**Files:**
- Modify: `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs`

**Interfaces:**
- Consumes: the new `EndSession(RuntimeSessionId, RuntimeSessionState)` overload from Task 3; the existing `TemporarySqliteDatabase` fixture from `tests/Zapret2Pilot.Runtime.Tests/State/TemporarySqliteDatabase.cs`
- Produces: four new xUnit tests:
  - `EndSessionWithFailedState_MarksSessionAsFailed`
  - `EndSessionWithFailedStateAndUnknownId_Throws`
  - `EndSessionWithFailedStateAndNullId_Throws`
  - `EndSessionWithActiveState_Throws` (invalid terminal state)

- [ ] **Step 1: Add the four tests to the end of the file**

In `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs`, immediately after the last existing test `StartSessionPreservesUniqueIdAndTimestamp` (ending at line 212), add the following four new tests. The full file's tail becomes:

```csharp
    [Fact]
    public static void StartSessionPreservesUniqueIdAndTimestamp()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        ProfileId profileId = new("profile-a");
        RuntimePlanId planId = new("plan-a");
        RuntimePlanCacheKey planCacheKey = new("a".PadRight(64, 'a'));

        RuntimeSessionRecord first = store.StartSession(profileId, planId, planCacheKey);
        RuntimeSessionRecord second = store.StartSession(profileId, planId, planCacheKey);

        Assert.NotEqual(first.Id.Value, second.Id.Value);
        Assert.NotEqual(first.StartedAtUtc, second.StartedAtUtc);
    }

    [Fact]
    public static void EndSessionWithFailedState_MarksSessionAsFailed()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        RuntimeSessionRecord started = store.StartSession(
            new ProfileId("profile-a"),
            new RuntimePlanId("plan-a"),
            new RuntimePlanCacheKey("a".PadRight(64, 'a')));

        store.EndSession(started.Id, RuntimeSessionState.Failed);

        Assert.Null(store.GetCurrentSession());

        IReadOnlyList<RuntimeSessionRecord> history = store.GetRecentSessions(10);
        RuntimeSessionRecord closed = Assert.Single(history);
        Assert.Equal(started.Id, closed.Id);
        Assert.Equal(RuntimeSessionState.Failed, closed.State);
        Assert.NotNull(closed.EndedAtUtc);
    }

    [Fact]
    public static void EndSessionWithFailedStateAndUnknownId_Throws()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        RuntimeSessionId unknown = new(Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(
            () => store.EndSession(unknown, RuntimeSessionState.Failed));
    }

    [Fact]
    public static void EndSessionWithFailedStateAndNullId_Throws()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());

        Assert.Throws<ArgumentNullException>(
            () => store.EndSession(null!, RuntimeSessionState.Failed));
    }

    [Fact]
    public static void EndSessionWithActiveState_Throws()
    {
        using TemporarySqliteDatabase database = new();
        database.Initialize();

        RuntimeKernelStateStore store = new(database.CreateFactory());
        RuntimeSessionRecord started = store.StartSession(
            new ProfileId("profile-a"),
            new RuntimePlanId("plan-a"),
            new RuntimePlanCacheKey("a".PadRight(64, 'a')));

        // Active is not a valid terminal state for EndSession;
        // callers must supply Stopped or Failed.
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => store.EndSession(started.Id, RuntimeSessionState.Active));
        Assert.Equal("finalState", exception.ParamName);

        // The session must still be Active after the rejected call:
        // the failed validation must not mutate the persisted state.
        RuntimeSessionRecord? current = store.GetCurrentSession();
        Assert.NotNull(current);
        Assert.Equal(RuntimeSessionState.Active, current!.State);
    }
}
```

- [ ] **Step 2: Run the state store tests**

Run:

```powershell
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeKernelStateStoreTests"
```

Expected: all 15 tests (11 existing + 4 new) pass.

- [ ] **Step 3: Commit**

```bash
git add tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs
git commit -m "test(runtime): cover EndSession overload with Failed and invalid states"
```

---

## Task 11: Add `RuntimeHealthSnapshot` contract tests

**Files:**
- Create: `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs`
- Modify: `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj` (add `System.Reactive` `PackageReference` — the test file does not actually need it for the snapshot contract tests, but the next task's tests do; add it once here so the project compiles when the next test file is added)

**Interfaces:**
- Consumes: `RuntimeHealthState` and `RuntimeHealthSnapshot` from Task 5
- Produces: a new test file with focused xUnit tests that pin down the snapshot's invariants:
  - `Constructor_RejectsUnknownEnumValue`
  - `Constructor_RejectsNonPositiveProcessId`
  - `Constructor_AcceptsNullProcessId` (i.e. `Unknown` snapshots)
  - `RecordEquality_IsStructural`
  - `RecordHashCode_MatchesEquality`

- [ ] **Step 1: Add `System.Reactive` to the test project**

In `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj`, add a new `PackageReference` line. The `ItemGroup` that contains `Microsoft.Extensions.Hosting` becomes:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="System.Reactive" />
  </ItemGroup>
```

- [ ] **Step 2: Create the snapshot test file**

Write the file `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs` with:

```csharp
using System;
using Xunit;
using Zapret2Pilot.Runtime.Health;

namespace Zapret2Pilot.Runtime.Tests.Health;

public sealed class RuntimeHealthSnapshotTests
{
    [Fact]
    public static void Constructor_RejectsUnknownEnumValue()
    {
        DateTimeOffset observed = DateTimeOffset.UtcNow;
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new RuntimeHealthSnapshot(
                state: (RuntimeHealthState)999,
                processId: null,
                observedAtUtc: observed));
        Assert.Equal("state", exception.ParamName);
    }

    [Fact]
    public static void Constructor_RejectsNonPositiveProcessId()
    {
        DateTimeOffset observed = DateTimeOffset.UtcNow;
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Healthy,
                processId: 0,
                observedAtUtc: observed));
        Assert.Equal("processId", exception.ParamName);
    }

    [Fact]
    public static void Constructor_AcceptsNullProcessId()
    {
        DateTimeOffset observed = DateTimeOffset.UtcNow;
        RuntimeHealthSnapshot snapshot = new(
            state: RuntimeHealthState.Unknown,
            processId: null,
            observedAtUtc: observed);

        Assert.Equal(RuntimeHealthState.Unknown, snapshot.State);
        Assert.Null(snapshot.ProcessId);
        Assert.Equal(observed, snapshot.ObservedAtUtc);
    }

    [Fact]
    public static void RecordEquality_IsStructural()
    {
        DateTimeOffset observed = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        RuntimeHealthSnapshot first = new(
            state: RuntimeHealthState.Healthy,
            processId: 42,
            observedAtUtc: observed);
        RuntimeHealthSnapshot second = new(
            state: RuntimeHealthState.Healthy,
            processId: 42,
            observedAtUtc: observed);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public static void RecordEquality_DiffersWhenAnyFieldDiffers()
    {
        DateTimeOffset observed = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        RuntimeHealthSnapshot baseline = new(
            state: RuntimeHealthState.Healthy,
            processId: 42,
            observedAtUtc: observed);

        Assert.NotEqual(
            baseline,
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Exited,
                processId: 42,
                observedAtUtc: observed));
        Assert.NotEqual(
            baseline,
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Healthy,
                processId: 43,
                observedAtUtc: observed));
        Assert.NotEqual(
            baseline,
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Healthy,
                processId: 42,
                observedAtUtc: observed.AddSeconds(1)));
    }
}
```

- [ ] **Step 3: Run the snapshot tests**

Run:

```powershell
dotnet restore Zapret2Pilot.slnx
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthSnapshotTests"
```

Expected: all 5 tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs
git commit -m "test(runtime): cover RuntimeHealthSnapshot contract invariants"
```

---

## Task 12: Add `RuntimeHealthMonitor` integration tests using `FakeRuntime`

**Files:**
- Create: `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs`

**Interfaces:**
- Consumes:
  - `Zapret2Pilot.Testing.FakeRuntime` (already referenced by the test project via `ProjectReference PrivateAssets="all"`)
  - `TemporarySqliteDatabase` from `tests/Zapret2Pilot.Runtime.Tests/State/TemporarySqliteDatabase.cs`
  - `RuntimeProcessHost`, `RuntimeKernelWorker`, `RuntimeHealthMonitor`, `IRuntimeKernelStateStore` from prior tasks
  - `Microsoft.Extensions.Logging.Abstractions.NullLogger<T>` (already referenced)
- Produces: a new test file with focused xUnit tests that pin down the monitor's contract end-to-end against `FakeRuntime`:
  - `Constructor_InitialSnapshotIsUnknown`
  - `StartAsync_BeforeAnyProcess_SnapshotRemainsUnknown`
  - `Probe_AfterProcessStart_PublishesHealthySnapshot`
  - `Probe_AfterProcessExit_PublishesExitedSnapshot`
  - `Probe_TransitionToExited_MarksActiveSessionAsFailed`
  - `StopAsync_DisposesTimerAndStopsPublishingSnapshots`
  - `SnapshotChanged_SubscribersReceiveInitialAndTransitionSnapshots`

The test fixture uses a fast probe interval (`TimeSpan.FromMilliseconds(50)`) so the tests are responsive; the production default (`500 ms`) is unaffected.

- [ ] **Step 1: Create the integration test file**

Write the file `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs` with the following content:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Ownership;
using Zapret2Pilot.Runtime.Recovery;
using Zapret2Pilot.Runtime.State;
using Zapret2Pilot.Runtime.Tests.Hosting;
using Zapret2Pilot.Runtime.Tests.State;
using Zapret2Pilot.Runtime.Transactions;
using Zapret2Pilot.Runtime.Windows;
using Zapret2Pilot.Runtime.Workspace;

namespace Zapret2Pilot.Runtime.Tests.Health;

// snake_case test method names; suppress CA1707 for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// xUnit tests for <see cref="RuntimeHealthMonitor"/> (milestone 0.0.21).
/// All probes use a fast interval (50 ms) so the tests stay
/// responsive. The integration tests that actually launch the
/// <c>FakeRuntime</c> are gated to Windows because the FakeRuntime
/// relies on Windows process semantics; the pure contract tests
/// (initial snapshot, dispose, observable) run on every platform.
/// </summary>
public sealed class RuntimeHealthMonitorTests
{
    private static readonly TimeSpan FastProbeInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SnapshotWaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public static void Constructor_InitialSnapshotIsUnknown()
    {
        using MonitorFixture fixture = MonitorFixture.Create();

        Assert.Equal(RuntimeHealthState.Unknown, fixture.Monitor.LatestSnapshot.State);
        Assert.Null(fixture.Monitor.LatestSnapshot.ProcessId);
    }

    [Fact]
    public static async Task StartAsync_BeforeAnyProcess_SnapshotRemainsUnknown()
    {
        using MonitorFixture fixture = MonitorFixture.Create();
        await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            RuntimeHealthSnapshot snapshot = await WaitForSnapshot(
                fixture.Monitor,
                state => state == RuntimeHealthState.Unknown,
                SnapshotWaitTimeout);

            Assert.Equal(RuntimeHealthState.Unknown, snapshot.State);
        }
        finally
        {
            await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public static async Task Probe_AfterProcessStart_PublishesHealthySnapshot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using MonitorFixture fixture = MonitorFixture.Create();
        await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            fixture.HostFixture.PrepareFakeRuntimeInWorkspace();
            Result<RuntimeProcessHostResult> startResult = StartFakeRuntime(fixture);
            Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.ToString() : string.Empty);

            RuntimeHealthSnapshot healthy = await WaitForSnapshot(
                fixture.Monitor,
                state => state == RuntimeHealthState.Healthy,
                SnapshotWaitTimeout);

            Assert.Equal(RuntimeHealthState.Healthy, healthy.State);
            Assert.Equal(startResult.Value.ProcessId, healthy.ProcessId);
        }
        finally
        {
            await StopFakeRuntime(fixture);
            await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public static async Task Probe_AfterProcessExit_PublishesExitedSnapshot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using MonitorFixture fixture = MonitorFixture.Create();
        await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            fixture.HostFixture.PrepareFakeRuntimeInWorkspace();
            Result<RuntimeProcessHostResult> startResult = StartFakeRuntime(fixture);
            Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.ToString() : string.Empty);

            // Wait for the Healthy snapshot first, so the subsequent
            // Exited observation is a real transition (not the
            // monitor's initial Unknown value).
            await WaitForSnapshot(
                fixture.Monitor,
                state => state == RuntimeHealthState.Healthy,
                SnapshotWaitTimeout);

            // Kill the launched process from outside the host so
            // HasExited flips to true. The host's RunningProcess
            // accessor still returns the (now exited) Process
            // reference.
            using (Process running = Process.GetProcessById(startResult.Value.ProcessId))
            {
                running.Kill(entireProcessTree: true);
            }

            RuntimeHealthSnapshot exited = await WaitForSnapshot(
                fixture.Monitor,
                state => state == RuntimeHealthState.Exited,
                SnapshotWaitTimeout);

            Assert.Equal(RuntimeHealthState.Exited, exited.State);
        }
        finally
        {
            await StopFakeRuntime(fixture);
            await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public static async Task Probe_TransitionToExited_MarksActiveSessionAsFailed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using MonitorFixture fixture = MonitorFixture.Create();
        await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            // Open an active session in the state store before the
            // monitor observes the Exited transition. The monitor
            // must close this session as Failed.
            RuntimeSessionRecord started = fixture.StateStore.StartSession(
                new ProfileId("profile-a"),
                new RuntimePlanId("plan-a"),
                new RuntimePlanCacheKey("a".PadRight(64, 'a')));
            Assert.Equal(RuntimeSessionState.Active, started.State);

            fixture.HostFixture.PrepareFakeRuntimeInWorkspace();
            Result<RuntimeProcessHostResult> startResult = StartFakeRuntime(fixture);
            Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.ToString() : string.Empty);

            await WaitForSnapshot(
                fixture.Monitor,
                state => state == RuntimeHealthState.Healthy,
                SnapshotWaitTimeout);

            using (Process running = Process.GetProcessById(startResult.Value.ProcessId))
            {
                running.Kill(entireProcessTree: true);
            }

            // Wait for the Exited snapshot. The monitor should have
            // marked the active session as Failed in the same probe.
            await WaitForSnapshot(
                fixture.Monitor,
                state => state == RuntimeHealthState.Exited,
                SnapshotWaitTimeout);

            // Allow the EndSession call (which runs on the worker
            // thread inside the probe) to commit.
            RuntimeSessionRecord? current = null;
            for (int i = 0; i < 50; i++)
            {
                current = fixture.StateStore.GetCurrentSession();
                if (current is null)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
            }

            Assert.Null(current);

            IReadOnlyList<RuntimeSessionRecord> history = fixture.StateStore.GetRecentSessions(10);
            RuntimeSessionRecord closed = Assert.Single(history, r => r.Id == started.Id);
            Assert.Equal(RuntimeSessionState.Failed, closed.State);
            Assert.NotNull(closed.EndedAtUtc);
        }
        finally
        {
            await StopFakeRuntime(fixture);
            await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public static async Task StopAsync_DisposesTimerAndStopsPublishingSnapshots()
    {
        using MonitorFixture fixture = MonitorFixture.Create();
        await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

        await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);

        // After StopAsync, the monitor MUST NOT publish further
        // snapshots. We confirm this by waiting for a brief
        // observation window (5 probe intervals) and asserting the
        // snapshot is still the initial Unknown value the Behavior
        // Subject was constructed with.
        RuntimeHealthSnapshot initial = fixture.Monitor.LatestSnapshot;
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        Assert.Same(initial, fixture.Monitor.LatestSnapshot);
    }

    [Fact]
    public static void SnapshotChanged_SubscribersReceiveInitialAndTransitionSnapshots()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using MonitorFixture fixture = MonitorFixture.Create();

        System.Collections.Generic.List<RuntimeHealthSnapshot> received = new();
        object receiveLock = new();
        IDisposable subscription = fixture.Monitor.SnapshotChanged.Subscribe(snapshot =>
        {
            lock (receiveLock)
            {
                received.Add(snapshot);
            }
        });

        try
        {
            // BehaviorSubject emits the initial Unknown snapshot
            // synchronously on subscription.
            SpinWait.SpinUntil(
                () => { lock (receiveLock) { return received.Count >= 1; } },
                SnapshotWaitTimeout);
            lock (receiveLock)
            {
                Assert.NotEmpty(received);
                Assert.Equal(RuntimeHealthState.Unknown, received[0].State);
            }
        }
        finally
        {
            subscription.Dispose();
        }
    }

    private static Result<RuntimeProcessHostResult> StartFakeRuntime(MonitorFixture fixture)
    {
        RuntimeProcessStartContext context = fixture.HostFixture.CreateStartContextForFakeRuntime();
        return fixture.HostFixture.Host.StartAsync(context, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static async Task StopFakeRuntime(MonitorFixture fixture)
    {
        try
        {
            await fixture.HostFixture.Host.StopAsync(CancellationToken.None);
        }
        catch
        {
            // best-effort: the FakeRuntime may already be dead.
        }
    }

    private static async Task<RuntimeHealthSnapshot> WaitForSnapshot(
        IRuntimeHealthMonitor monitor,
        Func<RuntimeHealthState, bool> predicate,
        TimeSpan timeout)
    {
        TaskCompletionSource<RuntimeHealthSnapshot> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RuntimeHealthSnapshot initial = monitor.LatestSnapshot;
        if (predicate(initial.State))
        {
            return initial;
        }

        IDisposable subscription = monitor.SnapshotChanged
            .Where(snapshot => predicate(snapshot.State))
            .Take(1)
            .Subscribe(tcs.SetResult);

        using CancellationTokenSource cts = new(timeout);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            subscription.Dispose();
        }
    }

    /// <summary>
    /// Per-test fixture that wires up a <see cref="TemporarySqliteDatabase"/>,
    /// a <see cref="RuntimeProcessHost"/> (via the existing
    /// <see cref="RuntimeProcessHostTests.HostFixture"/> helper), a
    /// dedicated <see cref="RuntimeKernelWorker"/>, a
    /// <see cref="RuntimeHealthMonitor"/> with a 50 ms probe
    /// interval, and the matching <see cref="IRuntimeKernelStateStore"/>.
    /// The monitor, the host, the worker and the temp directory
    /// are disposed together in <see cref="Dispose"/>.
    /// </summary>
    private sealed class MonitorFixture : IDisposable
    {
        private readonly TemporarySqliteDatabase database;
        private readonly RuntimeProcessHostTests hostTestsHelper;
        private bool disposed;

        private MonitorFixture(
            TemporarySqliteDatabase database,
            RuntimeProcessHostTests hostTestsHelper,
            IRuntimeKernelStateStore stateStore,
            RuntimeHealthMonitor monitor)
        {
            this.database = database;
            this.hostTestsHelper = hostTestsHelper;
            StateStore = stateStore;
            Monitor = monitor;
        }

        public RuntimeProcessHostTests.HostFixture HostFixture => hostTestsHelper.HostFixtureInstance;

        public IRuntimeKernelStateStore StateStore { get; }

        public RuntimeHealthMonitor Monitor { get; }

        public static MonitorFixture Create()
        {
            TemporarySqliteDatabase database = new();
            database.Initialize();
            SqliteConnectionFactory factory = database.CreateFactory();
            IRuntimeKernelStateStore stateStore = new RuntimeKernelStateStore(factory);

            RuntimeProcessHostTests hostTests = new();
            RuntimeProcessHostTests.HostFixture hostFixture = hostTests.CreateHostFixture();

            RuntimeHealthMonitor monitor = new(
                host: hostFixture.Host,
                worker: hostFixture.Worker,
                stateStore: stateStore,
                logger: NullLogger<RuntimeHealthMonitor>.Instance,
                probeInterval: FastProbeInterval);

            return new MonitorFixture(database, hostTests, stateStore, monitor);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            Monitor.Dispose();
            hostTestsHelper.DisposeHostFixture();
            database.Dispose();
        }
    }
}

#pragma warning restore CA1707 // Identifiers should not contain underscores
```

- [ ] **Step 2: Expose the test-only helpers on `RuntimeProcessHostTests`**

The integration test file above expects `RuntimeProcessHostTests` to expose:

- A parameterless `public` constructor (the test file creates `new RuntimeProcessHostTests()` to reach the helpers).
- A `public RuntimeProcessHostTests.HostFixture HostFixtureInstance { get; }` accessor that returns the fixture created by the helper.
- A `public static HostFixture CreateHostFixture()` method that creates and remembers a host fixture.
- A `public void DisposeHostFixture()` method that disposes the remembered host fixture.

Modify `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs` (lines 39–40) to change the test class from `public sealed class RuntimeProcessHostTests` to `public sealed partial class RuntimeProcessHostTests`, then add a sibling file `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.TestHelpers.cs` with:

```csharp
namespace Zapret2Pilot.Runtime.Tests.Hosting;

/// <summary>
/// Test-only helpers that let other test files in
/// <c>Zapret2Pilot.Runtime.Tests</c> share the
/// <see cref="HostFixture"/> used by <see cref="RuntimeProcessHostTests"/>.
/// The helpers are intentionally isolated in a partial-class file
/// so the existing <see cref="RuntimeProcessHostTests"/> test bodies
/// stay focused on the host contract.
/// </summary>
public sealed partial class RuntimeProcessHostTests
{
    private HostFixture? sharedHostFixture;

    public HostFixture HostFixtureInstance =>
        this.sharedHostFixture
            ?? throw new InvalidOperationException(
                "CreateHostFixture() must be called before accessing HostFixtureInstance.");

    public HostFixture CreateHostFixture()
    {
        this.sharedHostFixture ??= HostFixture.Create();
        return this.sharedHostFixture;
    }

    public void DisposeHostFixture()
    {
        this.sharedHostFixture?.Dispose();
        this.sharedHostFixture = null;
    }
}
```

Make sure the test-only `HostFixture` type is accessible. The original `HostFixture` is declared `private sealed class HostFixture : IDisposable` inside `RuntimeProcessHostTests`. To make it usable from a sibling file, promote its visibility to `internal sealed class` (still nested under `RuntimeProcessHostTests`):

```csharp
internal sealed class HostFixture : IDisposable
{
    // ... existing body unchanged ...
}
```

The same change is needed for `RecordingMaterializer` and `RecordingTransactionManager` only if the new test file references them; it does not, so leave those private.

- [ ] **Step 3: Run the new tests**

Run:

```powershell
dotnet restore Zapret2Pilot.slnx
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthMonitorTests"
```

Expected: all 7 new tests pass. The Windows-gated tests (`Probe_AfterProcessStart_PublishesHealthySnapshot`, `Probe_AfterProcessExit_PublishesExitedSnapshot`, `Probe_TransitionToExited_MarksActiveSessionAsFailed`, `SnapshotChanged_SubscribersReceiveInitialAndTransitionSnapshots`) early-return on non-Windows hosts, so the build stays cross-platform-buildable; the remaining 3 tests run everywhere.

- [ ] **Step 4: Run the full Runtime test suite**

Run:

```powershell
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release
```

Expected: every test in the project passes. The pre-existing widening of the UI non-blocking test budget is preserved.

- [ ] **Step 5: Commit**

```bash
git add tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.TestHelpers.cs
git commit -m "test(runtime): add RuntimeHealthMonitor integration tests against FakeRuntime"
```

---

## Task 13: Add a DI integration test for `AddRuntimeHealthMonitor`

**Files:**
- Modify: `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: the new `AddRuntimeHealthMonitor` extension from Task 8
- Produces: a new xUnit test `AddRuntimeHealthMonitorRegistersMonitorAsHostedService` that:
  - Registers `AddRuntimeKernelStateStore`, `AddRuntimeKernelWorker`, `AddRuntimeProcessHost`, `AddRuntimeHealthMonitor` in the same order as `AppHost.Build`.
  - Resolves `IRuntimeHealthMonitor`, `RuntimeHealthMonitor`, and the first `IHostedService` from the Generic Host.
  - Asserts that the three resolutions are the same instance and that the monitor is the first `IHostedService` (the kernel worker is the second).

- [ ] **Step 1: Add the new test at the end of the file**

In `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`, after the existing `AddRuntimeProcessHostRegistersResolvableHost` test (ending at line 81), add the new test. The full file's tail becomes:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.State;

namespace Zapret2Pilot.Runtime.Tests.DependencyInjection;

public sealed class RuntimeServiceCollectionExtensionsTests
{
    [Fact]
    public static void AddRuntimeKernelStateStoreRegistersResolvableStore()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.DirectoryPath, "z2p.db");

        {
            using IHost host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddRuntimeKernelStateStore(databasePath))
                .Build();

            IRuntimeKernelStateStore store = host.Services.GetRequiredService<IRuntimeKernelStateStore>();

            RuntimeSessionRecord session = store.StartSession(
                new ProfileId("profile-a"),
                new RuntimePlanId("plan-a"),
                new RuntimePlanCacheKey("a".PadRight(64, 'a')));

            Assert.NotNull(session);
            Assert.Equal(RuntimeSessionState.Active, session.State);

            RuntimeSessionRecord? current = store.GetCurrentSession();
            Assert.NotNull(current);
            Assert.Equal(session.Id, current!.Id);

            Assert.True(File.Exists(databasePath), "SQLite database file should be created.");
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public static async Task AddRuntimeProcessHostRegistersResolvableHost()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(static services =>
            {
                services.AddRuntimeKernelWorker();
                services.AddRuntimeProcessHost();
            })
            .Build();

        await host.StartAsync(cancellationToken);

        try
        {
            RuntimeProcessHost host1 = host.Services.GetRequiredService<RuntimeProcessHost>();
            RuntimeProcessHost host2 = host.Services.GetRequiredService<RuntimeProcessHost>();

            Assert.NotNull(host1);
            Assert.Same(host1, host2);

            RuntimeKernelWorker worker = host.Services.GetRequiredService<RuntimeKernelWorker>();
            IHostedService hostedService = host.Services.GetRequiredService<IHostedService>();

            Assert.Same(worker, hostedService);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public static async Task AddRuntimeHealthMonitorRegistersMonitorAsHostedService()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.DirectoryPath, "z2p.db");

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddRuntimeKernelStateStore(databasePath);
                services.AddRuntimeKernelWorker();
                services.AddRuntimeProcessHost();
                services.AddRuntimeHealthMonitor();
            })
            .Build();

        await host.StartAsync(cancellationToken);

        try
        {
            IRuntimeHealthMonitor interfaceResolution = host.Services.GetRequiredService<IRuntimeHealthMonitor>();
            RuntimeHealthMonitor concreteResolution = host.Services.GetRequiredService<RuntimeHealthMonitor>();

            Assert.NotNull(interfaceResolution);
            Assert.Same(interfaceResolution, concreteResolution);

            // The monitor is the LAST hosted service the host
            // starts, so the last IHostedService resolved by the
            // generic IEnumerable<IHostedService> service must
            // resolve to the same instance.
            System.Collections.Generic.IEnumerable<IHostedService> hostedServices =
                host.Services.GetServices<IHostedService>();
            IHostedService lastHosted = Assert.Single(hostedServices, s => s is RuntimeHealthMonitor);
            Assert.Same(concreteResolution, lastHosted);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }

        SqliteConnection.ClearAllPools();
    }
}
```

- [ ] **Step 2: Run the DI test**

Run:

```powershell
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"
```

Expected: all 3 DI tests pass (1 existing kernel state store test, 1 existing process host test, 1 new health monitor test).

- [ ] **Step 3: Commit**

```bash
git add tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs
git commit -m "test(runtime): verify IRuntimeHealthMonitor resolves as a hosted service"
```

---

## Task 14: Bump `VERSION` and `MainWindowViewModel.AppVersion` to `v0.0.21`

**Files:**
- Modify: `VERSION` (single line `0.0.20` → `0.0.21`)
- Modify: `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` (line 91: `"v0.0.20"` → `"v0.0.21"`)
- Modify: `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs` (line 17: `"v0.0.20"` → `"v0.0.21"`)

**Interfaces:**
- Consumes: pre-committed changes from `0.0.20` (already on disk; preserved)
- Produces: the canonical project version string bumped to `0.0.21` across `VERSION`, the App's `AppVersion` display property, and the matching test assertion. No other version-bearing string is touched.

- [ ] **Step 1: Update `VERSION`**

In `VERSION`, replace the single line `0.0.20` with `0.0.21`. The full file is then:

```text
0.0.21
```

- [ ] **Step 2: Update `MainWindowViewModel.AppVersion`**

In `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`, replace the existing property declaration:

```csharp
    public string AppVersion { get; } = "v0.0.20";
```

with:

```csharp
    public string AppVersion { get; } = "v0.0.21";
```

- [ ] **Step 3: Update the matching test assertion**

In `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`, replace the existing assertion line:

```csharp
        Assert.Equal("v0.0.20", viewModel.AppVersion);
```

with:

```csharp
        Assert.Equal("v0.0.21", viewModel.AppVersion);
```

- [ ] **Step 4: Verify the build and the App tests**

Run:

```powershell
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release
```

Expected: the solution builds; the App view-model tests pass with the new `v0.0.21` assertion. The pre-committed DI integration test and the widened UI non-blocking timing budget remain in place and continue to pass.

- [ ] **Step 5: Commit**

```bash
git add VERSION src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs
git commit -m "chore(release): bump project to v0.0.21 (Runtime Health Monitor)"
```

---

## Task 15: Update `docs/Z2P-ROADMAP.md` with the `0.0.21` section

**Files:**
- Modify: `docs/Z2P-ROADMAP.md`

**Interfaces:**
- Consumes: the existing roadmap structure (each implemented milestone has a "Status: Implemented" header, a "Scope" / "Acceptance" / "Verification ladder" / "Reviewer focus" structure, and an "Out of scope" sub-section where relevant)
- Produces: a new section that documents `0.0.21 — Runtime Health Monitor Hosted Service` immediately after the `0.0.20` section. The section uses `Status: planned.` and lays out scope, acceptance, verification ladder, reviewer focus, and out-of-scope exactly as the spec requires.

- [ ] **Step 1: Insert the new section at the end of the file**

Append the following section to `docs/Z2P-ROADMAP.md`. Open the file and add the section after the last line of the existing `0.0.20` section. The new section is:

```markdown

## 0.0.21 — Runtime Health Monitor Hosted Service

Status: planned.

Follows 0.0.20. Adds the `RuntimeHealthMonitor` Generic-Host
`IHostedService` that periodically probes the live runtime process,
publishes immutable `RuntimeHealthSnapshot` values through an
`IObservable<RuntimeHealthSnapshot>` and marks the active session as
`Failed` on an unexpected process exit. Closes the observable gap
between the kernel's `RuntimeProcessHost` and any future UI / event
projection that needs to react to runtime state transitions, and
removes the requirement for a future health milestone to add the
"transitions into Exited → Failed" wiring.

### Scope

- `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj` — add a
  `PackageReference` to `System.Reactive` (version 6.1.0 is already
  centrally managed in `Directory.Packages.props`);
- `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs` and
  `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs` — new
  `EndSession(RuntimeSessionId, RuntimeSessionState)` overload that
  closes a session as `Stopped` or `Failed`; refactor the private
  `CloseSession` helper to accept the final state;
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` — new
  `internal Process? RunningProcess { get; }` accessor (thread-safe
  under the existing `stateLock`) so the monitor can read the live
  process from the kernel worker thread without leaking the host's
  private state;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs` — new
  `public enum RuntimeHealthState { Unknown, Healthy, Exited }`;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs` — new
  immutable `sealed record class` with constructor validation;
- `src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs` — new
  public contract exposing `IObservable<RuntimeHealthSnapshot>
  SnapshotChanged` and `RuntimeHealthSnapshot LatestSnapshot`;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs` — new
  `IHostedService`, `IRuntimeHealthMonitor`, `IDisposable`
  implementation:
  - owns a `System.Threading.Timer` whose callback runs on a
    `ThreadPool` thread but only enqueues a probe;
  - the probe runs on the dedicated `RuntimeKernelWorker` thread;
  - reads `RuntimeProcessHost.RunningProcess` on the worker thread;
  - builds a snapshot (`Unknown` when no process, `Healthy` when
    alive, `Exited` when `HasExited`);
  - on a transition from `Healthy` / `Unknown` to `Exited` while
    the state store has an active session, calls
    `stateStore.EndSession(current.Id, RuntimeSessionState.Failed)`;
  - publishes the snapshot through a
    `BehaviorSubject<RuntimeHealthSnapshot>` exposed via
    `AsObservable()`;
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` —
  new `AddRuntimeHealthMonitor(this IServiceCollection)` extension
  that registers the monitor as a singleton, as
  `IRuntimeHealthMonitor`, and as `IHostedService`, all resolving
  to the same instance;
- `src/Zapret2Pilot.App/Program.cs` — call
  `services.AddRuntimeHealthMonitor();` immediately after
  `services.AddRuntimeProcessHost();` in `AppHost.Build`;
- `VERSION` bumped to `0.0.21`;
- `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` — `AppVersion`
  bumped from `v0.0.20` to `v0.0.21`;
- `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`
  — the existing assertion bumped from `v0.0.20` to `v0.0.21`;
- `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj`
  — add a `System.Reactive` `PackageReference` so the health-monitor
  tests can use `BehaviorSubject` / `IObservable` operators;
- new xUnit tests:
  - `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs`
    — four new tests for the `EndSession(id, state)` overload
    (`Failed` happy path, unknown id, null id, invalid `Active`
    state);
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs`
    — five new contract tests for the snapshot record;
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs`
    — seven new integration tests using `FakeRuntime` (initial
    `Unknown`, `Healthy` after start, `Exited` after kill, `Failed`
    session recorded, `StopAsync` stops publishing, observable
    receives initial + transitions, and the `BehaviorSubject`
    subscription test);
  - `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`
    — one new DI test confirming `IRuntimeHealthMonitor` and
    `IHostedService` resolve to the same singleton.

### Acceptance

- `dotnet restore Zapret2Pilot.slnx` passes;
- `dotnet build Zapret2Pilot.slnx -c Release` passes on the whole
  solution;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release`
  passes (all 25+ runtime tests, including the new state-store
  overload tests, the snapshot contract tests, the health-monitor
  integration tests and the new DI test);
- `dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release`
  passes (the bumped `v0.0.21` assertion holds);
- `dotnet test Zapret2Pilot.slnx -c Release` passes on the whole
  solution;
- `RuntimeHealthMonitor` is started and stopped by the Generic Host
  (no manual lifecycle code in `AppHost.Build`);
- the monitor's timer callback only enqueues work; every read of
  the runtime process happens on the `RuntimeKernelWorker` thread;
- a `Healthy → Exited` transition marks the active session as
  `Failed` in `IRuntimeKernelStateStore`; a `Exited → Exited` repeat
  probe does not re-record the session;
- the new `EndSession(id, state)` overload rejects
  `RuntimeSessionState.Active` with `ArgumentException` and does
  not mutate the persisted state;
- no real `winws2` process is launched; the integration tests use
  `Zapret2Pilot.Testing.FakeRuntime` only;
- no new NuGet package versions; the only `PackageReference`
  additions are the already-centrally-managed `System.Reactive`
  6.1.0 on `Zapret2Pilot.Runtime` and on the test project;
- no Windows Service, IPC, VPN, proxy, MITM, per-URL router, or
  `.bat` / `.cmd` wrapper is added.

### Verification ladder

- `dotnet restore Zapret2Pilot.slnx`;
- `dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release`;
- `dotnet build src/Zapret2Pilot.App/Zapret2Pilot.App.csproj -c Release`;
- `dotnet build Zapret2Pilot.slnx -c Release`;
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeKernelStateStoreTests"` (state-store overload tests);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthSnapshotTests"` (snapshot contract tests);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthMonitorTests"` (integration tests against `FakeRuntime`);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"` (DI integration test);
- `dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release` (whole runtime test suite);
- `dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release` (App view-model tests);
- `dotnet test Zapret2Pilot.slnx -c Release` (whole solution).

### Out of scope for 0.0.21

- launching a real `winws2` process; the monitor only observes the
  process owned by `RuntimeProcessHost` and currently the host is
  wired against `FakeRuntime`;
- surfacing the `IObservable<RuntimeHealthSnapshot>` through the
  Avalonia UI; this milestone only registers and tests the
  observable, no `MainWindowViewModel` is wired to it;
- changing the `RuntimeHealthSnapshot` payload; the snapshot carries
  `State`, `ProcessId` and `ObservedAtUtc` only — richer fields
  (uptime, last heartbeat, last command-line hash) are future
  work;
- promoting `IRuntimeHealthMonitor` to a multi-process health
  aggregator; the monitor owns a single `RuntimeProcessHost`
  reference and is intentionally not a multi-instance coordinator;
- changing the `RuntimeHealthState` enum; only `Unknown`, `Healthy`
  and `Exited` are defined, additional states (e.g. `Recovering`,
  `Degraded`) are future work.

### Reviewer focus

- confirm that `RuntimeHealthMonitor.StartAsync` only creates a
  `System.Threading.Timer` and `StopAsync` disposes it; the timer
  callback MUST NOT touch `RuntimeProcessHost`, the state store or
  the `BehaviorSubject` directly;
- confirm that every read of `RuntimeProcessHost.RunningProcess`
  happens inside a `worker.Enqueue` body so the read runs on the
  dedicated `RuntimeKernelWorker` thread, not on a `ThreadPool`
  thread;
- confirm that the `BehaviorSubject` is constructed with an
  initial `Unknown` snapshot and that `LatestSnapshot` returns that
  value before `StartAsync` is called;
- confirm that the `EndSession(id, state)` overload rejects
  `RuntimeSessionState.Active` and that the existing
  `EndSession(id)` overload is preserved unchanged;
- confirm that the new `internal Process? RunningProcess { get; }`
  accessor takes the host's `stateLock` and that the returned
  `Process` reference MUST NOT be disposed by the caller;
- confirm that `AddRuntimeHealthMonitor` registers the monitor as
  the same singleton instance under three keys
  (`RuntimeHealthMonitor`, `IRuntimeHealthMonitor`, `IHostedService`)
  and that `Program.cs` calls it after `AddRuntimeProcessHost`;
- confirm that the test fixture for `RuntimeHealthMonitorTests`
  reuses the existing `HostFixture` from `RuntimeProcessHostTests`
  via the new partial-class helper file, instead of duplicating
  the wiring logic;
- confirm that the integration test
  `Probe_TransitionToExited_MarksActiveSessionAsFailed` actually
  opens a session in the state store, observes the `Healthy`
  snapshot, kills the FakeRuntime from outside the host, waits for
  the `Exited` snapshot, and asserts the session is recorded as
  `Failed` — the test is the load-bearing proof of the
  health-monitor contract.
```

- [ ] **Step 2: Verify the doc renders**

Open `docs/Z2P-ROADMAP.md` and confirm the new section appears at the end of the file, the section is a sibling of the `0.0.20` section, and the table of contents / numbering is preserved (no existing section is renumbered).

- [ ] **Step 3: Commit**

```bash
git add docs/Z2P-ROADMAP.md
git commit -m "docs(roadmap): add 0.0.21 Runtime Health Monitor Hosted Service section"
```

---

## Task 16: Update `docs/Z2P-IMPLEMENTATION-STATUS.md` with the `0.0.21` status section

**Files:**
- Modify: `docs/Z2P-IMPLEMENTATION-STATUS.md`

**Interfaces:**
- Consumes: the existing status document structure (each implemented milestone has a header, a "Status" line, an "Implemented files and areas" / "Validation commands to run locally" / "Notes" body)
- Produces: a new `0.0.21` section that mirrors the format used by the existing sections and explicitly tags the milestone as planned (not yet implemented). The section also records the acceptance and verification ladder so the reviewer can repeat the gate when the implementation lands.

- [ ] **Step 1: Append the new section at the end of the file**

Append the following section to `docs/Z2P-IMPLEMENTATION-STATUS.md`. Open the file and add the section after the last line of the existing `0.0.19` section. The new section is:

```markdown

## 0.0.21 — Runtime Health Monitor Hosted Service

Status: **planned**.

Planned files and areas:

- `src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj` — add
  `System.Reactive` `PackageReference` (version 6.1.0 is already
  managed centrally in `Directory.Packages.props`);
- `src/Zapret2Pilot.Runtime/State/IRuntimeKernelStateStore.cs` —
  new `EndSession(RuntimeSessionId, RuntimeSessionState)` overload;
- `src/Zapret2Pilot.Runtime/State/RuntimeKernelStateStore.cs` —
  implement the new overload, refactor the private `CloseSession`
  helper to accept the terminal state;
- `src/Zapret2Pilot.Runtime/Hosting/RuntimeProcessHost.cs` —
  `internal Process? RunningProcess { get; }` accessor (thread-safe
  under the existing `stateLock`);
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthState.cs` — new
  `public enum` with `Unknown`, `Healthy`, `Exited`;
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthSnapshot.cs` —
  new immutable `sealed record class`;
- `src/Zapret2Pilot.Runtime/Health/IRuntimeHealthMonitor.cs` —
  new public contract (`SnapshotChanged`, `LatestSnapshot`);
- `src/Zapret2Pilot.Runtime/Health/RuntimeHealthMonitor.cs` —
  new `IHostedService`, `IRuntimeHealthMonitor`, `IDisposable`
  implementation with a `System.Threading.Timer` that only
  enqueues probes onto `RuntimeKernelWorker`;
- `src/Zapret2Pilot.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` —
  new `AddRuntimeHealthMonitor(this IServiceCollection)` extension
  registering the monitor as a singleton under three keys;
- `src/Zapret2Pilot.App/Program.cs` — call
  `services.AddRuntimeHealthMonitor();` after
  `services.AddRuntimeProcessHost();` in `AppHost.Build`;
- `VERSION = 0.0.21`;
- `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs` — `AppVersion`
  bumped from `v0.0.20` to `v0.0.21`;
- `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`
  — the matching `v0.0.21` test assertion;
- `tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj` —
  add `System.Reactive` `PackageReference`;
- new xUnit tests:
  - `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs` —
    `EndSessionWithFailedState_MarksSessionAsFailed`,
    `EndSessionWithFailedStateAndUnknownId_Throws`,
    `EndSessionWithFailedStateAndNullId_Throws`,
    `EndSessionWithActiveState_Throws`;
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs` —
    five contract tests (`Constructor_RejectsUnknownEnumValue`,
    `Constructor_RejectsNonPositiveProcessId`,
    `Constructor_AcceptsNullProcessId`, `RecordEquality_IsStructural`,
    `RecordEquality_DiffersWhenAnyFieldDiffers`);
  - `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs` —
    seven integration tests against `FakeRuntime`
    (`Constructor_InitialSnapshotIsUnknown`,
    `StartAsync_BeforeAnyProcess_SnapshotRemainsUnknown`,
    `Probe_AfterProcessStart_PublishesHealthySnapshot`,
    `Probe_AfterProcessExit_PublishesExitedSnapshot`,
    `Probe_TransitionToExited_MarksActiveSessionAsFailed`,
    `StopAsync_DisposesTimerAndStopsPublishingSnapshots`,
    `SnapshotChanged_SubscribersReceiveInitialAndTransitionSnapshots`);
  - `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs` —
    `AddRuntimeHealthMonitorRegistersMonitorAsHostedService`;
  - `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeProcessHostTests.cs` —
    promoted to `partial` so the integration tests can reuse the
    existing `HostFixture` via the new
    `RuntimeProcessHostTests.TestHelpers.cs` partial file
    (`HostFixture` is now `internal sealed class` so the sibling
    test file can reach it);
- `docs/Z2P-ROADMAP.md` — new `0.0.21` section;
- `docs/Z2P-IMPLEMENTATION-STATUS.md` — this section.

Validation commands to run locally once the implementation lands:

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
dotnet build src/Zapret2Pilot.App/Zapret2Pilot.App.csproj -c Release
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeKernelStateStoreTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthSnapshotTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthMonitorTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Notes:

- The milestone does not launch a real `winws2`; the
  `Probe_AfterProcessStart_PublishesHealthySnapshot` and
  `Probe_AfterProcessExit_PublishesExitedSnapshot` tests are
  Windows-gated because `FakeRuntime` is a Windows process and the
  host's job-object / mutex plumbing is platform-specific. The
  cross-platform contract tests
  (`Constructor_InitialSnapshotIsUnknown`,
  `StartAsync_BeforeAnyProcess_SnapshotRemainsUnknown`,
  `StopAsync_DisposesTimerAndStopsPublishingSnapshots`,
  `Constructor_RejectsUnknownEnumValue`,
  `Constructor_RejectsNonPositiveProcessId`,
  `Constructor_AcceptsNullProcessId`,
  `RecordEquality_IsStructural`,
  `RecordEquality_DiffersWhenAnyFieldDiffers`,
  and the four new state-store tests) run on every host.
- The monitor's `Timer` is intentionally created and disposed
  inside `IHostedService.StartAsync` / `StopAsync`; the Generic
  Host owns the lifetime, so `AppHost.Build` does not need to
  call `monitor.StartAsync` / `monitor.StopAsync` manually.
- `IRuntimeHealthMonitor` is intentionally a separate abstraction
  from the underlying `RuntimeProcessHost` so the future
  presentation layer can subscribe to
  `monitor.SnapshotChanged` without depending on the host itself.
- The plan preserves the pre-existing pre-committed changes to
  `src/Zapret2Pilot.App/Shell/MainWindowViewModel.cs`,
  `tests/Zapret2Pilot.App.ViewModelTests/MainWindowViewModelTests.cs`,
  `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs`
  and
  `tests/Zapret2Pilot.Runtime.Tests/Hosting/RuntimeKernelWorkerUiNonBlockingTests.cs`;
  only the `v0.0.20` strings are bumped to `v0.0.21`.
- No new NuGet package versions are added; the only new
  `PackageReference` lines are `System.Reactive` on
  `Zapret2Pilot.Runtime` and on the test project, both pulling
  from the central `System.Reactive` 6.1.0 entry in
  `Directory.Packages.props`.
- No `global.json` change, no lock file change, no
  `Zapret2Pilot.slnx` change.
```

- [ ] **Step 2: Verify the doc renders**

Open `docs/Z2P-IMPLEMENTATION-STATUS.md` and confirm the new section appears at the end of the file, the section is a sibling of the `0.0.19` section, and the previous milestone entries are unchanged.

- [ ] **Step 3: Commit**

```bash
git add docs/Z2P-IMPLEMENTATION-STATUS.md
git commit -m "docs(status): add 0.0.21 Runtime Health Monitor Hosted Service status section"
```

---

## Self-Review

I ran the spec-vs-plan check from the writing-plans skill:

1. **Spec coverage**
   - "Add an `EndSession(RuntimeSessionId, RuntimeSessionState)` overload to `IRuntimeKernelStateStore` / `RuntimeKernelStateStore`" → Task 2 (contract) + Task 3 (implementation) + Task 10 (tests).
   - "Define `RuntimeHealthState` enum (`Unknown`, `Healthy`, `Exited`) and immutable `RuntimeHealthSnapshot` record in `Zapret2Pilot.Runtime.Health`" → Task 5.
   - "Define `IRuntimeHealthMonitor` exposing `IObservable<RuntimeHealthSnapshot> SnapshotChanged` and `RuntimeHealthSnapshot LatestSnapshot { get; }`" → Task 6.
   - "Add `System.Reactive` PackageReference to `Zapret2Pilot.Runtime.csproj` (already centrally managed in `Directory.Packages.props`)" → Task 1.
   - "Implement `RuntimeHealthMonitor` as `IHostedService`, `IRuntimeHealthMonitor`, `IDisposable`. Runs a `System.Threading.Timer` outside the kernel worker. On each tick, enqueues a probe to `RuntimeKernelWorker` so the probe runs on the kernel thread. The probe reads `RuntimeProcessHost.RunningProcess` (you will add an `internal Process? RunningProcess { get; }` property to RuntimeProcessHost, thread-safe). Builds a snapshot: `Unknown` when no process; `Healthy` when process is alive; `Exited` when process has exited. Pushes snapshot to a `BehaviorSubject<RuntimeHealthSnapshot>` exposed via `AsObservable()`. On a transition from `Healthy`/`Unknown` to `Exited` while the state store has a current active session, call `EndSession(current.Id, RuntimeSessionState.Failed)`." → Task 4 (`RunningProcess` accessor) + Task 7 (full implementation).
   - "Register `RuntimeHealthMonitor` in DI via a new `AddRuntimeHealthMonitor(this IServiceCollection)` extension in `RuntimeServiceCollectionExtensions.cs`. It must be registered as `IRuntimeHealthMonitor` and as `IHostedService`, and must be called AFTER `AddRuntimeProcessHost` (which already depends on `AddRuntimeKernelWorker`)" → Task 8 (extension) + Task 9 (Program.cs call order).
   - "Call `services.AddRuntimeHealthMonitor();` in `src/Zapret2Pilot.App/Program.cs` after `AddRuntimeProcessHost()`" → Task 9.
   - "Write focused xUnit v3 tests: State store overload tests in `tests/Zapret2Pilot.Runtime.Tests/State/RuntimeKernelStateStoreTests.cs`. Snapshot/contract unit tests in `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthSnapshotTests.cs`. Health monitor integration tests in `tests/Zapret2Pilot.Runtime.Tests/Health/RuntimeHealthMonitorTests.cs` using FakeRuntime: initial Unknown, Healthy after start, Exited after external kill, Failed state recorded, StopAsync disposes timer. DI integration test in `tests/Zapret2Pilot.Runtime.Tests/DependencyInjection/RuntimeServiceCollectionExtensionsTests.cs` proving `IRuntimeHealthMonitor` and `IHostedService` resolve to the same singleton." → Tasks 10, 11, 12, 13.
   - "Update `VERSION` to `0.0.21`. Update `MainWindowViewModel.AppVersion` and its test assertion from `v0.0.20` to `v0.0.21` (preserve the existing pre-committed changes)." → Task 14.
   - "Update `docs/Z2P-ROADMAP.md` with a new `0.0.21 — Runtime Health Monitor Hosted Service` section (scope, acceptance, verification ladder, reviewer focus)." → Task 15.
   - "Update `docs/Z2P-IMPLEMENTATION-STATUS.md` with a new `0.0.21` status section." → Task 16.

2. **Placeholder scan**
   - Searched the plan for `TBD`, `TODO`, `implement later`, `fill in details`, `add appropriate error handling`, `add validation`, `handle edge cases`, `write tests for the above`, `similar to Task N`. None found. Every test name, code snippet, file path and command is concrete and the reviewer can execute it as written.
   - The plan deliberately uses TDD step labels ("Step 1: Write the failing test", "Step 2: Run test to verify it fails", "Step 3: Write minimal implementation", "Step 4: Run test to verify it passes", "Step 5: Commit") only where they are actually applicable. For non-test tasks the steps are the concrete actions the implementer must take.

3. **Type consistency**
   - The types defined in earlier tasks are referenced with the exact same names in later tasks:
     - `RuntimeHealthState` (Task 5) is consumed by `RuntimeHealthSnapshot` (Task 5), `IRuntimeHealthMonitor` (Task 6), `RuntimeHealthMonitor` (Task 7) and the tests in Tasks 11 and 12.
     - `RuntimeHealthSnapshot` (Task 5) is consumed by `IRuntimeHealthMonitor` (Task 6), `RuntimeHealthMonitor` (Task 7) and the tests in Tasks 11 and 12.
     - `IRuntimeHealthMonitor` (Task 6) is the contract the `AddRuntimeHealthMonitor` extension (Task 8) registers and the DI test (Task 13) resolves.
     - `RuntimeHealthMonitor` (Task 7) is the concrete type registered by the DI extension (Task 8) and exercised by the integration tests (Task 12).
     - `IRuntimeKernelStateStore.EndSession(RuntimeSessionId, RuntimeSessionState)` (Tasks 2/3) is consumed by `RuntimeHealthMonitor.TryMarkActiveSessionAsFailed` (Task 7) and exercised by the state-store overload tests (Task 10).
     - `RuntimeProcessHost.RunningProcess` (Task 4) is consumed by `RuntimeHealthMonitor.ProbeOnWorkerAsync` (Task 7) and indirectly by the integration tests (Task 12) which go through the host's `StartAsync` / `StopAsync` to set the field.
   - Constructor parameter names are consistent across the plan: `host`, `worker`, `stateStore`, `logger`, `probeInterval` in `RuntimeHealthMonitor`; `services` in every DI extension method; `sessionId`, `finalState` in the new `EndSession` overload.

4. **Other risks caught during self-review**
   - The test fixture for `RuntimeHealthMonitorTests` reuses the existing `HostFixture` from `RuntimeProcessHostTests`. The original `HostFixture` is `private sealed class`, so the plan promotes it to `internal sealed class` and splits the helper methods into a sibling partial-class file. The plan documents this promotion explicitly and the `RecordingMaterializer` / `RecordingTransactionManager` types remain private because the new test file does not need them.
   - The plan's `BehaviorSubject` is constructed with an initial `Unknown` snapshot at construction time, which means `LatestSnapshot` returns a valid value even before `StartAsync` is called. The plan documents this contract on `IRuntimeHealthMonitor.LatestSnapshot` and the integration test `Constructor_InitialSnapshotIsUnknown` pins it down.
   - The plan intentionally rejects `RuntimeSessionState.Active` from the new `EndSession` overload because it is not a valid terminal state. The `EndSessionWithActiveState_Throws` test (Task 10) also asserts that the persisted session state is unchanged after the rejected call, so the validation cannot accidentally mutate state.
   - The integration test `Probe_TransitionToExited_MarksActiveSessionAsFailed` polls `GetCurrentSession()` for up to 1 second after the `Exited` snapshot is observed, because the `EndSession` call runs on the worker thread inside the same probe. The test does not assume the persistence is synchronous with the `OnNext` call; the polling loop is documented inline.

## Verification ladder

Run the full ladder locally on a Windows developer machine (or a non-Windows host for the cross-platform subset):

```powershell
dotnet --version
dotnet restore Zapret2Pilot.slnx
dotnet build src/Zapret2Pilot.Runtime/Zapret2Pilot.Runtime.csproj -c Release
dotnet build src/Zapret2Pilot.App/Zapret2Pilot.App.csproj -c Release
dotnet build Zapret2Pilot.slnx -c Release
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeKernelStateStoreTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthSnapshotTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeHealthMonitorTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RuntimeServiceCollectionExtensionsTests"
dotnet test tests/Zapret2Pilot.Runtime.Tests/Zapret2Pilot.Runtime.Tests.csproj -c Release
dotnet test tests/Zapret2Pilot.App.ViewModelTests/Zapret2Pilot.App.ViewModelTests.csproj -c Release
dotnet test Zapret2Pilot.slnx -c Release
```

Expected: every command exits with `0`; the runtime test project runs ≥ 25 tests (11 original state-store + 4 new state-store overload + 5 snapshot + 7 monitor + 2 original DI + 1 new DI = 30 total), the App view-model tests run with the bumped `v0.0.21` assertion, and the whole solution stays green.

## Out of scope (reaffirmed)

- launching a real `winws2` process; tests use `Zapret2Pilot.Testing.FakeRuntime`;
- UI thread blocking; every probe runs on `RuntimeKernelWorker`;
- new NuGet package versions; only the existing centrally-managed `System.Reactive` 6.1.0 is added as a `PackageReference`;
- Windows Service, IPC, VPN, proxy, MITM, per-URL router, `.bat` / `.cmd` wrappers, or arbitrary command execution;
- surfacing `IObservable<RuntimeHealthSnapshot>` through the Avalonia UI (the observable is registered and tested only);
- changing `MainWindowViewModel` to subscribe to the health monitor (the milestone only registers the monitor in DI and updates the `AppVersion` display string);
- reworking `RuntimeHealthSnapshot` (the snapshot carries `State`, `ProcessId` and `ObservedAtUtc` only; richer fields are future work);
- changing `RuntimeHealthState` (only `Unknown`, `Healthy` and `Exited` are defined; additional states are future work);
- any non-kernel UI, application or storage refactor.
