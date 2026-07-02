using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Tests.Hosting;

// Test method names deliberately use snake_case (e.g. Enqueue_ReturnsImmediately)
// to make scenarios readable in the test runner. Suppress CA1707 locally for this file.
#pragma warning disable CA1707 // Identifiers should not contain underscores

/// <summary>
/// xUnit tests that prove <see cref="RuntimeKernelWorker.Enqueue"/>
/// returns control to its caller immediately, never blocking the
/// calling thread on the in-flight work item (milestone 0.0.20,
/// packet 3, UI non-blocking contract).
/// </summary>
/// <remarks>
/// <para>
/// The work item deliberately awaits a measurable delay (100 ms
/// by default) inside the worker thread, while the test asserts
/// that the <see cref="RuntimeKernelWorker.Enqueue{T}"/>
/// call itself returns in well under that delay (10 ms by
/// default). A regression that caused the worker to block the
/// caller would trip the upper bound immediately.
/// </para>
/// <para>
/// The tests do NOT block the test thread waiting for the work
/// item inside the enqueue call. They use a
/// <see cref="TaskCompletionSource"/> signalled from the worker
/// thread and await it after returning from <c>Enqueue</c>, so
/// the timing measurement reflects the worker's contract and
/// not the test's own scheduling.
/// </para>
/// <para>
/// xunit v3 requires passing <see cref="TestContext.Current.CancellationToken"/>
/// to methods that accept a <see cref="CancellationToken"/>
/// (rule xUnit1051). The tests therefore capture it once per
/// test method in a local <c>cancellationToken</c> variable and
/// forward it to every <see cref="IHostedService"/> /
/// <see cref="RuntimeKernelWorker.Enqueue{T}"/> /
/// <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> call.
/// </para>
/// </remarks>
public sealed class RuntimeKernelWorkerUiNonBlockingTests
{
    /// <summary>
    /// Upper bound (in milliseconds) for the <c>Enqueue</c> call to
    /// return. The work item awaits 100 ms; the enqueue call must
    /// return at least 10× faster. A regression that lets the
    /// worker block the caller would push this past the bound on
    /// any reasonable machine.
    /// </summary>
    private const int EnqueueReturnBudgetMs = 10;

    /// <summary>
    /// The work-item delay in milliseconds. Long enough to be
    /// clearly distinguishable from <see cref="EnqueueReturnBudgetMs"/>
    /// on a slow CI runner, short enough to keep the test fast.
    /// </summary>
    private const int WorkItemDelayMs = 100;

    /// <summary>
    /// Generous timeout used to wait for the in-flight work to
    /// finish before tearing the worker down. Independent of the
    /// non-blocking assertion.
    /// </summary>
    private static readonly TimeSpan WorkItemCompletionTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public static async Task Enqueue_ReturnsImmediately_WithoutBlockingCaller()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            TaskCompletionSource workStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

            // Measure the Enqueue call itself: the work item
            // awaits 100 ms on the worker thread, so the
            // expected caller-side latency is microseconds. Any
            // blocking regression is caught by the >10 ms bound.
            long startTimestamp = Stopwatch.GetTimestamp();
            Task<int> workTask = worker.Enqueue(
                async ct =>
                {
                    workStarted.TrySetResult();
                    await Task.Delay(WorkItemDelayMs, ct).ConfigureAwait(false);
                    return 1;
                },
                cancellationToken);
            long elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).Milliseconds;

            Assert.True(
                elapsedMs < EnqueueReturnBudgetMs,
                $"Enqueue returned in {elapsedMs} ms; expected < {EnqueueReturnBudgetMs} ms.");

            // Now wait for the work to actually run and complete,
            // on the test thread, after the enqueue has returned.
            // This is the assertion the brief calls for: the test
            // thread is free to do other work while the kernel
            // executes the work item.
            await workStarted.Task.WaitAsync(WorkItemCompletionTimeout, cancellationToken);
            int result = await workTask.WaitAsync(WorkItemCompletionTimeout, cancellationToken);
            Assert.Equal(1, result);
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }
    }

    [Fact]
    public static async Task Enqueue_DoesNotBlockCaller_WhenWorkItemAwaitsForever()
    {
        // A second scenario that uses an effectively infinite
        // work-item delay. This catches the most insidious
        // regression: a caller-side wait that would only ever
        // be noticed when a real work item takes more than a
        // few milliseconds. We cancel the work item via its
        // own token after the timing measurement to drive
        // the worker to a deterministic end state.
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RuntimeKernelWorker worker = new(NullLogger<RuntimeKernelWorker>.Instance);
        try
        {
            await worker.StartAsync(cancellationToken);

            TaskCompletionSource workStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource workItemCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            long startTimestamp = Stopwatch.GetTimestamp();
            Task workTask = worker.Enqueue(
                async ct =>
                {
                    workStarted.TrySetResult();
                    // No try/catch: the OperationCanceledException
                    // raised by Task.Delay must propagate to the
                    // worker, which surfaces it on the task
                    // returned by Enqueue as a TaskCanceledException.
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                },
                workItemCts.Token);
            long elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).Milliseconds;

            Assert.True(
                elapsedMs < EnqueueReturnBudgetMs,
                $"Enqueue returned in {elapsedMs} ms; expected < {EnqueueReturnBudgetMs} ms.");

            await workStarted.Task.WaitAsync(WorkItemCompletionTimeout, cancellationToken);

            // Cancel the work item so the worker can shut down
            // promptly when StopAsync is called and so the task
            // returned by Enqueue transitions to Canceled.
            workItemCts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(
                async () => await workTask.WaitAsync(WorkItemCompletionTimeout, cancellationToken));
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }
    }
}

#pragma warning restore CA1707
