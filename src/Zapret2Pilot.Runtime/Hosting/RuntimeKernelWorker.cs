using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zapret2Pilot.Runtime.Hosting;

/// <summary>
/// <see cref="IHostedService"/> that runs a single dedicated thread
/// (named <c>Z2P-RuntimeKernel</c>) for serialised, single-reader
/// dispatch of kernel-side work items.
/// </summary>
/// <remarks>
/// <para>
/// The worker is a single-reader / multi-writer dispatcher. Callers
/// post <see cref="Func{CancellationToken, Task}"/> or
/// <see cref="Func{CancellationToken, Task{T}}"/> delegates through
/// <see cref="Enqueue(Func{CancellationToken, Task}, CancellationToken)"/>
/// or <see cref="Enqueue{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
/// and a dedicated <see cref="Thread"/> reads them one at a time
/// from a bounded <see cref="Channel{T}"/>. The dedicated thread is
/// the host-level counterpart of the in-process safety primitives:
/// it gives the kernel a single, predictable thread of execution for
/// state-mutating work without depending on a thread-pool scheduler.
/// </para>
/// <para>
/// The work delegate receives a linked cancellation token that
/// combines the caller's <see cref="CancellationToken"/> with the
/// worker's internal token. Cancellation is observed at the
/// <c>await</c> points inside the work delegate; an
/// <see cref="OperationCanceledException"/> is propagated to the
/// caller as a cancelled <see cref="Task"/> (or
/// <see cref="Task{TResult}"/>).
/// </para>
/// <para>
/// <see cref="StopAsync"/> completes the channel writer (so no new
/// items are accepted), cancels the worker token (so the in-flight
/// work observes it), joins the worker thread for at most
/// <c>stopTimeout</c>, and cancels any items still sitting in the
/// channel when the thread exits. <see cref="Dispose"/> routes
/// through <see cref="StopAsync"/> and then disposes the internal
/// <see cref="CancellationTokenSource"/>.
/// </para>
/// <para>
/// The host is not designed for concurrent <see cref="StartAsync"/>
/// or <see cref="StopAsync"/> calls. <see cref="IHostedService"/>
/// guarantees a single start / stop pair, and <see cref="Dispose"/>
/// is idempotent.
/// </para>
/// </remarks>
public sealed class RuntimeKernelWorker : IHostedService, IDisposable
{
    private readonly ILogger<RuntimeKernelWorker> logger;
    private readonly TimeSpan stopTimeout;
    private readonly Channel<WorkItem> channel;
    private readonly CancellationTokenSource workerCts = new();
    private Thread? workerThread;

    // 0 = running, 1 = stopping/stopped. Toggled with Interlocked to
    // make the StopAsync entry point safe under concurrent
    // StartAsync / StopAsync / Dispose calls.
    private int stoppingFlag;

    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="RuntimeKernelWorker"/>.
    /// </summary>
    /// <param name="logger">
    /// Logger that receives structured events for unhandled
    /// exceptions raised by work items and for the
    /// "worker thread did not exit within timeout" path.
    /// Pass <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger{T}.Instance"/>
    /// in tests.
    /// </param>
    /// <param name="stopTimeout">
    /// Maximum time to wait for the worker thread to join during
    /// <see cref="StopAsync"/>. Defaults to 5 seconds when
    /// <c>null</c>.
    /// </param>
    public RuntimeKernelWorker(ILogger<RuntimeKernelWorker> logger, TimeSpan? stopTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        this.logger = logger;
        this.stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(5);
        channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>
    /// Managed thread id of the dedicated worker thread, or <c>0</c>
    /// if the thread has not started or has already been joined.
    /// Exposed as <c>internal</c> for tests.
    /// </summary>
    internal int WorkerThreadId => workerThread?.ManagedThreadId ?? 0;

    /// <summary>
    /// Starts the dedicated worker thread. Returns immediately; the
    /// thread runs in the background until <see cref="StopAsync"/>
    /// is called.
    /// </summary>
    /// <param name="cancellationToken">
    /// Observed before the thread is created. The worker does not
    /// honour cancellation while the thread is starting.
    /// </param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        workerThread = new Thread(RunWorkerLoop)
        {
            Name = "Z2P-RuntimeKernel",
            IsBackground = true,
        };
        workerThread.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the worker. Completes the channel writer, cancels the
    /// worker token, joins the worker thread for at most
    /// <c>stopTimeout</c>, and cancels any items still sitting in
    /// the channel. Idempotent: subsequent calls return immediately.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token observed while waiting for the worker
    /// thread to join. The internal <c>Thread.Join(stopTimeout)</c>
    /// is not itself cancellable, so the token primarily
    /// short-circuits the <c>await</c> wrapper.
    /// </param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref stoppingFlag, 1) != 0)
        {
            return;
        }

        channel.Writer.Complete();
        workerCts.Cancel();

        Thread? thread = Interlocked.Exchange(ref workerThread, null);
        if (thread is not null && thread.IsAlive)
        {
            await Task.Run(() => thread.Join(stopTimeout), cancellationToken).ConfigureAwait(false);
            if (thread.IsAlive)
            {
                logger.LogCritical(
                    "RuntimeKernelWorker worker thread did not exit within {Timeout}.",
                    stopTimeout);
            }
        }

        while (channel.Reader.TryRead(out WorkItem? item))
        {
            item.Cancel();
        }
    }

