using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
using Zapret2Pilot.Runtime.Supervisor;
using Zapret2Pilot.Runtime.Tests.Testing;

namespace Zapret2Pilot.Runtime.Tests.Supervisor;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// Focused xUnit tests for <see cref="RuntimeSupervisor"/>
/// (milestone 0.0.23). The supervisor is exercised with hand-rolled
/// fakes for <see cref="IRuntimeProcessHost"/> and
/// <see cref="IRuntimeHealthMonitor"/>; the real
/// <see cref="CrashLoopGuard"/> is reused with small, fast
/// <see cref="CrashLoopGuardOptions"/> so the guard's
/// failure / success / backoff behaviour stays observable without
/// sleeping. A shared <see cref="FakeClock"/> drives time for both
/// the supervisor's <see cref="TimeProvider"/> and the guard's
/// clock seam, so every scenario is fully deterministic.
/// </summary>
public sealed class RuntimeSupervisorTests
{
    private const string RuntimeExecutableRelativePath = "bin/winws2.exe";
    private const string ExecutableContent = "fake-winws2-binary";
    private const int SuccessfulProcessId = 1234;
    private const string SuccessfulProcessName = "winws2";
    private const string SuccessfulExecutablePath = @"C:\fake\winws2.exe";

    private static readonly TimeSpan StateWaitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HealthPublishDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan BackoffElapseBuffer = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StabilityElapseBuffer = TimeSpan.FromMilliseconds(100);

    [Fact]
    public static void Constructor_SetsInitialStateToStopped()
    {
        FakeClock clock = new();
        using RuntimeSupervisor supervisor = CreateSupervisor(clock).Supervisor;

        Assert.Equal(RuntimeSupervisorStatus.Stopped, supervisor.CurrentState.Status);
        Assert.Equal(clock.GetUtcNow(), supervisor.CurrentState.Timestamp);
        Assert.Null(supervisor.CurrentState.LastStartResult);
        Assert.Null(supervisor.CurrentState.GuardResult);
        Assert.Null(supervisor.CurrentState.LastError);
    }

