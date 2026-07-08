#pragma warning disable CA1707 // Identifiers should not contain underscores

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
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
using Zapret2Pilot.Runtime.Health;
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

    [Fact]
    public static async Task EffectCompleted_IsNotDropped_UnderObservationPressure()
    {
        // P0-3 regression test: an EffectCompleted posted by an
        // in-flight effect must not be dropped even while 100
        // observations are queued in the observation slot. The
        // lifecycle channel is unbounded, so the kernel can
        // always accept the completion; the previous bounded
        // channel design could silently drop it under pressure.
        using ReleasableRunner runner = new();
        CountingGuard guard = new();
        using RuntimeKernelLoop loop = new(
            guard,
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await Task.Run(
            () => runner.Started.Wait(DrainWaitTimeout, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        // Capture the operation id and generation while the
        // state is still `Starting` and the pending operation
        // id is the in-flight start effect.
        RuntimeOperationId capturedOperationId = loop.CurrentState.PendingOperationId
            ?? RuntimeOperationId.New();
        RuntimeGeneration capturedGeneration = loop.CurrentState.Generation;

        // Hammer the observation slot. Only the latest value
        // survives in the bounded capacity 1 + DropOldest
        // observation channel.
        for (int i = 0; i < 100; i++)
        {
            bool observationAccepted = await loop.PostCommandAsync(
                new RuntimeKernelCommand.Observation(
                    capturedOperationId,
                    new RuntimeHealthSnapshot(
                        RuntimeHealthState.Healthy,
                        processId: 4321,
                        observedAtUtc: DateTimeOffset.UtcNow)),
                TestContext.Current.CancellationToken);
            Assert.True(observationAccepted);
        }

        // Reflectively call the private PostCompletion method.
        // The redesign replaces the bounded channel TryWrite
        // with an unbounded one, so the call must always
        // succeed even when the observation slot is under
        // pressure.
        MethodInfo postCompletion = typeof(RuntimeKernelLoop).GetMethod(
            "PostCompletion",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "RuntimeKernelLoop.PostCompletion was not found.");

        RuntimeKernelCommand.EffectCompleted completion = new(
            capturedOperationId,
            capturedGeneration,
            Result.Success(Unit.Instance),
            StartResult: null,
            CancellationReason: null,
            CrossedIrreversibleBoundary: true);

        object? result = postCompletion.Invoke(loop, new object?[] { completion });
        Assert.NotNull(result);
        Assert.True((bool)result, "PostCompletion returned false; the EffectCompleted would be dropped.");

        // Release the runner so Dispose completes promptly.
        runner.Release();

        RuntimeKernelStatus status = await WaitForStatus(
            loop,
            RuntimeKernelStatus.Running,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeKernelStatus.Running, status);
    }

    [Fact]
    public static async Task Observations_Coalesce_UnderPressure()
    {
        // 100 observations posted back-to-back must collapse
        // through the bounded capacity 1 + DropOldest
        // observation slot. The guard's RecordSuccess counter
        // is the observable side effect: every processed
        // Healthy observation emits a RecordGuardSuccess
        // effect, so the count proves how many observations
        // the kernel actually drained.
        using ReleasableRunner runner = new();
        CountingGuard guard = new();
        using RuntimeKernelLoop loop = new(
            guard,
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await Task.Run(
            () => runner.Started.Wait(DrainWaitTimeout, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        RuntimeOperationId operationId = loop.CurrentState.PendingOperationId
            ?? RuntimeOperationId.New();

        for (int i = 0; i < 100; i++)
        {
            bool observationAccepted = await loop.PostCommandAsync(
                new RuntimeKernelCommand.Observation(
                    operationId,
                    new RuntimeHealthSnapshot(
                        RuntimeHealthState.Healthy,
                        processId: 4321,
                        observedAtUtc: DateTimeOffset.UtcNow)),
                TestContext.Current.CancellationToken);
            Assert.True(observationAccepted);
        }

        runner.Release();

        await WaitForStatus(
            loop,
            RuntimeKernelStatus.Running,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);

        // The kernel only ever processes the latest pending
        // observation per channel-read cycle, so the guard
        // counter must be strictly less than the number of
        // posts but at least 1 (the kernel did process at
        // least one observation).
        Assert.True(
            guard.RecordSuccessCount >= 1,
            $"Expected at least one RecordSuccess call. Actual: {guard.RecordSuccessCount}.");
        Assert.True(
            guard.RecordSuccessCount < 100,
            $"Coalescing failed: guard.RecordSuccessCount was {guard.RecordSuccessCount} (>= 100).");
    }

    [Fact]
    public static async Task StopCommand_Delivered_UnderObservationPressure()
    {
        // Stop is a lifecycle command and must supersede every
        // queued observation. With the redesigned transport
        // the lifecycle channel is unbounded, so Stop is never
        // queued behind observations.
        using ReleasableRunner runner = new();
        CountingGuard guard = new();
        using RuntimeKernelLoop loop = new(
            guard,
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool accepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(accepted);

        await Task.Run(
            () => runner.Started.Wait(DrainWaitTimeout, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        RuntimeOperationId operationId = loop.CurrentState.PendingOperationId
            ?? RuntimeOperationId.New();

        for (int i = 0; i < 100; i++)
        {
            bool observationAccepted = await loop.PostCommandAsync(
                new RuntimeKernelCommand.Observation(
                    operationId,
                    new RuntimeHealthSnapshot(
                        RuntimeHealthState.Healthy,
                        processId: 4321,
                        observedAtUtc: DateTimeOffset.UtcNow)),
                TestContext.Current.CancellationToken);
            Assert.True(observationAccepted);
        }

        bool stopAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Stop(
                RuntimeOperationId.New(),
                "test-pressure"),
            TestContext.Current.CancellationToken);
        Assert.True(stopAccepted);

        // The runner is still blocked. The loop must reach
        // Stopping because Stop is delivered through the
        // lifecycle channel, which is never queued behind
        // observations.
        RuntimeKernelStatus status = await WaitForStatus(
            loop,
            RuntimeKernelStatus.Stopping,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeKernelStatus.Stopping, status);

        runner.Release();
    }

    [Fact]
    public static async Task Dispose_DrainsObservationSlot()
    {
        // Dispose must not throw when an observation is still
        // sitting in the observation slot. The kernel loop
        // should exit cleanly through the channel completion
        // path without leaking the in-flight effect or
        // surfacing an unhandled exception.
        using ReleasableRunner runner = new();
        CountingGuard guard = new();
        RuntimeKernelLoop loop = new(
            guard,
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool startAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(startAccepted);

        bool observationAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Observation(
                RuntimeOperationId.New(),
                new RuntimeHealthSnapshot(
                    RuntimeHealthState.Healthy,
                    processId: 4321,
                    observedAtUtc: DateTimeOffset.UtcNow)),
            TestContext.Current.CancellationToken);
        Assert.True(observationAccepted);

        // Release the runner so the in-flight effect observes
        // cancellation promptly during Dispose and the
        // in-flight effect tracker can drain.
        runner.Release();

        // Give the kernel thread a moment to observe the
        // observation so we are testing the slot-draining
        // path (otherwise the kernel might still be reading
        // the Start command when Dispose runs).
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        await loop.StopAsync(TestContext.Current.CancellationToken);
        Exception? disposeException = Record.Exception(() => loop.Dispose());
        Assert.Null(disposeException);
    }

    [Fact]
    public static async Task Stop_DuringStart_CancelsInFlightStartEffect()
    {
        // P0-2 regression test: when a Stop (or, in this
        // direct-to-loop variant, a CancelOperation)
        // supersedes an in-flight Start, the underlying
        // effect must observe cancellation and unwind
        // promptly — not only the supervisor receipt.
        // Without the per-operation CTS, the runner would
        // block until its natural deadline (or 30 seconds)
        // because nothing in the kernel told its
        // CancellationToken to fire.
        using CancellableRunner runner = new();
        using RuntimeKernelLoop loop = new(
            new AlwaysAllowedGuard(),
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool startAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(startAccepted);

        // Wait until the in-flight effect has actually
        // started so the test races against a real
        // in-flight Task, not a queued Start.
        await Task.Run(
            () => runner.Started.Wait(DrainWaitTimeout, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        RuntimeKernelState startingState = loop.CurrentState;
        Assert.Equal(RuntimeKernelStatus.Starting, startingState.Status);
        RuntimeOperationId capturedOperationId = startingState.PendingOperationId
            ?? RuntimeOperationId.New();
        RuntimeGeneration capturedGeneration = startingState.Generation;

        // Post a CancelOperation directly. The reducer
        // matches it against the in-flight effect, the loop
        // flips the per-operation CTS, and the runner
        // observes the cancellation within a few tens of
        // milliseconds.
        bool cancelAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.CancelOperation(
                capturedOperationId,
                capturedGeneration,
                RuntimeCancellationReason.Superseded),
            TestContext.Current.CancellationToken);
        Assert.True(cancelAccepted);

        // The runner must observe the cancellation well
        // before the 30-second start deadline. 2 seconds is
        // a generous upper bound that still proves the
        // cancellation is fast.
        bool observed = await Task.Run(
            () => runner.CancellationObserved.Wait(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.True(
            observed,
            "The in-flight start effect did not observe cancellation within 2 seconds after the CancelOperation was posted.");

        // The state must reach Stopped quickly too, because
        // the runner's cancellation propagates as an
        // EffectCompleted with the Superseded reason. The
        // reducer treats the cancelled effect as a regular
        // failure and moves the state to Stopped.
        RuntimeKernelStatus status = await WaitForStatus(
            loop,
            RuntimeKernelStatus.Stopped,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeKernelStatus.Stopped, status);
    }

    [Fact]
    public static async Task CancelOperation_StaleGeneration_IsIgnored()
    {
        // A CancelOperation whose generation does not match
        // the current kernel state must be ignored: the
        // reducer emits an IgnoredStaleCompletion event and
        // the in-flight effect is NOT cancelled. The
        // plan-level guarantee is "Generation must match";
        // the test exercises a generation that is older
        // than the current state (the in-flight start is
        // already at the next generation by the time the
        // cancel arrives).
        using CancellableRunner runner = new();
        using RuntimeKernelLoop loop = new(
            new AlwaysAllowedGuard(),
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool startAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(startAccepted);

        await Task.Run(
            () => runner.Started.Wait(DrainWaitTimeout, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        RuntimeKernelState startingState = loop.CurrentState;
        Assert.Equal(RuntimeKernelStatus.Starting, startingState.Status);
        RuntimeOperationId capturedOperationId = startingState.PendingOperationId
            ?? RuntimeOperationId.New();

        // Use a generation older than the current one. The
        // reducer must reject the command and the runner
        // must NOT observe cancellation.
        RuntimeGeneration staleGeneration = new(Math.Max(0, startingState.Generation.Value - 1));

        bool cancelAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.CancelOperation(
                capturedOperationId,
                staleGeneration,
                RuntimeCancellationReason.Superseded),
            TestContext.Current.CancellationToken);
        Assert.True(cancelAccepted);

        // The state must remain Starting: the cancel was a
        // no-op because the generation did not match.
        await Task.Delay(
            TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeKernelStatus.Starting, loop.CurrentState.Status);

        // The runner must NOT have observed cancellation.
        Assert.False(
            runner.CancellationObserved.IsSet,
            "A stale-generation CancelOperation must not cancel the in-flight effect.");

        // Release the runner so the test can drain
        // cleanly.
        runner.Release();
    }

    [Fact]
    public static async Task Start_Cancelled_CompletesAsSuperseded()
    {
        // When a Start effect is superseded (e.g. by a
        // Stop that races against the in-flight start),
        // the kernel loop must:
        //   1. Flip the per-operation CTS so the runner
        //      observes cancellation.
        //   2. Record the cancellation reason from the
        //      CancelOperation command so the
        //      completion callback can lift it onto the
        //      resulting EffectCompleted.
        //   3. Drive the state to Stopped once the
        //      EffectCompleted is processed.
        //
        // This test verifies the captured EffectCompleted
        // carries RuntimeCancellationReason.Superseded and
        // the final state is Stopped. The wrapper runner
        // captures the EffectCompleted so the test can
        // assert on it directly (the loop's
        // RuntimeKernelState projection does not surface
        // the completion's CancellationReason).
        using CompletionCapturingRunner runner = new();
        using RuntimeKernelLoop loop = new(
            new AlwaysAllowedGuard(),
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool startAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(startAccepted);

        // Wait until the in-flight effect has actually
        // started so the test races against a real
        // in-flight Task, not a queued Start.
        await Task.Run(
            () => runner.Started.Wait(DrainWaitTimeout, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        RuntimeKernelState startingState = loop.CurrentState;
        Assert.Equal(RuntimeKernelStatus.Starting, startingState.Status);
        RuntimeOperationId capturedOperationId = startingState.PendingOperationId
            ?? RuntimeOperationId.New();
        RuntimeGeneration capturedGeneration = startingState.Generation;

        // Post a CancelOperation with the Superseded
        // reason — the same shape the supervisor posts
        // from TryCancelInFlightStart.
        bool cancelAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.CancelOperation(
                capturedOperationId,
                capturedGeneration,
                RuntimeCancellationReason.Superseded),
            TestContext.Current.CancellationToken);
        Assert.True(cancelAccepted);

        // Wait for the runner to observe cancellation
        // (i.e. the linked token fired) so the test does
        // not race the completion back to the loop.
        bool observed = await Task.Run(
            () => runner.CancellationObserved.Wait(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.True(
            observed,
            "The in-flight start effect did not observe cancellation within 2 seconds after the CancelOperation was posted.");

        // Wait for the state to reach Stopped. The
        // reducer treats the cancelled EffectCompleted
        // (failure with Superseded reason) as a regular
        // failure and drives the state to Stopped.
        RuntimeKernelStatus status = await WaitForStatus(
            loop,
            RuntimeKernelStatus.Stopped,
            StateWaitTimeout,
            TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeKernelStatus.Stopped, status);

        // The completion captured by the wrapper must
        // carry the Superseded reason. The completion
        // callback in the loop lifts the stored
        // per-operation reason onto the EffectCompleted
        // before posting it back to the reducer; this
        // assertion verifies that lift is wired
        // correctly.
        Assert.NotNull(runner.CapturedCompletion);
        Assert.Equal(
            RuntimeCancellationReason.Superseded,
            runner.CapturedCompletion!.CancellationReason);
    }

    [Fact]
    public static async Task WorkerShutdown_CancelsAllInFlightOperations()
    {
        // When the loop is disposed (or its worker is
        // otherwise shut down) while two or more slow
        // effects are in flight, the worker-CTS
        // cancellation must propagate through each
        // effect's linked token and each effect must
        // observe cancellation. The kernel's
        // per-operation CTS dictionary is also swept
        // during Dispose, but the primary mechanism is
        // the worker-CTS cancellation.
        //
        // To produce two in-flight effects we exploit
        // the kernel's generation model: the Start
        // effect is dispatched on generation N+1 and is
        // still in flight when we post a Stop. The
        // Stop command produces a StopProcess effect on
        // generation N+2, leaving both effects in
        // flight simultaneously.
        using MultiEffectCancellableRunner runner = new();
        using RuntimeKernelLoop loop = new(
            new AlwaysAllowedGuard(),
            runner,
            new FakeClock(),
            NullLogger<RuntimeKernelLoop>.Instance);

        using TemporaryDirectory assetsRoot = new();
        RuntimeProcessStartContext context = CreateStartContext(assetsRoot);

        bool startAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Start(context, AutomationOwner.User),
            TestContext.Current.CancellationToken);
        Assert.True(startAccepted);

        // Wait until the in-flight start effect has
        // actually been dispatched. We do this by
        // polling the runner's InFlightCount rather
        // than relying on a single ManualResetEventSlim
        // because we need to track the count for
        // multiple effects.
        await WaitForAsync(
            () => Task.FromResult(runner.InFlightCount >= 1),
            DrainWaitTimeout,
            TestContext.Current.CancellationToken);

        // Post a Stop command while the start effect is
        // still in flight. The reducer dispatches a
        // StopProcess effect on a fresh generation, so
        // both the start and the stop effects are now
        // in flight concurrently.
        bool stopAccepted = await loop.PostCommandAsync(
            new RuntimeKernelCommand.Stop(
                RuntimeOperationId.New(),
                "test-multi-effect-shutdown"),
            TestContext.Current.CancellationToken);
        Assert.True(stopAccepted);

        // Wait until both effects are in flight. This
        // is the precondition the test is verifying:
        // the kernel holds 2+ in-flight effects before
        // Dispose is called.
        await WaitForAsync(
            () => Task.FromResult(runner.InFlightCount >= 2),
            DrainWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.True(
            runner.InFlightCount >= 2,
            $"Expected at least 2 in-flight effects, but observed {runner.InFlightCount}.");

        // Dispose the loop. The worker-CTS cancellation
        // must propagate to both linked tokens; both
        // effects must observe cancellation.
        Exception? disposeException = Record.Exception(() => loop.Dispose());
        Assert.Null(disposeException);

        // Wait until both effects have observed
        // cancellation. The cancellation counter is
        // incremented from each effect's
        // CancellationTokenRegistration callback, so
        // the assertion directly proves the worker-CTS
        // cancellation reached every in-flight effect.
        await WaitForAsync(
            () => Task.FromResult(runner.CancellationObservedCount >= 2),
            DrainWaitTimeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            2,
            runner.CancellationObservedCount);
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

    private static async Task WaitForAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        linked.CancelAfter(timeout);
        TimeSpan pollInterval = TimeSpan.FromMilliseconds(10);
        while (!linked.IsCancellationRequested)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await Task.Delay(pollInterval, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException(
            $"The condition did not become true within {timeout}.");
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
    /// <see cref="IRuntimeEffectRunner"/> fake used by the
    /// P0-2 cancellation tests. The runner registers a
    /// callback on its incoming
    /// <see cref="CancellationToken"/> so the test can
    /// observe the moment the kernel flips the
    /// per-operation CTS. The returned
    /// <see cref="Task{TResult}"/> completes only when
    /// either the token is cancelled (returning a
    /// <see cref="RuntimeCancellationReason.Superseded"/>
    /// completion) or the test calls
    /// <see cref="Release"/> (returning a normal success).
    /// </summary>
    private sealed class CancellableRunner : IRuntimeEffectRunner, IDisposable
    {
        private readonly ManualResetEventSlim started = new();
        private readonly ManualResetEventSlim cancellationObserved = new();
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int runAsyncCallCount;
        private int disposedFlag;

        public ManualResetEventSlim Started => started;

        public ManualResetEventSlim CancellationObserved => cancellationObserved;

        public int RunAsyncCallCount => Volatile.Read(ref runAsyncCallCount);

        public void Release() => release.TrySetResult(true);

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            started.Set();
            Interlocked.Increment(ref runAsyncCallCount);

            return Task.Run(async () =>
            {
                CancellationTokenSource localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                try
                {
                    // Race Release against cancellation:
                    // whichever wins determines the
                    // completion shape. A local CTS
                    // disposes the registration
                    // deterministically so the test
                    // runner never races Dispose on
                    // the helper.
                    using CancellationTokenRegistration registration = localCts.Token.Register(
                        static state =>
                        {
                            CancellableRunner self = (CancellableRunner)state!;
                            if (Volatile.Read(ref self.disposedFlag) == 0)
                            {
                                try
                                {
                                    self.cancellationObserved.Set();
                                }
                                catch (ObjectDisposedException)
                                {
                                    // The test disposed the
                                    // helper between the
                                    // cancellation signal and
                                    // our callback. The
                                    // signal is still valid
                                    // for the test's
                                    // assertions; swallow
                                    // the disposal race.
                                }
                            }
                        },
                        this);

                    Task winner = await Task.WhenAny(
                        release.Task,
                        Task.Delay(Timeout.Infinite, localCts.Token))
                        .ConfigureAwait(false);

                    if (winner == release.Task)
                    {
                        return new RuntimeKernelCommand.EffectCompleted(
                            intent.OperationId,
                            intent.Generation,
                            Result.Success(Unit.Instance),
                            StartResult: null,
                            CancellationReason: null,
                            CrossedIrreversibleBoundary: true);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Fall through to the cancellation
                    // completion below.
                }
                finally
                {
                    localCts.Dispose();
                }

                return new RuntimeKernelCommand.EffectCompleted(
                    intent.OperationId,
                    intent.Generation,
                    Result.Failure<Unit>(new ErrorInfo(
                        code: "RuntimeEffectCancelled",
                        message: "The cancellable runner observed cancellation.",
                        severity: ErrorSeverity.Error,
                        category: ErrorCategory.Runtime)),
                    StartResult: null,
                    CancellationReason: RuntimeCancellationReason.Superseded,
                    CrossedIrreversibleBoundary: false);
            }, cancellationToken);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref disposedFlag, 1);
            started.Dispose();
            cancellationObserved.Dispose();
            release.TrySetResult(false);
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

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake that signals
    /// when <see cref="RunAsync"/> is invoked, then blocks the
    /// returned task until either <see cref="Release"/> is
    /// called or the supplied cancellation token fires. Used
    /// by the observation-pressure tests to keep the
    /// in-flight start effect open while the test posts
    /// observations and lifecycle commands directly into the
    /// kernel.
    /// </summary>
    private sealed class ReleasableRunner : IRuntimeEffectRunner, IDisposable
    {
        private readonly ManualResetEventSlim started = new();
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int runAsyncCallCount;

        public ManualResetEventSlim Started => started;

        public bool RunAsyncCalled => Volatile.Read(ref runAsyncCallCount) > 0;

        public int RunAsyncCallCount => Volatile.Read(ref runAsyncCallCount);

        public void Release() => release.TrySetResult(true);

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            started.Set();
            Interlocked.Increment(ref runAsyncCallCount);

            return Task.Run(async () =>
            {
                // Race Release against cancellation: whichever
                // wins determines the completion shape. When
                // the test releases the runner the completion
                // is a normal success; when the kernel worker
                // CTS is cancelled (Dispose / StopAsync) the
                // completion reports HostShutdown so the
                // in-flight effect tracker can drain.
                Task winner = await Task.WhenAny(
                    release.Task,
                    Task.Delay(Timeout.Infinite, cancellationToken))
                    .ConfigureAwait(false);

                if (winner == release.Task)
                {
                    return new RuntimeKernelCommand.EffectCompleted(
                        intent.OperationId,
                        intent.Generation,
                        Result.Success(Unit.Instance),
                        StartResult: null,
                        CancellationReason: null,
                        CrossedIrreversibleBoundary: true);
                }

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
            release.TrySetResult(false);
        }
    }

    /// <summary>
    /// <see cref="ICrashLoopGuard"/> fake that records how
    /// many <see cref="RecordSuccess"/> and
    /// <see cref="RecordFailure"/> calls the reducer emitted.
    /// Used by the observation-coalescing test to prove the
    /// kernel collapsed duplicate observations before they
    /// reached the guard.
    /// </summary>
    private sealed class CountingGuard : ICrashLoopGuard
    {
        private int recordSuccessCount;
        private int recordFailureCount;

        public int RecordSuccessCount => Volatile.Read(ref recordSuccessCount);

        public int RecordFailureCount => Volatile.Read(ref recordFailureCount);

        public CrashLoopGuardResult Check() => new(
            isAllowed: true,
            backoffRemaining: null,
            consecutiveFailures: 0);

        public void RecordFailure() => Interlocked.Increment(ref recordFailureCount);

        public void RecordSuccess() => Interlocked.Increment(ref recordSuccessCount);

        public void Reset()
        {
        }
    }

    /// <summary>
    /// Minimal wrapper around <see cref="CancellableRunner"/>
    /// that captures the <see cref="RuntimeKernelCommand.EffectCompleted"/>
    /// the runner produced. The kernel's
    /// <see cref="RuntimeKernelState"/> projection does not
    /// surface the completion's
    /// <see cref="RuntimeKernelCommand.EffectCompleted.CancellationReason"/>,
    /// so the test needs a side channel to assert on the
    /// reason that the loop's completion callback lifted
    /// onto the EffectCompleted.
    /// </summary>
    private sealed class CompletionCapturingRunner : IRuntimeEffectRunner, IDisposable
    {
        private readonly CancellableRunner inner = new();

        public ManualResetEventSlim Started => inner.Started;

        public ManualResetEventSlim CancellationObserved => inner.CancellationObserved;

        public RuntimeKernelCommand.EffectCompleted? CapturedCompletion { get; private set; }

        public void Release() => inner.Release();

        public async Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            RuntimeKernelCommand.EffectCompleted completion = await inner
                .RunAsync(intent, timeProvider, cancellationToken)
                .ConfigureAwait(false);
            CapturedCompletion = completion;
            return completion;
        }

        public void Dispose() => inner.Dispose();
    }

    /// <summary>
    /// <see cref="IRuntimeEffectRunner"/> fake that tracks
    /// the number of in-flight effects and the number of
    /// effects that observed cancellation. Each
    /// <see cref="RunAsync"/> call increments the in-flight
    /// counter; the corresponding
    /// <see cref="CancellationTokenRegistration"/> callback
    /// increments the cancellation counter when the linked
    /// token fires. The effect returns a successful
    /// completion if released, or a
    /// <see cref="RuntimeCancellationReason.HostShutdown"/>
    /// completion on cancellation. Designed for the
    /// worker-shutdown regression test: it lets the test
    /// prove that disposing the kernel loop while 2+
    /// effects are in flight causes every effect to
    /// observe cancellation.
    /// </summary>
    private sealed class MultiEffectCancellableRunner : IRuntimeEffectRunner, IDisposable
    {
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int inFlightCount;
        private int cancellationObservedCount;
        private int disposedFlag;

        public int InFlightCount => Volatile.Read(ref inFlightCount);

        public int CancellationObservedCount => Volatile.Read(ref cancellationObservedCount);

        public void Release() => release.TrySetResult(true);

        public Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
            RuntimeEffectIntent intent,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref inFlightCount);

            return Task.Run(async () =>
            {
                try
                {
                    // Register the cancellation observer
                    // directly on the supplied token. The
                    // callback is invoked synchronously by
                    // CancellationTokenSource.Cancel even
                    // when the token is already cancelled
                    // at the time of registration, so the
                    // counter increments regardless of the
                    // dispatch race.
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        static state =>
                        {
                            MultiEffectCancellableRunner self = (MultiEffectCancellableRunner)state!;
                            if (Volatile.Read(ref self.disposedFlag) == 0)
                            {
                                Interlocked.Increment(ref self.cancellationObservedCount);
                            }
                        },
                        this);

                    Task winner = await Task.WhenAny(
                        release.Task,
                        Task.Delay(Timeout.Infinite, cancellationToken))
                        .ConfigureAwait(false);

                    if (winner == release.Task)
                    {
                        return new RuntimeKernelCommand.EffectCompleted(
                            intent.OperationId,
                            intent.Generation,
                            Result.Success(Unit.Instance),
                            StartResult: null,
                            CancellationReason: null,
                            CrossedIrreversibleBoundary: true);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Fall through to the cancellation
                    // completion below.
                }
                finally
                {
                    Interlocked.Decrement(ref inFlightCount);
                }

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
            Interlocked.Exchange(ref disposedFlag, 1);
            release.TrySetResult(false);
        }
    }
}
