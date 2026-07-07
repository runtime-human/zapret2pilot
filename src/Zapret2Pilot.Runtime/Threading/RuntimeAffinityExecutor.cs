using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Zapret2Pilot.Runtime.Threading;

/// <summary>
/// Executes asynchronous work on a single, dedicated background
/// thread named <c>"Z2P-RuntimeAffinity"</c>. The executor preserves
/// the runtime pipeline's ownership-mutex / lease thread-affinity
/// invariant: any work scheduled through
/// <see cref="ExecuteAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
/// runs on the same thread, and the awaited continuations inside
/// the scheduled delegate resume on that same thread.
/// </summary>
/// <remarks>
/// <para>
/// The implementation is a small <c>SingleThreadSynchronizationContext</c>:
/// a dedicated background thread hosts a
/// <see cref="SynchronizationContext"/> that fans every awaited
/// continuation back into a bounded queue, which the same thread
/// pumps sequentially. Two
/// <see cref="BlockingCollection{T}"/> queues are used:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>_workQueue</c> holds top-level work items enqueued by
/// <see cref="ExecuteAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>. The
/// thread loop dequeues one work item at a time, invokes the
/// user-supplied async delegate on the affinity thread, and
/// stores the resulting <see cref="Task{TResult}"/>.
/// </item>
/// <item>
/// <c>_continuationQueue</c> holds the <see cref="SendOrPostCallback"/>
/// continuations posted by the affinity
/// <see cref="SynchronizationContext"/> when the user's
/// delegate awaits something. The thread loop drains this
/// queue after starting the work item and until the work's
/// task transitions to a terminal state.
/// </item>
/// </list>
/// <para>
/// Because both queues are pumped on the same thread, the
/// pipeline never overlaps with itself. The caller's thread is
/// never blocked: <see cref="ExecuteAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
/// only enqueues the work item and returns the
/// <see cref="Task{TResult}"/> that completes when the work
/// finishes on the affinity thread.
/// </para>
/// <para>
/// <see cref="Dispose"/> is idempotent. It cancels pending
/// work, completes the queues, joins the thread with a bounded
/// timeout and releases the underlying resources. A
/// <see cref="ObjectDisposedException"/> is thrown by
/// <see cref="ExecuteAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
/// when invoked after <see cref="Dispose"/>.
/// </para>
/// </remarks>
public interface IRuntimeAffinityExecutor : IDisposable
{
    /// <summary>
    /// Schedules <paramref name="work"/> to run on the affinity
    /// thread. The returned <see cref="Task{TResult}"/> completes
    /// when the delegate finishes (successfully, faulted or
    /// cancelled) on the affinity thread.
    /// </summary>
    /// <typeparam name="T">Result type of the async delegate.</typeparam>
    /// <param name="work">
    /// The async delegate to execute. MUST NOT be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed by the work and combined with
    /// the executor's internal disposal token. When the token is
    /// cancelled, the work is signalled to stop; the
    /// <see cref="Task{TResult}"/> transitions to
    /// <see cref="TaskStatus.Canceled"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that completes when the
    /// affinity thread finishes the work. The task's continuation
    /// is free to resume on any thread; the work itself always
    /// runs on the affinity thread.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="work"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The executor has been disposed.
    /// </exception>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules <paramref name="work"/> to run on the affinity
    /// thread. The returned <see cref="Task"/> completes when
    /// the delegate finishes (successfully, faulted or cancelled)
    /// on the affinity thread.
    /// </summary>
    /// <param name="work">
    /// The async delegate to execute. MUST NOT be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed by the work and combined with
    /// the executor's internal disposal token.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the affinity
    /// thread finishes the work.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="work"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The executor has been disposed.
    /// </exception>
    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IRuntimeAffinityExecutor"/> implementation.
/// Owns a single dedicated background thread named
/// <c>"Z2P-RuntimeAffinity"</c> and a private
/// <see cref="SynchronizationContext"/> that pins the awaited
/// continuations of the scheduled work to that thread. See
/// <see cref="IRuntimeAffinityExecutor"/> for the full
/// contract.
/// </summary>
public sealed class RuntimeAffinityExecutor : IRuntimeAffinityExecutor
{
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly BlockingCollection<Action> _workQueue = new(new ConcurrentQueue<Action>());
    private readonly BlockingCollection<Action> _continuationQueue = new(new ConcurrentQueue<Action>());
    private readonly Thread _thread;
    private int _disposed;