    [Fact]
    public static async Task StartAsync_NullContext_ThrowsArgumentNullException()
    {
        FakeClock clock = new();
        using RuntimeSupervisor supervisor = CreateSupervisor(clock).Supervisor;

        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => supervisor.StartAsync(context: null!, TestContext.Current.CancellationToken));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public static async Task StartAsync_WhenRunning_ReturnsAlreadyRunning()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Host.NextStartResult = CreateSuccessResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> first = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.ToString() : string.Empty);
        Assert.Equal(RuntimeSupervisorStatus.Running, supervisor.CurrentState.Status);

        Result<RuntimeProcessHostResult> second = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);

        Assert.True(second.IsFailure);
        Assert.Equal("RuntimeSupervisorAlreadyRunning", second.Error.Code);
        Assert.Equal(1, harness.Host.StartCallCount);
    }

    [Fact]
    public static async Task StartAsync_GuardBlocked_PublishesStartBlockedAndDoesNotCallHost()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);

        // Drive the guard into a blocked state: a single recorded
        // failure puts the counter at 1 and the backoff window is
        // not yet elapsed, so the very next Check() returns
        // IsAllowed=false. The counter is still below
        // MaxConsecutiveFailures (10) so the supervisor classifies
        // this as a temporary backoff, not a permanent lockout.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        harness.Guard.RecordFailure();

        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> result = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeStartBlockedByCrashLoopGuard", result.Error.Code);
        Assert.Equal(RuntimeSupervisorStatus.StartBlocked, supervisor.CurrentState.Status);
        Assert.Equal(0, harness.Host.StartCallCount);
        Assert.NotNull(supervisor.CurrentState.GuardResult);
    }

    [Fact]
    public static async Task StartAsync_HostFailure_PublishesStoppedAndRecordsFailure()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Host.NextStartResult = CreateFailedStartResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> result = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("FakeHostStartFailed", result.Error.Code);
        Assert.Equal(RuntimeSupervisorStatus.Stopped, supervisor.CurrentState.Status);

        CrashLoopGuardResult guardResult = harness.Guard.Check();
        Assert.True(guardResult.ConsecutiveFailures > 0);
    }

    [Fact]
    public static async Task StartAsync_HostSuccess_PublishesRunning()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Host.NextStartResult = CreateSuccessResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> result = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        Assert.Equal(RuntimeSupervisorStatus.Running, supervisor.CurrentState.Status);
        Assert.NotNull(supervisor.CurrentState.LastStartResult);
        Assert.Equal(SuccessfulProcessId, supervisor.CurrentState.LastStartResult!.ProcessId);
    }

    [Fact]
    public static async Task StopAsync_WhenStopped_IsIdempotent()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<Unit> result = await supervisor.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        Assert.Equal(0, harness.Host.StopCallCount);
        Assert.Equal(RuntimeSupervisorStatus.Stopped, supervisor.CurrentState.Status);
    }

    [Fact]
    public static async Task StopAsync_WhenRunning_StopsHostAndPublishesStopped()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Host.NextStartResult = CreateSuccessResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> start = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(start.IsSuccess);

        Result<Unit> stop = await supervisor.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(stop.IsSuccess, stop.IsFailure ? stop.Error.ToString() : string.Empty);
        Assert.Equal(1, harness.Host.StopCallCount);
        Assert.Equal(RuntimeSupervisorStatus.Stopped, supervisor.CurrentState.Status);
    }

    [Fact]
    public static async Task HostedService_StartAsync_SubscribesToHealthMonitor()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Host.NextStartResult = CreateSuccessResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        // Bring the supervisor into Running so the health-snapshot
        // handler is willing to re-publish a Running state. The
        // guard is still at 0; we record a failure next so the
        // "counter clears after stability window + Check" assertion
        // below has something non-trivial to clear.
        Result<RuntimeProcessHostResult> start = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(start.IsSuccess, start.IsFailure ? start.Error.ToString() : string.Empty);
        Assert.Equal(RuntimeSupervisorStatus.Running, supervisor.CurrentState.Status);

        DateTimeOffset runningTimestamp = supervisor.CurrentState.Timestamp;

        harness.Guard.RecordFailure();
        CrashLoopGuardResult afterFailure = harness.Guard.Check();
        Assert.True(afterFailure.ConsecutiveFailures > 0);

        // Wire the supervisor up to the health monitor via the
        // hosted-service entry point. The explicit interface cast
        // is what production code uses to start the supervisor as
        // part of the Generic Host pipeline.
        await ((IHostedService)supervisor).StartAsync(TestContext.Current.CancellationToken);

        clock.Advance(HealthPublishDelay);
        harness.HealthMonitor.Publish(new RuntimeHealthSnapshot(
            state: RuntimeHealthState.Healthy,
            processId: SuccessfulProcessId,
            observedAtUtc: clock.GetUtcNow()));

        // The Healthy transition must re-publish Running with a
        // fresh timestamp and a cleared LastError — that is the
        // observable side effect of the supervisor calling
        // guard.RecordSuccess().
        Assert.Equal(RuntimeSupervisorStatus.Running, supervisor.CurrentState.Status);
        Assert.True(supervisor.CurrentState.Timestamp >= runningTimestamp);
        Assert.Null(supervisor.CurrentState.LastError);

        // Advance past the stability window. The next guard.Check()
        // must observe the recorded success + elapsed stability
        // window and clear the consecutive-failure counter to 0.
        clock.Advance(FastGuardOptions().StabilityWindow + StabilityElapseBuffer);

        CrashLoopGuardResult afterStability = harness.Guard.Check();
        Assert.True(afterStability.IsAllowed);
        Assert.Equal(0, afterStability.ConsecutiveFailures);
    }

    [Fact]
    public static async Task HealthSnapshot_Exited_PublishesStoppingAndStops()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Host.NextStartResult = CreateSuccessResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> start = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(start.IsSuccess, start.IsFailure ? start.Error.ToString() : string.Empty);

        await ((IHostedService)supervisor).StartAsync(TestContext.Current.CancellationToken);

        clock.Advance(HealthPublishDelay);
        harness.HealthMonitor.Publish(new RuntimeHealthSnapshot(
            state: RuntimeHealthState.Exited,
            processId: SuccessfulProcessId,
            observedAtUtc: clock.GetUtcNow()));

        // The Exited handler publishes Stopping synchronously on
        // the publishing thread, then kicks off StopAsync outside
        // the lock as a fire-and-forget task. Spin-wait until the
        // automatic stop publishes the terminal Stopped snapshot.
        RuntimeSupervisorState resolved = await WaitForStateAsync(
            supervisor,
            RuntimeSupervisorStatus.Stopped,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(RuntimeSupervisorStatus.Stopped, resolved.Status);
        Assert.Equal(1, harness.Host.StopCallCount);
    }

    private static SupervisorHarness CreateSupervisor(FakeClock clock)
    {
        CrashLoopGuard guard = new(FastGuardOptions(), clock.Now);
        FakeRuntimeProcessHost host = new();
        FakeRuntimeHealthMonitor healthMonitor = new();
        RuntimeSupervisor supervisor = new(
            host,
            healthMonitor,
            guard,
            NullLogger<RuntimeSupervisor>.Instance,
            clock);
        return new SupervisorHarness(supervisor, host, healthMonitor, guard, clock);
    }

    private static CrashLoopGuardOptions FastGuardOptions() => new(
        baseBackoff: TimeSpan.FromMilliseconds(20),
        maxBackoff: TimeSpan.FromMilliseconds(80),
        stabilityWindow: TimeSpan.FromMilliseconds(50),
        maxConsecutiveFailures: 3);

    private static Result<RuntimeProcessHostResult> CreateSuccessResult()
    {
        CompiledZapretPlan plan = new(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

        RuntimeProcessHostResult payload = new(
            processId: SuccessfulProcessId,
            processName: SuccessfulProcessName,
            executablePath: SuccessfulExecutablePath,
            plan: plan);

        return Result.Success(payload);
    }

    private static Result<RuntimeProcessHostResult> CreateFailedStartResult()
    {
        return Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
            code: "FakeHostStartFailed",
            message: "Simulated host start failure for the supervisor tests.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime));
    }

    /// <summary>
    /// Builds a valid <see cref="RuntimeProcessStartContext"/>. The
    /// host fake ignores the context, so the temporary executable
    /// is only needed to satisfy
    /// <see cref="VerifiedRuntimeExecutablePath.TryCreate"/>'s
    /// "file must exist on disk" check.
    /// </summary>
    private static RuntimeProcessStartContext CreateStartContext()
    {
        using TemporaryDirectory assetsRoot = new();

        string absolutePath = WriteFakeExecutable(
            assetsRoot.DirectoryPath,
            RuntimeExecutableRelativePath,
            ExecutableContent);

        string hash = ComputeSha256HexLower(absolutePath);
        ZapretAssetManifest manifest = new(
            RuntimeExecutable: new ZapretRuntimeAsset(
                RuntimeExecutableRelativePath,
                hash,
                AssetKind.Executable),
            Hostlists: Array.Empty<ZapretRuntimeAsset>(),
            StrategyPacks: Array.Empty<ZapretRuntimeAsset>());

        ZapretAssetVerificationSummary summary = new(new[] { RuntimeExecutableRelativePath });
        Result<VerifiedRuntimeExecutablePath> verifiedResult = VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            assetsRoot.DirectoryPath);
        Assert.True(
            verifiedResult.IsSuccess,
            verifiedResult.IsFailure ? verifiedResult.Error.ToString() : string.Empty);

        CompiledZapretPlan plan = new(
            generatedConfigContent: "# config\n",
            argsContent: "--new\n",
            hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

        return new RuntimeProcessStartContext(
            plan: plan,
            manifest: manifest,
            workspaceDirectory: assetsRoot.DirectoryPath,
            runtimeExecutablePath: verifiedResult.Value);
    }

    private static string WriteFakeExecutable(string root, string relativePath, string content)
    {
        string fullPath = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllBytes(fullPath, Encoding.UTF8.GetBytes(content));
        return fullPath;
    }

    private static string ComputeSha256HexLower(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        byte[] hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Spins until the supervisor reaches the requested
    /// <paramref name="expected"/> state, or
    /// <paramref name="timeout"/> elapses. Used by the Exited
    /// scenario, where the automatic stop is driven outside the
    /// snapshot handler's lock and resolves on a worker thread.
    /// </summary>
    private static async Task<RuntimeSupervisorState> WaitForStateAsync(
        RuntimeSupervisor supervisor,
        RuntimeSupervisorStatus expected,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (supervisor.CurrentState.Status == expected)
        {
            return supervisor.CurrentState;
        }

        TaskCompletionSource<RuntimeSupervisorState> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable subscription = supervisor.StateChanged
            .Where(state => state.Status == expected)
            .Take(1)
            .Subscribe(tcs.SetResult);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            return await tcs.Task.WaitAsync(linked.Token);
        }
        finally
        {
            subscription.Dispose();
        }
    }

    /// <summary>
    /// Test fixture that bundles the supervisor with its fakes so
    /// every test can reach the recording surfaces (host call
    /// counts, health-monitor publish) without juggling a forest
    /// of <c>out</c> parameters.
    /// </summary>
    private sealed record SupervisorHarness(
        RuntimeSupervisor Supervisor,
        FakeRuntimeProcessHost Host,
        FakeRuntimeHealthMonitor HealthMonitor,
        CrashLoopGuard Guard,
        FakeClock Clock);

    /// <summary>
    /// In-memory <see cref="IRuntimeProcessHost"/> fake. Records
    /// the next <see cref="StartAsync"/> result
    /// (<see cref="NextStartResult"/>) and the next
    /// <see cref="StopAsync"/> result
    /// (<see cref="NextStopResult"/>) so each test can drive the
    /// supervisor's state machine without touching a real
    /// process. Call counts are exposed for assertions.
    /// </summary>
    private sealed class FakeRuntimeProcessHost : IRuntimeProcessHost
    {
        public Result<RuntimeProcessHostResult> NextStartResult { get; set; } =
            Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "FakeHostNotConfigured",
                message: "FakeRuntimeProcessHost was not configured with a NextStartResult.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));

        public Result<Unit> NextStopResult { get; set; } = Result.Success(Unit.Instance);

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task<Result<RuntimeProcessHostResult>> StartAsync(
            RuntimeProcessStartContext context,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return Task.FromResult(NextStartResult);
        }

        public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            return Task.FromResult(NextStopResult);
        }
    }

    /// <summary>
    /// In-memory <see cref="IRuntimeHealthMonitor"/> fake. The
    /// snapshot stream is a <see cref="BehaviorSubject{T}"/> so
    /// <see cref="LatestSnapshot"/> always reflects the most
    /// recently published value, and tests can drive
    /// <see cref="Publish"/> to trigger the supervisor's
    /// health-snapshot handler synchronously on the calling
    /// thread.
    /// </summary>
    private sealed class FakeRuntimeHealthMonitor : IRuntimeHealthMonitor, IDisposable
    {
        private readonly BehaviorSubject<RuntimeHealthSnapshot> snapshotSubject = new(
            new RuntimeHealthSnapshot(
                state: RuntimeHealthState.Unknown,
                processId: null,
                observedAtUtc: DateTimeOffset.UtcNow));

        public IObservable<RuntimeHealthSnapshot> SnapshotChanged => snapshotSubject;

        public RuntimeHealthSnapshot LatestSnapshot => snapshotSubject.Value;

        public void Publish(RuntimeHealthSnapshot snapshot)
        {
            snapshotSubject.OnNext(snapshot);
        }

        public void Dispose()
        {
            snapshotSubject.Dispose();
        }
    }
}

#pragma warning restore CA1707 // Identifiers should not contain underscores
