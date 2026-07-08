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
/// xUnit v3 tests for <see cref="RuntimeAffinityOwner"/>. Cover the
/// sync-only thread-affinity contract: bounded command queue (256),
/// <see cref="RuntimeAffinityOwner.OwnerThreadId"/> stability,
/// serialised execution, cancellation propagation, disposal
/// idempotency and the canonical post-disposal
/// <see cref="ObjectDisposedException"/>.
/// </summary>
public sealed class RuntimeAffinityOwnerTests
{
    [Fact]
    public static async Task ExecuteAsync_WorkCompletes_ReturnsResult()
    {
        using RuntimeAffinityOwner owner = new();

        int result = await owner.ExecuteAsync(
            static _ => 42,
            TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public static async Task ExecuteAsync_WorkItemsExecuteSerially()
    {
        using RuntimeAffinityOwner owner = new();

        const int totalWorkItems = 16;

        int observedThreadId = 0;
        bool firstSeen = false;
        bool multipleThreadsObserved = false;
        int completedCount = 0;

        Task[] workItems = new Task[totalWorkItems];
        for (int i = 0; i < totalWorkItems; i++)
        {
            workItems[i] = owner.ExecuteAsync(
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
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(workItems).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(totalWorkItems, Volatile.Read(ref completedCount));
        Assert.False(multipleThreadsObserved);
        Assert.True(firstSeen);
    }

    [Fact]
    public static async Task ExecuteAsync_AllWorkRunsOnOwnerThread()
    {
        using RuntimeAffinityOwner owner = new();

        // The owner thread ID is captured inside RunLoop, before the
        // pump starts. WaitAsync(0) yields once so any racing
        // ExecuteAsync callers do not enqueue before _ownerThreadId
        // is initialised; in practice the field is set before
        // GetConsumingEnumerable blocks, so the first call already
        // observes the value.
        await Task.Yield();

        int ownerThreadId = owner.OwnerThreadId;
        Assert.NotEqual(0, ownerThreadId);

        int workItems = 8;
        int observedOffThread = 0;

        Task[] tasks = new Task[workItems];
        for (int i = 0; i < workItems; i++)
        {
            tasks[i] = owner.ExecuteAsync(
                _ =>
                {
                    if (Environment.CurrentManagedThreadId != ownerThreadId)
                    {
                        Interlocked.Increment(ref observedOffThread);
                    }
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, observedOffThread);
    }

    [Fact]
    public static async Task ExecuteAsync_CallerTokenCanceled_CompletesAsCanceled()
    {
        using RuntimeAffinityOwner owner = new();

        using CancellationTokenSource cts = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Work blocks until the test releases it, observing the
        // caller's cancellation token. The test never releases the
        // gate; instead it cancels cts, which makes the work throw
        // OperationCanceledException. The owner catches it inside
        // the command delegate and completes the TCS as Canceled.
        Task<int> work = owner.ExecuteAsync(
            ct =>
            {
                gate.Task.Wait(ct);
                return 1;
            },
            cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public static async Task ExecuteAsync_DisposeWhileWorkPending_CompletesAsCanceled()
    {
        RuntimeAffinityOwner owner = new();

        // Enqueue a work item that completes synchronously, so the
        // first command is consumed before Dispose. Then Dispose;
        // any further ExecuteAsync call would normally succeed
        // because the pump drains queued items, but a freshly
        // submitted work item may already see _disposed = 1 (the
        // disposal flag is set before CompleteAdding). This test
        // pins the cancellation contract for that race.
        TaskCompletionSource fence = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task work = owner.ExecuteAsync(
            _ =>
            {
                // Signal that the first command is running.
                fence.TrySetResult();
            },
            TestContext.Current.CancellationToken);

        await fence.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Now that the pump is idle, dispose and immediately
        // submit a new work item. Because Dispose sets _disposed =
        // 1 before joining, the new ExecuteAsync must either
        // throw ObjectDisposedException synchronously or, if the
        // work item is already enqueued, complete the TCS as
        // Canceled.
        owner.Dispose();

        Task? pending;
        try
        {
            pending = owner.ExecuteAsync(
                _ =>
                {
                    // Body should never run: Dispose set the
                    // _disposed flag and the command delegate
                    // fast-paths to TrySetCanceled before invoking
                    // the user delegate.
                },
                TestContext.Current.CancellationToken);
        }
        catch (ObjectDisposedException)
        {
            // Acceptable: the ObjectDisposedException.ThrowIf check
            // at the top of ExecuteAsync fired before the enqueue.
            return;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pending!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public static void ExecuteAsync_ThrowsAfterDispose()
    {
        RuntimeAffinityOwner owner = new();
        owner.Dispose();

        // After Dispose, the first call to ExecuteAsync must
        // throw ObjectDisposedException synchronously. If the
        // owner is in a race where _disposed = 1 is observed
        // AFTER the _commandQueue.Add succeeds, the resulting
        // task may complete as Canceled; we only assert that the
        // synchronous ObjectDisposedException contract holds.
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = owner.ExecuteAsync(
                static _ => 1,
                TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public static void Dispose_IsIdempotent()
    {
        RuntimeAffinityOwner owner = new();

        owner.Dispose();
        owner.Dispose();
        owner.Dispose();
    }

    [Fact]
    public static async Task OwnerThreadId_IsStable()
    {
        using RuntimeAffinityOwner owner = new();

        // Read the ID once, schedule several work items, read it
        // again. All three reads must return the same value.
        int first = owner.OwnerThreadId;
        Assert.NotEqual(0, first);

        for (int i = 0; i < 4; i++)
        {
            await owner.ExecuteAsync(
                _ => { },
                TestContext.Current.CancellationToken);
        }

        int last = owner.OwnerThreadId;
        Assert.Equal(first, last);
    }

    [Fact]
    public static void Queue_HasBoundedCapacity()
    {
        RuntimeAffinityOwner owner = new();

        try
        {
            FieldInfo queueField = typeof(RuntimeAffinityOwner).GetField(
                "_commandQueue",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            BlockingCollection<Action> queue = Assert.IsType<BlockingCollection<Action>>(queueField.GetValue(owner));

            Assert.Equal(256, queue.BoundedCapacity);
        }
        finally
        {
            owner.Dispose();
        }
    }

    [Fact]
    public static void ExecuteAsync_NullWork_ThrowsArgumentNullException()
    {
        using RuntimeAffinityOwner owner = new();

        // Generic overload.
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = owner.ExecuteAsync<int>(
                work: null!,
                TestContext.Current.CancellationToken);
        });

        // Non-generic overload.
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = owner.ExecuteAsync(
                work: null!,
                TestContext.Current.CancellationToken);
        });
    }
}
#pragma warning restore CA1707 // Identifiers should not contain underscores
