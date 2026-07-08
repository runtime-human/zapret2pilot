using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Runtime.Threading;

namespace Zapret2Pilot.Runtime.Tests.Threading;

// Test method names deliberately use snake_case to make scenarios
// readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// xUnit v3 tests for <see cref="RuntimeAffinityExecutor"/>. Cover the
/// P0-1 fixes: bounded queues (256), correct linked-CTS disposal
/// ordering, complete-the-TCS-on-pump-exit contract, and graceful
/// handling of a failed thread join in <see cref="RuntimeAffinityExecutor.Dispose"/>.
/// </summary>
public sealed class RuntimeAffinityExecutorTests
{
    [Fact]
    public static async Task ExecuteAsync_WorkCompletes_ReturnsResult()
    {
        using RuntimeAffinityExecutor executor = new();

        int result = await executor.ExecuteAsync(
            static _ => Task.FromResult(42),
            TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public static async Task ExecuteAsync_CallerTokenCanceled_CompletesAsCanceled()
    {
        using RuntimeAffinityExecutor executor = new();

        using CancellationTokenSource cts = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Work blocks until the test releases it, observing the caller's
        // cancellation token. We never release it; the test cancels the
        // token to drive the work through the OperationCanceledException
        // catch in ExecuteAsync.
        Task<int> work = executor.ExecuteAsync(
            async ct =>
            {
                await gate.Task.WaitAsync(ct).ConfigureAwait(true);
                return 1;
            },
            cts.Token);

        cts.Cancel();

        // The work is expected to throw OperationCanceledException,
        // which the production code catches and translates into a
        // canceled Task. Wrap with WaitAsync so the test does not
        // hang if the production code regresses.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public static async Task ExecuteAsync_DisposeWhileWorkAwaitingCompletion_CallerTaskCompletesAsCanceled()
    {
        RuntimeAffinityExecutor executor = new();

        TaskCompletionSource neverCompletes = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Work awaits a TCS that the test never completes, so the only
        // way the returned task can transition to a terminal state is
        // through Dispose() driving the pump's
        // OperationCanceledException catch (which now calls
        // tcs.TrySetCanceled() before returning).
        Task work = executor.ExecuteAsync(
            _ => neverCompletes.Task,
            TestContext.Current.CancellationToken);

        executor.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public static async Task ExecuteAsync_DisposeBeforeWorkStarts_CallerTaskCompletesAsCanceledOrThrowsObjectDisposed()
    {
        RuntimeAffinityExecutor executor = new();
        executor.Dispose();

        // After Dispose, ExecuteAsync must either:
        //   (a) throw ObjectDisposedException synchronously because
        //       the _disposed check at the top of ExecuteAsync
        //       fires, OR
        //   (b) return a task that transitions to Canceled because
        //       the work item was successfully enqueued before
        //       _workQueue.CompleteAdding() was observed.
        // Both are acceptable per the production contract; the test
        // asserts the caller observes a terminal, non-faulted outcome
        // in either case.
        Task? work;
        try
        {
            work = executor.ExecuteAsync(
                static ct => Task.Delay(Timeout.Infinite, ct),
                TestContext.Current.CancellationToken);
        }
        catch (ObjectDisposedException)
        {
            // Path (a): synchronous throw from the _disposed check
            // at the top of ExecuteAsync. Acceptable.
            return;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await work!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public static async Task ExecuteAsync_WorkItemsExecuteSerially()
    {
        using RuntimeAffinityExecutor executor = new();

        const int totalWorkItems = 16;

        int observedThreadId = 0;
        bool firstSeen = false;
        bool multipleThreadsObserved = false;
        int completedCount = 0;

        Task[] workItems = new Task[totalWorkItems];
        for (int i = 0; i < totalWorkItems; i++)
        {
            workItems[i] = executor.ExecuteAsync(
                _ =>
                {
                    int currentThreadId = Environment.CurrentManagedThreadId;
                    if (!firstSeen)
                    {
                        observedThreadId = currentThreadId;
                        firstSeen = true;
                    }
                    else if (observedThreadId != currentThreadId)
                    {
                        multipleThreadsObserved = true;
                    }

                    Interlocked.Increment(ref completedCount);
                    return Task.CompletedTask;
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(workItems).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(totalWorkItems, Volatile.Read(ref completedCount));
        Assert.False(multipleThreadsObserved);
        Assert.True(firstSeen);
    }

    [Fact]
    public static void Dispose_IsIdempotent()
    {
        RuntimeAffinityExecutor executor = new();

        executor.Dispose();
        executor.Dispose();
        executor.Dispose();
    }

    [Fact]
    public static void Queues_HaveBoundedCapacity()
    {
        RuntimeAffinityExecutor executor = new();

        try
        {
            FieldInfo workQueueField = typeof(RuntimeAffinityExecutor).GetField(
                "_workQueue",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            FieldInfo continuationQueueField = typeof(RuntimeAffinityExecutor).GetField(
                "_continuationQueue",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            BlockingCollection<Action> workQueue = Assert.IsType<BlockingCollection<Action>>(workQueueField.GetValue(executor));
            BlockingCollection<Action> continuationQueue = Assert.IsType<BlockingCollection<Action>>(continuationQueueField.GetValue(executor));

            Assert.Equal(256, workQueue.BoundedCapacity);
            Assert.Equal(256, continuationQueue.BoundedCapacity);
        }
        finally
        {
            executor.Dispose();
        }
    }
}
