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
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Supervisor;
using Zapret2Pilot.Runtime.Tests.Testing;

namespace Zapret2Pilot.Runtime.Tests.Supervisor;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// Focused xUnit tests for <see cref="RuntimeSupervisor"/>
/// (milestone 0.0.24). The supervisor is a façade over
/// <see cref="RuntimeKernelLoop"/>; tests exercise it with
/// hand-rolled fakes for <see cref="IRuntimeEffectRunner"/> and
/// <see cref="IRuntimeHealthMonitor"/> wired into a real
/// <see cref="RuntimeKernelLoop"/>. The real
/// <see cref="CrashLoopGuard"/> is reused with small, fast
/// <see cref="CrashLoopGuardOptions"/> so the guard's
/// failure / success / backoff behaviour stays observable without
/// sleeping. A shared <see cref="FakeClock"/> drives time for
/// both the supervisor's <see cref="TimeProvider"/> and the
/// guard's clock seam, so every scenario is fully deterministic.
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
        harness.Runner.NextStartResult = CreateSuccessResult();
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
        Assert.Equal(1, harness.Runner.StartCallCount);
    }

    [Fact]
    public static async Task StartAsync_GuardBlocked_PublishesStartBlockedAndDoesNotCallExecutor()
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
        Assert.Equal(0, harness.Runner.StartCallCount);
        Assert.NotNull(supervisor.CurrentState.GuardResult);
    }

    [Fact]
    public static async Task StartAsync_HostFailure_PublishesStoppedAndRecordsFailure()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Runner.NextStartResult = CreateFailedStartResult();
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
        harness.Runner.NextStartResult = CreateSuccessResult();
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
        Assert.Equal(0, harness.Runner.StopCallCount);
        Assert.Equal(RuntimeSupervisorStatus.Stopped, supervisor.CurrentState.Status);
    }

    [Fact]
    public static async Task StopAsync_WhenRunning_StopsProcessAndPublishesStopped()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Runner.NextStartResult = CreateSuccessResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> start = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(start.IsSuccess);

        Result<Unit> stop = await supervisor.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(stop.IsSuccess, stop.IsFailure ? stop.Error.ToString() : string.Empty);
        Assert.Equal(1, harness.Runner.StopCallCount);
        Assert.Equal(RuntimeSupervisorStatus.Stopped, supervisor.CurrentState.Status);
    }

    [Fact]
    public static async Task HostedService_StartAsync_ForwardsHealthSnapshotsToLoop()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Runner.NextStartResult = CreateSuccessResult();
        using RuntimeSupervisor supervisor = harness.Supervisor;

        // Bring the supervisor into Running so the health-snapshot
        // handler is willing to drive the loop's guard success path.
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

        // The Healthy observation is posted to the loop, which
        // records a guard success and re-publishes Running with a
        // fresh timestamp and a cleared LastError.
        await WaitForHealthyRecordedAsync(harness, StateWaitTimeout, TestContext.Current.CancellationToken);

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
        harness.Runner.NextStartResult = CreateSuccessResult();
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

        // The Exited observation is posted to the loop, which
        // records a guard failure and drives an automatic stop.
        // The supervisor's projection then publishes the terminal
        // Stopped snapshot.
        RuntimeSupervisorState resolved = await WaitForStateAsync(
            supervisor,
            RuntimeSupervisorStatus.Stopped,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(RuntimeSupervisorStatus.Stopped, resolved.Status);
        Assert.True(harness.Runner.StopCallCount >= 1);
    }

    [Fact]
    public static void KernelStatus_And_SupervisorStatus_AreAligned()
    {
        foreach (RuntimeKernelStatus kernelStatus in Enum.GetValues<RuntimeKernelStatus>())
        {
            var supervisorStatus = (RuntimeSupervisorStatus)(int)kernelStatus;
            Assert.True(Enum.IsDefined(supervisorStatus), $"Mapped supervisor status for {kernelStatus} is not defined.");
            Assert.Equal(kernelStatus.ToString(), supervisorStatus.ToString());
        }
    }

    [Fact]
    public static async Task Dispose_WhenRunning_PerformsGracefulStopAndDoesNotThrow()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Runner.NextStartResult = CreateSuccessResult();
        RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> start = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(start.IsSuccess, start.IsFailure ? start.Error.ToString() : string.Empty);
        Assert.Equal(RuntimeSupervisorStatus.Running, supervisor.CurrentState.Status);

        // Observe the projected stop transition so the test
        // validates the state reached the terminal Stopped
        // snapshot before Dispose returns. The observer is
        // detached once the stop fires so the test does not
        // observe the post-Dispose disposal of the publisher.
        TaskCompletionSource<RuntimeSupervisorState> stopObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable stopSubscription = supervisor.StateChanged
            .Where(state => state.Status == RuntimeSupervisorStatus.Stopped)
            .Take(1)
            .Subscribe(state => stopObserved.TrySetResult(state));

        try
        {
            // Dispose must not throw. The graceful stop path must
            // be reached even though the supervisor is being torn
            // down, so the runner's stop is invoked at least once.
            Exception? thrown = Record.Exception(() => supervisor.Dispose());
            Assert.Null(thrown);

            // The graceful stop transition must complete before
            // Dispose returns, and the runner's stop must have
            // been invoked. Accessing CurrentState after Dispose
            // is intentionally not exercised here: the underlying
            // publisher is disposed by the loop, which is the
            // documented pre-Disposal contract.
            RuntimeSupervisorState resolved = await stopObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            Assert.Equal(RuntimeSupervisorStatus.Stopped, resolved.Status);
        }
        finally
        {
            stopSubscription.Dispose();
        }

        Assert.True(harness.Runner.StopCallCount >= 1);
    }

    [Fact]
    public static void Dispose_IsIdempotent()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        RuntimeSupervisor supervisor = harness.Supervisor;

        Exception? first = Record.Exception(() => supervisor.Dispose());
        Exception? second = Record.Exception(() => supervisor.Dispose());
        Exception? third = Record.Exception(() => supervisor.Dispose());

        Assert.Null(first);
        Assert.Null(second);
        Assert.Null(third);
    }

    [Fact]
    public static async Task StopAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Runner.NextStartResult = CreateSuccessResult();
        RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> start = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(start.IsSuccess, start.IsFailure ? start.Error.ToString() : string.Empty);

        supervisor.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => supervisor.StopAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public static async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        RuntimeSupervisor supervisor = harness.Supervisor;

        supervisor.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => supervisor.StartAsync(
                CreateStartContext(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public static async Task HostedService_StopAsync_FollowedByDispose_DoesNotThrow()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        harness.Runner.NextStartResult = CreateSuccessResult();
        RuntimeSupervisor supervisor = harness.Supervisor;

        Result<RuntimeProcessHostResult> start = await supervisor.StartAsync(
            CreateStartContext(),
            TestContext.Current.CancellationToken);
        Assert.True(start.IsSuccess, start.IsFailure ? start.Error.ToString() : string.Empty);

        await ((IHostedService)supervisor).StartAsync(TestContext.Current.CancellationToken);
        await ((IHostedService)supervisor).StopAsync(TestContext.Current.CancellationToken);

        // The hosted-service StopAsync path drove the kernel to
        // the terminal Stopped state. We can read it through
        // CurrentState BEFORE Dispose because the publisher is
        // still alive at this point.
        Assert.Equal(RuntimeSupervisorStatus.Stopped, supervisor.CurrentState.Status);

        Exception? thrown = Record.Exception(() => supervisor.Dispose());
        Assert.Null(thrown);
    }

    [Fact]
    public static void HostedService_StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        FakeClock clock = new();
        SupervisorHarness harness = CreateSupervisor(clock);
        RuntimeSupervisor supervisor = harness.Supervisor;

        supervisor.Dispose();

        // The IHostedService.StartAsync implementation is
        // synchronous up to and including the disposed check, so
        // the exception is raised before any Task is returned.
        // Discard the return value to keep the lambda's return
        // type void and satisfy xUnit2014 (no Assert.Throws on
        // Func<Task>).
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = ((IHostedService)supervisor).StartAsync(TestContext.Current.CancellationToken);
        });
    }

    private static SupervisorHarness CreateSupervisor(FakeClock clock)
    {
        CrashLoopGuard guard = new(FastGuardOptions(), clock.Now);
        FakeRuntimeEffectRunner runner = new();
        FakeRuntimeHealthMonitor healthMonitor = new();
        RuntimeKernelLoop loop = new(
            guard,
            runner,
            clock,
            NullLogger<RuntimeKernelLoop>.Instance);
        RuntimeSupervisor supervisor = new(
            loop,
            healthMonitor,
            NullLogger<RuntimeSupervisor>.Instance,
            clock);
        return new SupervisorHarness(supervisor, runner, healthMonitor, guard, clock, loop);
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
    /// executor fake ignores the context, so the temporary
    /// executable is only needed to satisfy
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
    /// scenario, where the automatic stop is driven by the kernel
    /// loop on a dedicated thread and resolves asynchronously.
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
    /// Spins until the kernel loop has consumed the Healthy
    /// observation (the guard success has been recorded). This is
    /// a small helper that polls both the supervisor's projected
    /// state and the guard's <c>LastSuccessUtcForTests</c> seam so
    /// the test can race-free assert the post-condition of the
    /// health-snapshot handler. The kernel loop publishes its
    /// state <em>before</em> executing the inline
    /// <c>RecordGuardSuccess</c> effect, so a state-only wait can
    /// resolve before <c>RecordSuccess()</c> has actually been
    /// called.
    /// </summary>
    private static async Task WaitForHealthyRecordedAsync(
        SupervisorHarness harness,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        linked.CancelAfter(timeout);

        TimeSpan pollInterval = TimeSpan.FromMilliseconds(10);
        while (!linked.IsCancellationRequested)
        {
            RuntimeSupervisorState current = harness.Supervisor.CurrentState;
            if (current.Status == RuntimeSupervisorStatus.Running
                && current.LastError is null
                && harness.Guard.LastSuccessUtcForTests is not null)
            {
                return;
            }

            try
            {
                await Task.Delay(pollInterval, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timeout or caller cancellation; exit the loop and throw below.
                break;
            }
        }

        throw new TimeoutException(
            $"Timed out after {timeout} waiting for the supervisor to enter " +
            "Running with no LastError and for the guard to record a success.");
    }

    /// <summary>
    /// Test fixture that bundles the supervisor with its fakes so
    /// every test can reach the recording surfaces (runner call
    /// counts, health-monitor publish, loop) without juggling a
    /// forest of <c>out</c> parameters.
    /// </summary>
    private sealed record SupervisorHarness(
        RuntimeSupervisor Supervisor,
        FakeRuntimeEffectRunner Runner,
        FakeRuntimeHealthMonitor HealthMonitor,
        CrashLoopGuard Guard,
        FakeClock Clock,
        RuntimeKernelLoop Loop);

    /// <summary>
    /// In-memory <see cref="IRuntimeEffectRunner"/> fake. Records
    /// the next <see cref="NextStartResult"/> for a
    /// <see cref="RuntimeEffectKind.StartProcess"/> intent and the
    /// next <see cref="NextStopResult"/> for a
    /// <see cref="RuntimeEffectKind.StopProcess"/> intent so each
    /// test can drive the kernel's state machine without touching
    /// a real process. Call counts are exposed for assertions.
    /// </summary>
    /// <remarks>
    /// The dispatch shape is a single <see cref="RunAsync"/>
    /// that branches on <see cref="RuntimeEffectIntent.Kind"/>
    /// rather than two separate execute methods.
    /// </remarks>
    private sealed class FakeRuntimeEffectRunner : IRuntimeEffectRunner
    {
        public Result<RuntimeProcessHostResult> NextStartResult { get; set; } =
            Result.Failure<RuntimeProcessHostResult>(new ErrorInfo(
                code: "FakeRunnerNotConfigured",
                message: "FakeRuntimeEffectRunner was not configured with a NextStartResult.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime));

        public Result<Unit> NextStopResult { get; set; } = Result.Success(Unit.Instance);

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            if (intent.Kind == RuntimeEffectKind.StartProcess)
            {
                StartCallCount++;
                Result<RuntimeProcessHostResult> startResult = NextStartResult;
                Result<Unit> unitResult = startResult.IsSuccess
                    ? Result.Success(Unit.Instance)
                    : Result.Failure<Unit>(startResult.Error);
                return Task.FromResult(new RuntimeKernelCommand.EffectCompleted(
                    intent.OperationId,
                    intent.Generation,
                    unitResult,
                    startResult.IsSuccess ? startResult.Value : null,
                    CancellationReason: null,
                    CrossedIrreversibleBoundary: startResult.IsSuccess));
            }

            StopCallCount++;
            return Task.FromResult(new RuntimeKernelCommand.EffectCompleted(
                intent.OperationId,
                intent.Generation,
                NextStopResult,
                StartResult: null,
                CancellationReason: null,
                CrossedIrreversibleBoundary: true));
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
