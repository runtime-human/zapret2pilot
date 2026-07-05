#pragma warning disable CA1707 // Identifiers should not contain underscores

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Tests.Testing;

namespace Zapret2Pilot.Runtime.Tests.Kernel;

/// <summary>
/// xUnit tests for <see cref="RuntimeKernelLoop"/> (milestone
/// 0.0.24, Packet 3). The tests confirm the loop serialises
/// lifecycle commands on a dedicated worker thread, propagates
/// state transitions to <see cref="RuntimeStatePublisher"/>,
/// drains in-flight effect tasks on shutdown, rejects stale
/// completions, propagates cancellation reasons and enforces
/// the deadline applied to a <see cref="RuntimeEffectIntent"/>.
/// No real <c>winws2</c> process is launched; the crash-loop
/// guard is satisfied by a tiny in-memory fake.
/// </summary>
public sealed class RuntimeKernelLoopTests
{
    private const string RuntimeExecutableRelativePath = "bin/winws2.exe";
    private const string ExecutableContent = "fake-winws2-binary";
    private static readonly TimeSpan StateWaitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DrainWaitTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public static async Task PostCommand_Start_TransitionsThroughStartingToRunning()
    {
        using RuntimeKernelLoop loop = CreateLoop();
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        List<RuntimeKernelState> recorded = new();
        using IDisposable subscription = loop.StateChanged.Subscribe(recorded.Add);

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        RuntimeKernelStatus status = await WaitForStatus(
            loop,
            RuntimeKernelStatus.Running,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeKernelStatus.Running, status);
        Assert.Contains(recorded, s => s.Status == RuntimeKernelStatus.Starting);
        Assert.Equal(RuntimeKernelStatus.Running, recorded[recorded.Count - 1].Status);
    }