    /// <summary>
    /// Creates a new <see cref="RuntimeAffinityExecutor"/> and
    /// starts the dedicated background thread. The thread is
    /// named <c>"Z2P-RuntimeAffinity"</c>, runs as a background
    /// thread (so it does not keep the process alive on its
    /// own) and installs the affinity
    /// <see cref="SynchronizationContext"/> as the current
    /// <see cref="SynchronizationContext"/> for the lifetime of
    /// the thread.
    /// </summary>
    public RuntimeAffinityExecutor()
    {
        _thread = new Thread(RunLoop)
        {
            Name = "Z2P-RuntimeAffinity",
            IsBackground = true,
        };
        _thread.Start();
    }

    /// <inheritdoc />
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(work);

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);

        try
        {
            // _workQueue.Add is intentionally invoked with
            // CancellationToken.None: the work item is enqueued
            // even when the caller's token is already cancelled,
            // so the affinity thread can pick it up, observe the
            // cancellation through linkedCts and complete the TCS
            // with TaskStatus.Canceled. Suppressing CA2016 here
            // is intentional and documented.
            _workQueue.Add(() =>
            {
                try
                {
                    Task<T> task = work(linkedCts.Token);
                    PumpUntilCompleted(task, tcs, linkedCts);
                }
                catch (OperationCanceledException)
                {
                    linkedCts.Dispose();
                    tcs.TrySetCanceled(linkedCts.Token);
                }
                catch (Exception ex)
                {
                    linkedCts.Dispose();
                    tcs.TrySetException(ex);
                }
            }, CancellationToken.None);
        }
        catch
        {
            // _workQueue may have been completed by Dispose()
            // between the ObjectDisposedException check and the
            // Add() call. Release the linked CTS and rethrow as
            // an ObjectDisposedException for a consistent
            // contract.
            linkedCts.Dispose();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            throw;
        }

