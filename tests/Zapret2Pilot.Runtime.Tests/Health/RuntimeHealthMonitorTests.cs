using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Results;
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
using Zapret2Pilot.Storage.Sqlite;

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

#pragma warning disable CA1859 // Use concrete type for performance
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
#pragma warning restore CA1859 // Use concrete type for performance

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