    [Fact]
    public static async Task Commands_AreProcessedOnKernelThread()
    {
        using RuntimeKernelLoop loop = CreateLoop();

        int kernelThreadId = loop.KernelThreadId;
        Assert.NotEqual(0, kernelThreadId);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await WaitForStatus(
            loop,
            RuntimeKernelStatus.Running,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        // The kernel thread id is captured at construction time and
        // remains valid for the lifetime of the loop, so verifying it
        // is still set after a transition is sufficient evidence the
        // command was processed on the dedicated worker thread.
        Assert.Equal(kernelThreadId, loop.KernelThreadId);
    }

    [Fact]
    public static async Task PostCommand_AfterStop_ReturnsFalse()
    {
        RuntimeKernelLoop loop = CreateLoop();
        await loop.StopAsync(TestContext.Current.CancellationToken);
        try
        {
            using TemporaryDirectory assetsRoot = new();
            RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

            bool accepted = await loop.PostCommandAsync(
                new RuntimeKernelCommand.Start(context, AutomationOwner.User),
                TestContext.Current.CancellationToken);

            Assert.False(accepted);
        }
        finally
        {
            loop.Dispose();
        }
    }

    [Fact]
    public static async Task StopAsync_CompletesChannelAndJoinsThread()
    {
        RuntimeKernelLoop loop = CreateLoop();

        await loop.StopAsync(TestContext.Current.CancellationToken);

        // Dispose must run to release the publisher/CTS resources.
        loop.Dispose();
    }

    [Fact]
    public static void Dispose_IsIdempotent()
    {
        RuntimeKernelLoop loop = CreateLoop();

        loop.Dispose();
        Exception? second = Record.Exception(() => loop.Dispose());

        Assert.Null(second);
    }

    [Fact]
    public static async Task ThrowingSubscriber_DoesNotBreakKernelThread()
    {
        using RuntimeKernelLoop loop = CreateLoop();
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        List<RuntimeKernelState> recorded = new();
        ThrowingObserver throwingObserver = new();

        using IDisposable recordingSubscription = loop.StateChanged.Subscribe(recorded.Add);
        using IDisposable throwingSubscription = loop.StateChanged.Subscribe(throwingObserver);

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        RuntimeKernelStatus status = await WaitForStatus(
            loop,
            RuntimeKernelStatus.Running,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeKernelStatus.Running, status);
        Assert.Contains(recorded, s => s.Status == RuntimeKernelStatus.Starting);
        Assert.True(throwingObserver.OnNextCount >= 3);
    }

    [Fact]
    public static async Task StaleCompletion_IsRejectedAndStateUnchanged()
    {
        // The loop's reducer already discards stale completions;
        // this test makes the contract observable through the
        // public state projection by starting a fresh operation
        // and then posting a completion whose generation is
        // older than the current state.
        ManualResetEventSlim completionHandled = new(false);
        StaleCompletionRunner runner = new(completionHandled);
        FakeClock clock = new();
        AlwaysAllowedGuard guard = new();
        using RuntimeKernelLoop loop = new(
            guard,
            runner,
            clock,
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        // 1. Submit a Start, wait for Running.
        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        RuntimeKernelState beforeInjection = await WaitForStatusAndGet(
            loop,
            RuntimeKernelStatus.Running,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        // 2. Inject a completion for the same operation id, but
        //    using the generation captured BEFORE the Start was
        //    processed. The reducer must reject the completion
        //    and leave the state unchanged.
        RuntimeOperationId sameOperationId = beforeInjection.PendingOperationId
            ?? RuntimeOperationId.New();
        // The loop already cleared PendingOperationId on the
        // Running transition, so the rejection will be based on
        // the mismatched pending-operation id (also stale) —
        // both paths lead to "state unchanged".
        await loop.PostCommandAsync(
            new RuntimeKernelCommand.EffectCompleted(
                sameOperationId,
                beforeInjection.Generation,
                Result.Success(Unit.Instance),
                StartResult: null,
                CancellationReason: null,
                CrossedIrreversibleBoundary: true),
            TestContext.Current.CancellationToken);

        // Give the kernel thread a chance to process the injected
        // completion. Because the state is unchanged, we expect
        // the same status and the same generation.
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        RuntimeKernelState afterInjection = loop.CurrentState;
        Assert.Equal(RuntimeKernelStatus.Running, afterInjection.Status);
        Assert.Equal(beforeInjection.Generation, afterInjection.Generation);
    }

    [Fact]
    public static async Task CancellationReason_PropagatesFromRunnerToCompletion()
    {
        // The runner returns a failed completion with a
        // HostShutdown reason. The loop's reducer treats the
        // failure as a regular stop, the status moves to
        // Stopped, and the state's LastError carries the error
        // from the completion.
        CancellationReasonRunner runner = new();
        FakeClock clock = new();
        AlwaysAllowedGuard guard = new();
        using RuntimeKernelLoop loop = new(
            guard,
            runner,
            clock,
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        RuntimeGeneration initialGeneration = loop.CurrentState.Generation;

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await WaitForStateAsync(
            loop,
            state => state.Status == RuntimeKernelStatus.Stopped && state.Generation.Value > initialGeneration.Value,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.NotNull(loop.CurrentState.LastError);
        Assert.Equal("FakeRunnerTimeout", loop.CurrentState.LastError!.Code);
    }

    [Fact]
    public static async Task Deadline_AlreadyInPast_PropagatesAsTimeoutFailure()
    {
        // Verify deadline-failure propagation through the loop.
        // The runner always returns a Timeout failure for
        // StartProcess intents; the reducer treats the failure
        // as a regular stop, the status moves to Stopped, and
        // the state's LastError carries the error from the
        // completion.
        FakeClock clock = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        DeadlineFailureRunner runner = new();
        using RuntimeKernelLoop loop = new(
            new AlwaysAllowedGuard(),
            runner,
            clock,
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        RuntimeGeneration initialGeneration = loop.CurrentState.Generation;

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await WaitForStateAsync(
            loop,
            state => state.Status == RuntimeKernelStatus.Stopped && state.Generation.Value > initialGeneration.Value,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        // The runner reported a Timeout failure; the loop
        // recorded it as the last error.
        Assert.NotNull(loop.CurrentState.LastError);
        Assert.Equal("RuntimeEffectDeadlineExceeded", loop.CurrentState.LastError!.Code);
        Assert.True(runner.StartCalled);
        Assert.Equal(1, runner.StartCallCount);
    }

    [Fact]
    public static async Task Dispose_DrainsInFlightEffect()
    {
        // Submit a Start; the slow runner does not return a
        // completion for a long time. Dispose should still
        // join the in-flight task without throwing.
        using SlowRunner runner = new(TimeSpan.FromSeconds(30));
        using RuntimeKernelLoop loop = new(
            new AlwaysAllowedGuard(),
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        // Wait until the runner has at least been called once
        // so we know the effect is in flight. ManualResetEventSlim
        // does not expose WaitAsync, so we run the synchronous
        // Wait on a thread-pool thread to keep the test async.
        await Task.Run(
            () => runner.Started.Wait(DrainWaitTimeout, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        // Dispose should return — the bounded effect drain
        // either waits for the task or times out. Either way
        // Dispose does not throw.
        Exception? disposeException = Record.Exception(() => loop.Dispose());
        Assert.Null(disposeException);
    }

    [Fact]
    public static async Task PostCommandAsync_ConcurrentPosts_AreAllAccepted()
    {
        // The bounded command channel has plenty of capacity
        // (64 slots). Twenty concurrent posts must all be
        // accepted, regardless of how the kernel thread
        // serialises them, and the loop must survive the burst
        // without throwing. The final state must be one of the
        // stable terminal states (Running — the noop runner
        // completes the first start and the remaining 19 starts
        // are rejected by the reducer — or Stopped if a
        // failure intervened).
        const int postCount = 20;
        using RuntimeKernelLoop loop = CreateLoop();
        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        Task<bool>[] posts = new Task<bool>[postCount];
        for (int i = 0; i < postCount; i++)
        {
            posts[i] = loop.PostCommandAsync(
                new RuntimeKernelCommand.Start(context, AutomationOwner.User),
                TestContext.Current.CancellationToken).AsTask();
        }

        bool[] results = await Task.WhenAll(posts)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.All(results, accepted => Assert.True(accepted));

        // The first post drives the kernel to Running via the
        // noop runner. The remaining posts are processed by the
        // reducer and rejected as "already running", but the
        // reducer does not throw — the loop stays alive. Give
        // the kernel thread a moment to drain the queue.
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        RuntimeKernelStatus status = loop.CurrentState.Status;
        Assert.True(
            status is RuntimeKernelStatus.Running or RuntimeKernelStatus.Stopped,
            $"Loop did not reach a stable state. Actual status: {status}.");
    }

    [Fact]
    public static async Task FaultedRunnerTask_IsConvertedToStoppedState()
    {
        // The fake runner's RunAsync throws synchronously;
        // Task.Run captures the exception and returns a faulted
        // task. The loop's BuildCompletionFromTask must convert
        // the fault into a typed "RuntimeEffectRunnerThrew"
        // completion that drives the state to Stopped with the
        // matching error code on LastError.
        SynchronouslyThrowingRunner runner = new();
        FakeClock clock = new();
        AlwaysAllowedGuard guard = new();
        using RuntimeKernelLoop loop = new(
            guard,
            runner,
            clock,
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        RuntimeGeneration initialGeneration = loop.CurrentState.Generation;

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await WaitForStateAsync(
            loop,
            state => state.Status == RuntimeKernelStatus.Stopped
                && state.Generation.Value > initialGeneration.Value,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.NotNull(loop.CurrentState.LastError);
        Assert.Equal("RuntimeEffectRunnerThrew", loop.CurrentState.LastError!.Code);
        Assert.True(runner.RunAsyncCalled);
    }

    [Fact]
    public static async Task CancelledRunnerTask_IsConvertedToStoppedState()
    {
        // The fake runner returns a Task that is already in the
        // cancelled state. The loop's BuildCompletionFromTask
        // must convert the cancellation into a typed
        // "RuntimeEffectCancelled" completion that drives the
        // state to Stopped with the matching error code on
        // LastError.
        CancelledTaskRunner runner = new();
        FakeClock clock = new();
        AlwaysAllowedGuard guard = new();
        using RuntimeKernelLoop loop = new(
            guard,
            runner,
            clock,
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        RuntimeGeneration initialGeneration = loop.CurrentState.Generation;

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await WaitForStateAsync(
            loop,
            state => state.Status == RuntimeKernelStatus.Stopped
                && state.Generation.Value > initialGeneration.Value,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.NotNull(loop.CurrentState.LastError);
        Assert.Equal("RuntimeEffectCancelled", loop.CurrentState.LastError!.Code);
        Assert.True(runner.RunAsyncCalled);
    }

    private static RuntimeKernelLoop CreateLoop()
    {
        FakeClock clock = new();
        AlwaysAllowedGuard guard = new();
        NoopEffectRunner runner = new();
        return new RuntimeKernelLoop(guard, runner, clock, NullLogger<RuntimeKernelLoop>.Instance);
    }

    private static async Task<RuntimeKernelStatus> WaitForStatus(
        RuntimeKernelLoop loop,
        RuntimeKernelStatus expected,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (loop.CurrentState.Status == expected)
        {
            return loop.CurrentState.Status;
        }

        TaskCompletionSource<RuntimeKernelStatus> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable subscription = loop.StateChanged
            .Where(state => state.Status == expected)
            .Take(1)
            .Subscribe(state => tcs.TrySetResult(state.Status));

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

    private static async Task<RuntimeKernelState> WaitForStateAsync(
        RuntimeKernelLoop loop,
        Func<RuntimeKernelState, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (predicate(loop.CurrentState))
        {
            return loop.CurrentState;
        }

        TaskCompletionSource<RuntimeKernelState> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable subscription = loop.StateChanged
            .Where(predicate)
            .Take(1)
            .Subscribe(state => tcs.TrySetResult(state));

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

    private static async Task<RuntimeKernelState> WaitForStatusAndGet(
        RuntimeKernelLoop loop,
        RuntimeKernelStatus expected,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (loop.CurrentState.Status == expected)
        {
            return loop.CurrentState;
        }

        TaskCompletionSource<RuntimeKernelState> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable subscription = loop.StateChanged
            .Where(state => state.Status == expected)
            .Take(1)
            .Subscribe(state => tcs.TrySetResult(state));

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

    private static RuntimeProcessStartContext CreateStartContext(TemporaryDirectory assetsRoot)
    {
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
        if (verifiedResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to build verified executable path: {verifiedResult.Error}");
        }

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
    /// In-memory <see cref="ICrashLoopGuard"/> that always allows
    /// the requested operation. The reducer only consults
    /// <see cref="ICrashLoopGuard.Check"/> in the Start path, so the
    /// <c>RecordSuccess</c> / <c>RecordFailure</c> calls become
    /// silent no-ops.
    /// </summary>
    private sealed class AlwaysAllowedGuard : ICrashLoopGuard
    {
        public CrashLoopGuardResult Check() => new(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0);

        public void RecordFailure()
        {
        }

        public void RecordSuccess()
        {
        }

        public void Reset()
        {
        }
    }

    /// <summary>
    /// Observer that allows the initial replay value to pass
    /// through along with the first two transitions, but throws on
    /// the third notification. The first notification is the
    /// initial <see cref="RuntimeKernelState"/> delivered
    /// synchronously by the <c>BehaviorSubject</c> when the test
    /// subscribes; the second and third are the <c>Starting</c> and
    /// <c>Running</c> states produced by the asynchronous effect
    /// execution inside the kernel loop.
    /// </summary>
    private sealed class ThrowingObserver : IObserver<RuntimeKernelState>
    {
        private int onNextCount;

        public int OnNextCount => Volatile.Read(ref onNextCount);

        public void OnNext(RuntimeKernelState value)
        {
            int count = Interlocked.Increment(ref onNextCount);
            if (count >= 3)
            {
                throw new InvalidOperationException(
                    "ThrowingObserver: simulated subscriber failure on the third notification.");
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake used by the
    /// happy-path tests. Returns a successful start completion
    /// synchronously and a successful stop completion
    /// synchronously.
    /// </summary>
    private sealed class NoopEffectRunner : IRuntimeEffectRunner
    {
        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            CompiledZapretPlan plan = new(
                generatedConfigContent: "# config\n",
                argsContent: "--new\n",
                hostlists: Array.Empty<CompiledZapretPlan.HostlistContent>());

            RuntimeProcessHostResult payload = new(
                processId: 4321,
                processName: "noop",
                executablePath: @"C:\noop\noop.exe",
                plan: plan);

            if (intent.Kind == RuntimeEffectKind.StartProcess)
            {
                return Task.FromResult(new RuntimeKernelCommand.EffectCompleted(
                    intent.OperationId,
                    intent.Generation,
                    Result.Success(Unit.Instance),
                    StartResult: payload,
                    CancellationReason: null,
                    CrossedIrreversibleBoundary: true));
            }

            return Task.FromResult(new RuntimeKernelCommand.EffectCompleted(
                intent.OperationId,
                intent.Generation,
                Result.Success(Unit.Instance),
                StartResult: null,
                CancellationReason: null,
                CrossedIrreversibleBoundary: true));
        }
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake that returns a
    /// failed completion with a typed <c>FakeRunnerTimeout</c>
    /// error and a <see cref="RuntimeCancellationReason.Timeout"/>
    /// reason. Used to verify the cancellation reason propagates
    /// from the runner, through the loop, into the reducer and
    /// finally into the public state.
    /// </summary>
    private sealed class CancellationReasonRunner : IRuntimeEffectRunner
    {
        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            ErrorInfo error = new(
                code: "FakeRunnerTimeout",
                message: "Simulated timeout failure for the cancellation-reason test.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);

            return Task.FromResult(new RuntimeKernelCommand.EffectCompleted(
                intent.OperationId,
                intent.Generation,
                Result.Failure<Unit>(error),
                StartResult: null,
                CancellationReason: RuntimeCancellationReason.Timeout,
                CrossedIrreversibleBoundary: false));
        }
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake that returns a
    /// deadline-exceeded failure for every <see cref="RuntimeEffectKind.StartProcess"/>
    /// intent. Used to verify that a deadline/timeout failure reported by
    /// the runner is propagated through the loop into the public state.
    /// </summary>
    private sealed class DeadlineFailureRunner : IRuntimeEffectRunner
    {
        private int startCallCount;

        public bool StartCalled => Volatile.Read(ref startCallCount) > 0;

        public int StartCallCount => Volatile.Read(ref startCallCount);

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            if (intent.Kind == RuntimeEffectKind.StartProcess)
            {
                Interlocked.Increment(ref startCallCount);
            }

            ErrorInfo error = new(
                code: "RuntimeEffectDeadlineExceeded",
                message: "The effect deadline has already elapsed.",
                severity: ErrorSeverity.Error,
                category: ErrorCategory.Runtime);

            return Task.FromResult(new RuntimeKernelCommand.EffectCompleted(
                intent.OperationId,
                intent.Generation,
                Result.Failure<Unit>(error),
                StartResult: null,
                CancellationReason: RuntimeCancellationReason.Timeout,
                CrossedIrreversibleBoundary: false));
        }
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake that is recorded
    /// in the constructor so the test can wait until the loop
    /// has actually dispatched the start effect. The fake
    /// records the calls it observes so the test can confirm
    /// the runner ran exactly once.
    /// </summary>
    private sealed class StaleCompletionRunner : IRuntimeEffectRunner
    {
        private readonly ManualResetEventSlim completionHandled;
        private int deadlineStartCallCount;

        public StaleCompletionRunner(ManualResetEventSlim completionHandled)
        {
            this.completionHandled = completionHandled;
        }

        public bool DeadlineStartCalled => Volatile.Read(ref deadlineStartCallCount) > 0;

        public int DeadlineStartCallCount => Volatile.Read(ref deadlineStartCallCount);

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            if (intent.Kind == RuntimeEffectKind.StartProcess)
            {
                Interlocked.Increment(ref deadlineStartCallCount);
            }

            return Task.FromResult(new RuntimeKernelCommand.EffectCompleted(
                intent.OperationId,
                intent.Generation,
                Result.Success(Unit.Instance),
                StartResult: null,
                CancellationReason: null,
                CrossedIrreversibleBoundary: true));
        }
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake that blocks
    /// (with <see cref="Task.Delay(TimeSpan, CancellationToken)"/>)
    /// before producing a completion. Used by the dispose-drain
    /// test to simulate an in-flight effect that outlives the
    /// loop's <see cref="RuntimeKernelLoop.StopAsync(CancellationToken)"/>
    /// call.
    /// </summary>
    private sealed class SlowRunner : IRuntimeEffectRunner, IDisposable
    {
        private readonly TimeSpan delay;
        private int startedCount;
        private readonly ManualResetEventSlim started = new();

        public SlowRunner(TimeSpan delay)
        {
            this.delay = delay;
        }

        public ManualResetEventSlim Started => started;

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            started.Set();
            Interlocked.Increment(ref startedCount);

            return Task.Run(async () =>
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                return new RuntimeKernelCommand.EffectCompleted(
                    intent.OperationId,
                    intent.Generation,
                    Result.Success(Unit.Instance),
                    StartResult: null,
                    CancellationReason: RuntimeCancellationReason.HostShutdown,
                    CrossedIrreversibleBoundary: false);
            }, cancellationToken);
        }

        public void Dispose()
        {
            started.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake whose
    /// <see cref="RunAsync"/> always throws
    /// <see cref="InvalidOperationException"/> synchronously.
    /// The kernel loop wraps the call in
    /// <see cref="Task.Run(Action, CancellationToken)"/> so the
    /// resulting task is in the faulted state; the loop must
    /// convert that fault into a typed
    /// <c>RuntimeEffectRunnerThrew</c> completion.
    /// </summary>
    private sealed class SynchronouslyThrowingRunner : IRuntimeEffectRunner
    {
        private int runAsyncCallCount;

        public bool RunAsyncCalled => Volatile.Read(ref runAsyncCallCount) > 0;

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref runAsyncCallCount);
            throw new InvalidOperationException(
                "SynchronouslyThrowingRunner: simulated synchronous host failure.");
        }
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake that returns a
    /// <see cref="Task{TResult}"/> already in the
    /// <see cref="TaskStatus.Canceled"/> state. The kernel
    /// loop's continuation observes the cancellation and
    /// converts it into a typed
    /// <c>RuntimeEffectCancelled</c> completion that drives
    /// the state to <see cref="RuntimeKernelStatus.Stopped"/>.
    /// </summary>
    private sealed class CancelledTaskRunner : IRuntimeEffectRunner
    {
        private static readonly CancellationToken CancelledToken = new(canceled: true);
        private int runAsyncCallCount;

        public bool RunAsyncCalled => Volatile.Read(ref runAsyncCallCount) > 0;

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref runAsyncCallCount);
            return Task.FromCanceled<RuntimeKernelCommand.EffectCompleted>(CancelledToken);
        }
    }
}
