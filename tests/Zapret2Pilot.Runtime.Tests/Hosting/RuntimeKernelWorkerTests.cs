using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Tests.Hosting;

// Test method names deliberately use snake_case (e.g. Enqueue_RunsOnDedicatedThread)
// to make scenarios readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// xUnit tests for <see cref="RuntimeKernelWorker"/> (milestone 0.0.20,
/// packet 1).
/// </summary>
/// <remarks>
/// <para>
/// Every test creates a fresh <see cref="RuntimeKernelWorker"/>,
/// starts it manually, runs the scenario, then stops and disposes
/// the worker inside a <c>finally</c> block. <c>StopAsync</c> is
/// called explicitly so a failing assertion in the scenario body
/// still tears the dedicated thread down deterministically; the
/// trailing <c>Dispose</c> call then disposes the internal
/// <see cref="CancellationTokenSource"/>.
/// </para>
/// <para>
/// Tests use <see cref="NullLogger{T}.Instance"/> because the
/// worker is only expected to log on two paths (worker thread did
/// not exit in time, unhandled exception inside a work item) and
/// neither path is exercised by these tests.
/// </para>
/// <para>
/// xunit v3 requires passing <see cref="TestContext.Current.CancellationToken"/>
/// to methods that accept a <see cref="CancellationToken"/>
/// (rule xUnit1051). The tests therefore capture it once per
/// test method in a local <c>cancellationToken</c> variable and
/// forward it to every <see cref="IHostedService"/> /
/// <see cref="RuntimeKernelWorker.Enqueue(Func{CancellationToken, Task}, CancellationToken)"/>
/// / <see cref="RuntimeKernelWorker.Enqueue{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
/// / <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> call.
/// </para>
/// </remarks>
public sealed class RuntimeKernelWorkerTests
{
    [Fact]
    public static async Task Enqueue_RunsOnDedicatedThread()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            int callerThreadId = Environment.CurrentManagedThreadId;
            int dedicatedThreadId = worker.WorkerThreadId;
            Assert.NotEqual(0, dedicatedThreadId);
            Assert.NotEqual(callerThreadId, dedicatedThreadId);

            int workThreadId = 0;
            await worker.Enqueue(
                _ =>
                {
                    workThreadId = Environment.CurrentManagedThreadId;
                    return Task.CompletedTask;
                },
                cancellationToken);

            Assert.Equal(dedicatedThreadId, workThreadId);
            Assert.NotEqual(callerThreadId, workThreadId);
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }
    }

    [Fact]
    public static async Task Enqueue_ReturnsResult()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            int result = await worker.Enqueue(_ => Task.FromResult(42), cancellationToken);
            Assert.Equal(42, result);

            string message = await worker.Enqueue(ct => Task.FromResult("hello"), cancellationToken);
            Assert.Equal("hello", message);
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }
    }

    [Fact]
    public static async Task Enqueue_PropagatesException()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            // Non-generic Enqueue: exception surfaces to the awaited Task.
            InvalidOperationException nonGeneric = await Assert.ThrowsAsync<InvalidOperationException>(
                () => worker.Enqueue(_ => throw new InvalidOperationException("boom"), cancellationToken));
            Assert.Equal("boom", nonGeneric.Message);

            // Generic Enqueue: same propagation contract.
            InvalidOperationException generic = await Assert.ThrowsAsync<InvalidOperationException>(
                () => worker.Enqueue<int>(_ => throw new InvalidOperationException("typed-boom"), cancellationToken));
            Assert.Equal("typed-boom", generic.Message);
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }
    }

    [Fact]
    public static async Task StopAsync_DrainsPendingWork()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            // Enqueue an in-flight work item that blocks until its
            // cancellation token fires.
            TaskCompletionSource workStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task inFlightTask = worker.Enqueue(
                async ct =>
                {
                    workStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                },
                cancellationToken);

            // Wait for the worker thread to actually pick the item up.
            await workStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            // Enqueue a second item that never gets to run because
            // the in-flight item is still in progress. It is still
            // queued at this point; StopAsync must drain it.
            Task queuedTask = worker.Enqueue(_ => Task.CompletedTask, cancellationToken);

            // Stop the worker. The in-flight task is cancelled by
            // workerCts, the queued task is drained (and cancelled)
            // by the worker loop's finally block and by StopAsync's
            // own drain loop.
            await worker.StopAsync(cancellationToken);

            await Assert.ThrowsAsync<TaskCanceledException>(() => inFlightTask);
            await Assert.ThrowsAsync<TaskCanceledException>(() => queuedTask);
        }
        finally
        {
            worker.Dispose();
        }
    }

    [Fact]
    public static async Task Cancellation_Honored()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            TaskCompletionSource workStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task task = worker.Enqueue(
                async ct =>
                {
                    workStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                },
                cts.Token);

            // Make sure the work has actually started before we
            // cancel, otherwise we could race the worker thread.
            await workStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }
    }

    [Fact]
    public static async Task MultipleWorkItems_Isolated()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            // Pre-fault: an exception in the middle of the queue
            // must not derail later items. The worker is
            // single-reader, so a faulted work item only affects
            // its own Task; subsequent work items run normally.
            Task first = worker.Enqueue(_ => Task.CompletedTask, cancellationToken);
            Task<int> faulted = worker.Enqueue<int>(
                _ => throw new InvalidOperationException("middle-boom"),
                cancellationToken);
            Task third = worker.Enqueue(_ => Task.CompletedTask, cancellationToken);

            await first;

            InvalidOperationException propagated = await Assert.ThrowsAsync<InvalidOperationException>(
                () => faulted);
            Assert.Equal("middle-boom", propagated.Message);

            await third;
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }
    }
}

#pragma warning restore CA1707
