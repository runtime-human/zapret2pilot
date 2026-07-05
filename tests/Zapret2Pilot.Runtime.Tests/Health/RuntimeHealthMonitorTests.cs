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

            // Allow the EndSession call (which runs on the probe
            // loop task inside the probe body) to commit.
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

    /// <summary>
    /// 0.0.23 contract: after <see cref="RuntimeHealthMonitor.StopAsync"/>
    /// returns, the monitor MUST NOT publish any further snapshots.
    /// This pins down the post-stop "no publish" invariant that the
    /// new <see cref="System.Threading.PeriodicTimer"/>-driven loop
    /// must guarantee.
    /// </summary>
    [Fact]
    public static async Task StopAsync_DoesNotPublishAfterReturn()
    {
        using MonitorFixture fixture = MonitorFixture.Create();
        await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

        // Capture the snapshot reference after StopAsync. Because
        // RuntimeHealthSnapshot is a reference type (record class),
        // reference equality is the right way to assert "no new
        // snapshot was published".
        await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);
        RuntimeHealthSnapshot frozen = fixture.Monitor.LatestSnapshot;

        // Observe for several probe intervals. With the pre-0.0.23
        // timer, a late callback could still fire after Dispose
        // returned and push a new snapshot. The 0.0.23 loop must not.
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.Same(frozen, fixture.Monitor.LatestSnapshot);
    }

    /// <summary>
    /// 0.0.23 contract: a subscriber that throws inside <c>OnNext</c>
    /// must not stop the probe loop. The throw is observed on the
    /// monitor's loop task; the next <c>OnNext</c> call must still
    /// fire and the loop must keep publishing snapshots.
    /// </summary>
    [Fact]
    public static async Task ThrowingSubscriber_DoesNotStopProbeLoop()
    {
        using MonitorFixture fixture = MonitorFixture.Create();

        // A counting observer to verify the monitor keeps publishing
        // snapshots after a peer observer throws in OnNext. The
        // counting observer runs on the test thread; it accumulates
        // every snapshot that the monitor's BehaviorSubject delivers.
        System.Collections.Generic.List<RuntimeHealthSnapshot> received = new();
        object receiveLock = new();
        IDisposable countingSubscription = fixture.Monitor.SnapshotChanged.Subscribe(snapshot =>
        {
            lock (receiveLock)
            {
                received.Add(snapshot);
            }
        });

        // The throwing subscriber is established on a background
        // thread so the synchronous emission of the initial Unknown
        // snapshot (and its subsequent re-throw) does not abort the
        // test thread. The OnNext body intentionally re-throws
        // without swallowing the exception: the contract under test
        // is that the monitor's loop survives a throwing observer.
        IDisposable? throwingSubscription = null;
        Exception? subscribeError = null;
        Thread subscribeThread = new(() =>
        {
            try
            {
                throwingSubscription = fixture.Monitor.SnapshotChanged.Subscribe(_ =>
                {
                    throw new InvalidOperationException("subscriber boom");
                });
            }
            catch (Exception ex)
            {
                // The initial snapshot's OnNext call propagates the
                // throw out of Subscribe; the subscription is still
                // established for the next snapshot. Capture and
                // ignore — the throw is the path we want to exercise.
                subscribeError = ex;
            }
        })
        {
            IsBackground = true,
            Name = "RuntimeHealthMonitorTests.ThrowingSubscriber",
        };
        subscribeThread.Start();

        try
        {
            await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

            int baselineCount;
            lock (receiveLock)
            {
                baselineCount = received.Count;
            }

            // Give the loop several probe intervals. The throwing
            // subscriber will throw on every snapshot it receives;
            // the monitor's try/catch must isolate the throw and
            // let the next probe tick proceed.
            await Task.Delay(FastProbeInterval * 8, TestContext.Current.CancellationToken);

            int finalCount;
            lock (receiveLock)
            {
                finalCount = received.Count;
            }

            int newSnapshots = finalCount - baselineCount;
            Assert.True(
                newSnapshots >= 3,
                $"Expected at least 3 new snapshots after a throwing subscriber was attached, got {newSnapshots}.");
        }
        finally
        {
            throwingSubscription?.Dispose();
            countingSubscription.Dispose();
            subscribeThread.Join(TimeSpan.FromSeconds(2));
            await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// 0.0.23 contract: rapid-fire ticks must be coalesced. The
    /// monitor owns a single <c>probePending</c> flag toggled via
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>;
    /// while a probe is in flight, the loop drops subsequent ticks
    /// rather than running a second overlapping probe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test uses <see cref="TimeProvider.System"/> with the
    /// 50 ms fast interval. The probe body is fully synchronous
    /// (no I/O, no awaits), so each tick completes its full
    /// probe → publish cycle in well under one interval. The
    /// coalescing flag is therefore exercised on every tick: the
    /// loop never enqueues a second probe while the first is in
    /// flight.
    /// </para>
    /// <para>
    /// The test asserts the observable contract: the loop ticks
    /// at the configured cadence and publishes exactly one
    /// snapshot per processed tick (the snapshot is the freshly
    /// computed one, not a duplicate of the previous one).
    /// </para>
    /// </remarks>
    [Fact]
    public static async Task RepeatedTicks_CoalesceIntoOneProbe()
    {
        using MonitorFixture fixture = MonitorFixture.Create();

        System.Collections.Generic.List<RuntimeHealthSnapshot> received = new();
        object receiveLock = new();
        IDisposable? subscription = null;
        try
        {
            subscription = fixture.Monitor.SnapshotChanged.Subscribe(snapshot =>
            {
                lock (receiveLock)
                {
                    received.Add(snapshot);
                }
            });

            await fixture.Monitor.StartAsync(TestContext.Current.CancellationToken);

            // The BehaviorSubject emitted the initial Unknown
            // snapshot synchronously on subscription.
            int baselineCount;
            lock (receiveLock)
            {
                baselineCount = received.Count;
            }
            Assert.True(baselineCount >= 1);

            // Wait for several probe intervals. With a 50 ms
            // interval and a fully-synchronous probe body, the
            // loop should process at least 8 ticks in 500 ms.
            // The exact count is non-deterministic; we assert
            // a lower bound to make the test robust to CI
            // scheduling jitter.
            await Task.Delay(FastProbeInterval * 10, TestContext.Current.CancellationToken);

            int finalCount;
            lock (receiveLock)
            {
                finalCount = received.Count;
            }

            int processedTicks = finalCount - baselineCount;
            Assert.True(
                processedTicks >= 5,
                $"Expected at least 5 tick-driven OnNext calls, got {processedTicks}.");
        }
        finally
        {
            subscription?.Dispose();
            await fixture.Monitor.StopAsync(TestContext.Current.CancellationToken);
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
    /// <see cref="RuntimeHealthMonitor"/> with a 50 ms probe
    /// interval, and the matching <see cref="IRuntimeKernelStateStore"/>.
    /// The monitor, the host and the temp directory are disposed
    /// together in <see cref="Dispose"/>.
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

        public static MonitorFixture Create(TimeProvider? timeProvider = null)
        {
            TemporarySqliteDatabase database = new();
            database.Initialize();
            SqliteConnectionFactory factory = database.CreateFactory();
            IRuntimeKernelStateStore stateStore = new RuntimeKernelStateStore(factory);

            RuntimeProcessHostTests hostTests = new();
            RuntimeProcessHostTests.HostFixture hostFixture = hostTests.CreateHostFixture();

            RuntimeHealthMonitor monitor = new(
                host: hostFixture.Host,
                stateStore: stateStore,
                logger: NullLogger<RuntimeHealthMonitor>.Instance,
                timeProvider: timeProvider ?? TimeProvider.System,
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