    /// <summary>
    /// Disposes the worker. Sets the <c>disposed</c> flag, calls
    /// <see cref="StopAsync"/>, and disposes the internal
    /// <see cref="CancellationTokenSource"/>. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        workerCts.Dispose();
    }

    /// <summary>
    /// Posts a fire-and-forget work item to the worker thread.
    /// </summary>
    /// <param name="work">
    /// The work delegate. Invoked on the dedicated worker thread
    /// with a linked cancellation token that combines the caller's
    /// <paramref name="cancellationToken"/> and the worker's
    /// internal cancellation source.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed by the work delegate. The token
    /// is honoured inside the work via the linked token.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when <paramref name="work"/>
    /// completes, or that is cancelled (with
    /// <see cref="TaskCanceledException"/>) if the worker is
    /// stopping (or has been disposed) when the call is made, or if
    /// the channel refuses the write.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="work"/> is <c>null</c>.
    /// </exception>
    public Task Enqueue(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work, nameof(work));

        if (Volatile.Read(ref stoppingFlag) != 0)
        {
            return Task.FromCanceled(
                cancellationToken.IsCancellationRequested
                    ? cancellationToken
                    : new CancellationToken(canceled: true));
        }

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkItem item = new(
            Work: async _ =>
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                    workerCts.Token,
                    cancellationToken);
                try
                {
                    await work(linked.Token).ConfigureAwait(false);
                    tcs.SetResult();
                }
                catch (OperationCanceledException ex)
                {
                    tcs.SetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            },
            Description: "Enqueue",
            Cancel: () => tcs.TrySetCanceled());

        if (!channel.Writer.TryWrite(item))
        {
            // The channel was completed between the stoppingFlag
            // check and the write (typically because StopAsync
            // completed in between). Cancel the freshly-built
            // tcs and let the caller observe the cancellation.
            item.Cancel();
        }

        return tcs.Task;
    }

    /// <summary>
    /// Posts a value-returning work item to the worker thread.
    /// </summary>
    /// <typeparam name="T">Result type produced by <paramref name="work"/>.</typeparam>
    /// <param name="work">
    /// The work delegate. Invoked on the dedicated worker thread
    /// with a linked cancellation token that combines the caller's
    /// <paramref name="cancellationToken"/> and the worker's
    /// internal cancellation source.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed by the work delegate.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that completes with the value
    /// returned by <paramref name="work"/>, or that is faulted or
    /// cancelled on failure.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="work"/> is <c>null</c>.
    /// </exception>
    public Task<T> Enqueue<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work, nameof(work));

        if (Volatile.Read(ref stoppingFlag) != 0)
        {
            return Task.FromCanceled<T>(
                cancellationToken.IsCancellationRequested
                    ? cancellationToken
                    : new CancellationToken(canceled: true));
        }

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkItem item = new(
            Work: async _ =>
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                    workerCts.Token,
                    cancellationToken);
                try
                {
                    T result = await work(linked.Token).ConfigureAwait(false);
                    tcs.SetResult(result);
                }
                catch (OperationCanceledException ex)
                {
                    tcs.SetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            },
            Description: $"Enqueue<{typeof(T).Name}>",
            Cancel: () => tcs.TrySetCanceled());

        if (!channel.Writer.TryWrite(item))
        {
            item.Cancel();
        }

        return tcs.Task;
    }

    private void RunWorkerLoop()
    {
        try
        {
            while (!workerCts.Token.IsCancellationRequested)
            {
                WorkItem item;
                try
                {
                    item = channel.Reader.ReadAsync(workerCts.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                try
                {
                    item.Work(workerCts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // expected during shutdown
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "RuntimeKernelWorker: unhandled exception in work item '{Description}'.",
                        item.Description);
                }
            }
        }
        finally
        {
            // Drain anything still in the channel. This runs in
            // both graceful (channel completed cleanly) and
            // cancellation paths.
            while (channel.Reader.TryRead(out WorkItem? item))
            {
                item.Cancel();
            }
        }
    }

    /// <summary>
    /// Internal representation of a queued unit of work. Holds the
    /// delegate that will run on the dedicated thread, a short
    /// human-readable description for log messages, and a
    /// <see cref="Cancel"/> callback that the drain paths use to
    /// signal cancellation to the originating
    /// <see cref="TaskCompletionSource"/>.
    /// </summary>
    private sealed record WorkItem(
        Func<CancellationToken, Task> Work,
        string Description,
        Action Cancel);
}