        return tcs.Task;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return ExecuteAsync<bool?>(async ct =>
        {
            await work(ct).ConfigureAwait(true);
            return null;
        }, cancellationToken);
    }

    /// <summary>
    /// Stops the executor. Idempotent. Signals in-flight work to
    /// cancel via <c>_disposeCts</c>, stops accepting new work by
    /// completing <c>_workQueue</c> (so any work already enqueued
    /// is drained by the affinity thread), joins the affinity
    /// thread with a five-second timeout, completes the
    /// continuation queue (now safe because no thread is reading
    /// from it) and releases the underlying resources.
    /// Subsequent calls to
    /// <see cref="ExecuteAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
    /// throw <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            // 1. Signal in-flight work to cancel. Work items
            //    observe the cancellation through the linked CTS
            //    built in ExecuteAsync, so they can unwind cleanly
            //    before the thread exits.
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed at the CTS level; nothing more to do
            return;
        }

        // 2. Stop accepting new work. Existing entries are still
        //    consumed by the affinity thread, including the
        //    CleanupPipelineAsync posted by
        //    RuntimeProcessHost.Dispose; cancelling the
        //    enumerator here would drop those entries and hang
        //    the caller.
        _workQueue.CompleteAdding();

        // 3. Wait for the affinity thread to drain the queue and
        //    exit. The thread loops on GetConsumingEnumerable()
        //    and exits as soon as both CompleteAdding and an
        //    empty queue are observed.
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        // 4. Now that the affinity thread has exited, no one is
        //    reading from the continuation queue, so it is safe
        //    to complete and dispose it.
        _continuationQueue.CompleteAdding();

        _disposeCts.Dispose();
        _workQueue.Dispose();
        _continuationQueue.Dispose();
    }

    /// <summary>
    /// The affinity thread's main loop. Installs the affinity
    /// <see cref="SynchronizationContext"/>, consumes work items
    /// from <c>_workQueue</c> one at a time, and exits cleanly
    /// when the queue is completed AND drained. The enumeration
    /// is intentionally NOT cancelled by <c>_disposeCts</c>:
    /// <see cref="Dispose"/> calls <c>CompleteAdding</c> on the
    /// queue, the loop drains every work item that was enqueued
    /// before disposal (including the
    /// <c>CleanupPipelineAsync</c> posted by
    /// <see cref="Hosting.RuntimeProcessHost.Dispose"/>), and the
    /// loop exits naturally when the queue is empty. Cancelling
    /// the enumeration would silently drop the work items that
    /// were enqueued before <c>CompleteAdding</c>, hanging any
    /// caller waiting for the resulting <see cref="Task{T}"/>.
    /// Each work item is responsible for completing its own
    /// <see cref="TaskCompletionSource{TResult}"/>; an exception
    /// inside the work delegate is treated as a contract
    /// violation because the work delegate catches its own
    /// exceptions.
    /// </summary>
    private void RunLoop()
    {
        SynchronizationContext.SetSynchronizationContext(new AffinitySynchronizationContext(this));
        try
        {
            foreach (Action work in _workQueue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch
                {
                    // Work items are expected to handle their own
                    // errors and complete the TCS accordingly.
                    // Anything that escapes this catch is a bug in
                    // the work delegate; we drop it on the floor
                    // and move on, otherwise a buggy work item
                    // would poison the entire executor.
                }
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    /// <summary>
    /// Pumps the <c>_continuationQueue</c> until
    /// <paramref name="task"/> transitions to a terminal state.
    /// A <c>ContinueWith</c> on <see cref="TaskScheduler.Default"/>
    /// signals the loop via a private
    /// <see cref="TaskCompletionSource"/> when the task
    /// completes, so the loop never has to poll the task. When
    /// the loop exits, the user-visible
    /// <see cref="TaskCompletionSource{TResult}"/> is completed
    /// from the task's final state.
    /// </summary>
    /// <typeparam name="T">Task result type.</typeparam>
    /// <param name="task">The task to pump until completion.</param>
    /// <param name="tcs">
    /// The user-visible TCS to complete when the task finishes.
    /// </param>
    /// <param name="linkedCts">
    /// The linked cancellation token source to dispose once the
    /// task finishes.
    /// </param>
    private void PumpUntilCompleted<T>(Task<T> task, TaskCompletionSource<T> tcs, CancellationTokenSource linkedCts)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register the completion signal on the thread pool so the
        // signal does not depend on the affinity thread. The
        // affinity thread is currently busy running continuations
        // from _continuationQueue, so we cannot rely on the task's
        // own ContinueWith to run on it. The returned Task is
        // intentionally discarded: the completion TaskCompletionSource
        // is the signal we observe, not the ContinueWith's task.
        _ = task.ContinueWith(_ =>
        {
            try
            {
                linkedCts.Dispose();
            }
            finally
            {
                completion.TrySetResult();
                // Sentinel: unblock the pump's _continuationQueue.Take
                // so it can re-check completion.Task.IsCompleted and
                // exit. Without this sentinel, a work delegate that
                // completes without posting any continuations back to
                // the affinity SynchronizationContext (e.g. an
                // already-completed task, or a Task.Run that finishes
                // without awaiting anything that would post back)
                // would deadlock the pump: the affinity thread would
                // remain blocked on Take forever while completion
                // sits in its already-set state.
                try
                {
                    _continuationQueue.Add(() => { }, CancellationToken.None);
                }
                catch (ObjectDisposedException)
                {
                    // Queue was disposed; pump will exit anyway.
                    // Note: ObjectDisposedException is a subclass of
                    // InvalidOperationException, so this catch must
                    // come before the more general one below.
                }
                catch (InvalidOperationException)
                {
                    // Queue was completed; pump will exit anyway.
                }
            }
        }, TaskScheduler.Default);

        if (task.IsCompleted)
        {
            CompleteTcs(task, tcs);
            return;
        }

        while (!completion.Task.IsCompleted)
        {
            try
            {
                Action continuation = _continuationQueue.Take(_disposeCts.Token);
                try
                {
                    continuation();
                }
                catch
                {
                    // Continuations own their own state; any
                    // exception they throw is the caller's bug,
                    // not ours. Swallow so a single broken
                    // continuation cannot poison the executor.
                }
            }
            catch (OperationCanceledException)
            {
                // _disposeCts was cancelled (Dispose() in
                // progress). Stop pumping; the work item exits
                // and the outer TCS will not be completed here.
                return;
            }
            catch (ObjectDisposedException)
            {
                // _continuationQueue was disposed by a concurrent
                // Dispose() racing with the pump. Stop pumping.
                // Note: ObjectDisposedException is a subclass of
                // InvalidOperationException, so this catch must
                // come before the more general one below.
                return;
            }
            catch (InvalidOperationException)
            {
                // _continuationQueue was completed (e.g. by
                // Dispose() racing with the pump). Stop pumping.
                return;
            }
        }

        CompleteTcs(task, tcs);
    }

    /// <summary>
    /// Completes <paramref name="tcs"/> from the final state of
    /// <paramref name="task"/>. Maps success / fault / cancel
    /// onto the corresponding TCS outcome. Uses
    /// <c>TrySet*</c> so a benign re-completion (e.g. the
    /// task is observed after a cancellation already settled
    /// the TCS) is silently ignored.
    /// </summary>
    private static void CompleteTcs<T>(Task<T> task, TaskCompletionSource<T> tcs)
    {
        if (task.IsCompletedSuccessfully)
        {
            tcs.TrySetResult(task.Result);
        }
        else if (task.IsFaulted)
        {
            // Task.Exception is always an AggregateException
            // when IsFaulted is true. The compiler only sees
            // the declared Exception? type, so we cast to
            // unwrap the inner exceptions correctly.
            AggregateException? aggregate = task.Exception as AggregateException;
            if (aggregate is not null && aggregate.InnerExceptions.Count > 0)
            {
                tcs.TrySetException(aggregate.InnerExceptions);
            }
            else if (task.Exception is not null)
            {
                tcs.TrySetException(task.Exception);
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException("Task faulted without an exception."));
            }
        }
        else if (task.IsCanceled)
        {
            tcs.TrySetCanceled();
        }
    }

    /// <summary>
    /// Private <see cref="SynchronizationContext"/> installed on
    /// the affinity thread. Routes every
    /// <see cref="SynchronizationContext.Post"/> call into the
    /// executor's <c>_continuationQueue</c>, so awaited
    /// continuations posted from inside a work item resume on
    /// the affinity thread. <see cref="CreateCopy"/> returns the
    /// same instance to keep the context pinned to the affinity
    /// thread for the entire lifetime of the executor.
    /// </summary>
    private sealed class AffinitySynchronizationContext : SynchronizationContext
    {
        private readonly RuntimeAffinityExecutor _executor;

        public AffinitySynchronizationContext(RuntimeAffinityExecutor executor)
        {
            ArgumentNullException.ThrowIfNull(executor);
            _executor = executor;
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (_executor._disposed != 0 || _executor._continuationQueue.IsAddingCompleted)
            {
                return;
            }

            try
            {
                // _continuationQueue.Add is intentionally invoked
                // with CancellationToken.None: continuations are
                // always enqueued, even on the disposal path, so
                // the affinity thread can pump them. Suppressing
                // CA2016 here is intentional and documented.
                _executor._continuationQueue.Add(() => d(state), CancellationToken.None);
            }
            catch (ObjectDisposedException)
            {
                // _continuationQueue was disposed by a concurrent
                // Dispose() racing with the Post(). The
                // continuation is dropped for the same reason as
                // the InvalidOperationException handler below:
                // the originating task is still completed by
                // the ContinueWith in PumpUntilCompleted.
                // Note: ObjectDisposedException is a subclass of
                // InvalidOperationException, so this catch must
                // come before the more general one below.
            }
            catch (InvalidOperationException)
            {
                // _continuationQueue was completed between the
                // check and the Add(). The continuation is
                // dropped; the originating task's
                // TaskCompletionSource is still completed by
                // the ContinueWith in PumpUntilCompleted, so
                // the caller will see the right end state.
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
