#pragma warning disable CA1707 // Identifiers should not contain underscores

using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Runtime.Kernel;

namespace Zapret2Pilot.Runtime.Tests.Kernel;

/// <summary>
/// xUnit tests for <see cref="RuntimeStatePublisher"/> (milestone 0.0.24).
/// Verifies that a new subscriber immediately observes the latest state,
/// that <see cref="RuntimeStatePublisher.Publish"/> updates the cached
/// value, and that a misbehaving observer does not break the publisher
/// or starve the remaining subscribers. The publisher replays the
/// initial state and forwards subsequent emissions on the task-pool
/// scheduler, so the tests use a <see cref="TaskCompletionSource{TResult}"/>
/// with <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>
/// to wait for the asynchronous delivery instead of relying on
/// wall-clock sleeps.
/// </summary>
public sealed class RuntimeStatePublisherTests
{
    [Fact]
    public static async Task Subscriber_ReceivesLatestState_OnSubscription()
    {
        RuntimeKernelState initial = CreateState(RuntimeKernelStatus.Stopped, new RuntimeGeneration(0));
        using RuntimeStatePublisher publisher = new(initial);

        TaskCompletionSource<RuntimeKernelState> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = publisher.StateChanged.Subscribe(tcs.SetResult);

        RuntimeKernelState received = await tcs.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Same(initial, received);
        Assert.Same(initial, publisher.LatestState);
    }

    [Fact]
    public static void Publish_UpdatesLatestState()
    {
        RuntimeKernelState initial = CreateState(RuntimeKernelStatus.Stopped, new RuntimeGeneration(0));
        using RuntimeStatePublisher publisher = new(initial);

        RuntimeKernelState next = CreateState(RuntimeKernelStatus.Running, new RuntimeGeneration(1));
        publisher.Publish(next);

        Assert.Same(next, publisher.LatestState);
    }

    [Fact]
    public static async Task ThrowingSubscriber_DoesNotPropagate_ToPublisherOrOtherSubscribers()
    {
        RuntimeKernelState initial = CreateState(RuntimeKernelStatus.Stopped, new RuntimeGeneration(0));
        using RuntimeStatePublisher publisher = new(initial);

        TaskCompletionSource<RuntimeKernelState> delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ThrowingObserver throwing = new();
        int receivedCount = 0;

        using IDisposable normalSubscription = publisher.StateChanged.Subscribe(state =>
        {
            int count = Interlocked.Increment(ref receivedCount);
            if (count == 2)
            {
                delivered.TrySetResult(state);
            }
        });
        using IDisposable throwingSubscription = publisher.StateChanged.Subscribe(throwing);

        RuntimeKernelState next = CreateState(RuntimeKernelStatus.Running, new RuntimeGeneration(1));

        Exception? publishException = Record.Exception(() => publisher.Publish(next));
        Assert.Null(publishException);

        // Wait for both signals: the normal subscriber received the second
        // state, and the throwing observer reached the point where it would
        // raise. Either side may run first on the task-pool scheduler, so
        // awaiting both eliminates the race that previously made the
        // OnNextCount assertion flaky. Both tasks are guaranteed to be
        // completed after Task.WhenAll returns, so awaiting them again
        // completes synchronously and is safe under xUnit1031.
        await Task.WhenAll(
            delivered.Task.WaitAsync(TestContext.Current.CancellationToken),
            throwing.ThrownTask.WaitAsync(TestContext.Current.CancellationToken));

        RuntimeKernelState received = await delivered.Task;
        Assert.Same(next, received);
        Assert.Same(next, publisher.LatestState);
        Assert.True(throwing.OnNextCount >= 2);
    }

    private static RuntimeKernelState CreateState(RuntimeKernelStatus status, RuntimeGeneration generation)
    {
        return new RuntimeKernelState(
            status,
            AutomationOwner.None,
            generation,
            pendingOperationId: null,
            lastStartResult: null,
            guardResult: null,
            lastError: null,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Observer that tolerates the initial replay value (delivered by
    /// the underlying <c>BehaviorSubject</c>) and then throws on every
    /// subsequent notification. The first <c>OnNext</c> is consumed
    /// silently so the synchronous replay path cannot crash the
    /// subscribing thread; later emissions are deliberately
    /// misbehaving and exercise the publisher's defensive contract.
    /// Exposes <see cref="ThrownTask"/> so the test can deterministically
    /// wait for this observer to reach the throwing branch (the second
    /// <c>OnNext</c>) before asserting, regardless of the order in which
    /// the task-pool scheduler delivers notifications to subscribers.
    /// </summary>
    private sealed class ThrowingObserver : IObserver<RuntimeKernelState>
    {
        private readonly TaskCompletionSource<RuntimeKernelState> thrown =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int onNextCount;

        public int OnNextCount => Volatile.Read(ref onNextCount);

        public Task<RuntimeKernelState> ThrownTask => thrown.Task;

        public void OnNext(RuntimeKernelState value)
        {
            int count = Interlocked.Increment(ref onNextCount);
            if (count >= 2)
            {
                // Signal the test that the throwing branch has been reached
                // before the exception escapes, so the await side in the
                // test can synchronize on this observer's progress without
                // resorting to wall-clock sleeps.
                thrown.TrySetResult(value);
                throw new InvalidOperationException(
                    "ThrowingObserver: simulated subscriber failure on the second notification.");
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
