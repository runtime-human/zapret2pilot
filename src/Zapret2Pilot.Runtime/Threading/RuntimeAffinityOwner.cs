using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Zapret2Pilot.Runtime.Threading;

/// <summary>
/// Default <see cref="IRuntimeAffinityOwner"/> implementation. Owns
/// a single dedicated background thread named
/// <c>"Z2P-RuntimeAffinity"</c> and a bounded command queue. The
/// thread pumps one <see cref="Action"/> at a time from the queue,
/// serialising every submitted work item on a single, predictable
/// thread. See <see cref="IRuntimeAffinityOwner"/> for the full
/// contract.
/// </summary>
/// <remarks>
/// <para>
/// Unlike its predecessor (<c>RuntimeAffinityExecutor</c>) the
/// owner does NOT install a <see cref="SynchronizationContext"/> on
/// the owner thread and does NOT accept async delegates. Work
/// delegates are <see cref="Action{CancellationToken}"/> /
/// <see cref="Func{CancellationToken, TResult}"/>; if a delegate
/// needs asynchronous I/O it must block on the result via
/// <c>.GetAwaiter().GetResult()</c>. The owner thread is dedicated
/// and will not service other work while blocked.
/// </para>
/// <para>
/// The internal command queue is a <see cref="BlockingCollection{T}"/>
/// with the documented capacity of 256 items. <see cref="Dispose"/>
/// is idempotent: it cancels the disposal CTS, calls
/// <c>CompleteAdding</c> on the queue, joins the owner thread with
/// a five-second timeout and disposes the queue.
/// </para>
/// </remarks>
public sealed class RuntimeAffinityOwner : IRuntimeAffinityOwner
{
    /// <summary>
    /// Capacity of the bounded command queue. Matches the
    /// documented value for diagnostics continuity with the
    /// predecessor <c>RuntimeAffinityExecutor</c>.
    /// </summary>
    public const int QueueCapacity = 256;

    /// <summary>
    /// Maximum time the <see cref="Dispose"/> method waits for the
    /// owner thread to exit. Five seconds mirrors the predecessor
    /// implementation.
    /// </summary>
    private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly BlockingCollection<Action> _commandQueue = new(new ConcurrentQueue<Action>(), QueueCapacity);
    private readonly Thread _thread;
    private int _disposed;

    /// <summary>
    /// Captured managed thread ID of the owner thread. Set by the
    /// owner thread itself, in the thread's startup, before the
    /// pump loop starts consuming. Stable for the lifetime of the
    /// owner. Exposed as <see cref="OwnerThreadId"/> for assertions
    /// and diagnostics.
    /// </summary>
    private int _ownerThreadId;

    /// <summary>
    /// Creates a new <see cref="RuntimeAffinityOwner"/> and starts
    /// the dedicated background thread. The thread is named
    /// <c>"Z2P-RuntimeAffinity"</c> and runs as a background thread
    /// (so it does not keep the process alive on its own). No
    /// <see cref="SynchronizationContext"/> is installed on the
    /// thread; the affinity invariant is expressed by the
    /// <see cref="OwnerThreadId"/> property and the
    /// <c>ownerManagedThreadId</c> check inside
    /// <see cref="Ownership.RuntimeOwnershipLease"/>.
    /// </summary>
    public RuntimeAffinityOwner()
    {
        _thread = new Thread(RunLoop)
        {
            Name = "Z2P-RuntimeAffinity",
            IsBackground = true,
        };
        _thread.Start();
    }

    /// <inheritdoc />
    public int OwnerThreadId => Volatile.Read(ref _ownerThreadId);

    /// <inheritdoc />
    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, T> work,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(work);

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);

        bool enqueued = false;
        try
        {
            // _commandQueue.Add is intentionally invoked with
            // CancellationToken.None: the work item is enqueued
            // even when the caller's token is already cancelled, so
            // the owner thread can pick it up, observe the
            // cancellation through linkedCts and complete the TCS
            // with TaskStatus.Canceled. Suppressing CA2016 here is
            // intentional and documented.
            _commandQueue.Add(() =>
            {
                // Fast-path: the disposal CTS fired before the
                // command was picked up. Complete the TCS as
                // cancelled without invoking the work.
                if (_disposed != 0 || linkedCts.IsCancellationRequested)
                {
                    tcs.TrySetCanceled();
                    linkedCts.Dispose();
                    return;
                }

                try
                {
                    T result = work(linkedCts.Token);
                    // Re-check the disposal flag before completing
                    // the TCS. The work may have observed its
                    // cancellation through linkedCts, but it may
                    // also have produced a result before that; the
                    // disposal contract is that the caller sees a
                    // terminal state, so we always prefer the
                    // cancelled outcome over a successful result
                    // once Dispose has run.
                    if (_disposed != 0)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    linkedCts.Dispose();
                }
            }, CancellationToken.None);
            enqueued = true;
        }
        catch (ObjectDisposedException)
        {
            // _commandQueue was disposed by a concurrent
            // Dispose(). Release the linked CTS and surface the
            // canonical ObjectDisposedException.
            linkedCts.Dispose();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            throw;
        }
        catch (InvalidOperationException)
        {
            // _commandQueue had CompleteAdding called between the
            // _disposed check and the Add(). The owner is
            // effectively disposed for the caller's purposes.
            linkedCts.Dispose();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            throw;
        }
        finally
        {
            // Defensive: if the enqueue failed AND we did not
            // re-throw through the catches above (e.g. an
            // unexpected exception type), make sure the linked CTS
            // is released and the TCS is settled. The
            // CancellationToken.None is intentional: this is a
            // best-effort defensive completion, not a propagation
            // of the caller's token; CA2016 is suppressed by
            // passing None explicitly.
            if (!enqueued && !tcs.Task.IsCompleted)
            {
                linkedCts.Dispose();
                tcs.TrySetCanceled(CancellationToken.None);
            }
        }

        return tcs.Task;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(
        Action<CancellationToken> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return ExecuteAsync<bool?>(ct =>
        {
            work(ct);
            return null;
        }, cancellationToken);
    }

    /// <summary>
    /// Stops the owner. Idempotent. Signals in-flight work to
    /// cancel via <c>_disposeCts</c>, stops accepting new work by
    /// completing <c>_commandQueue</c>, joins the owner thread with
    /// a five-second timeout, and releases the underlying
    /// resources. Subsequent calls to
    /// <see cref="ExecuteAsync{T}(Func{CancellationToken, T}, CancellationToken)"/>
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
        //    consumed by the owner thread, including the
        //    CleanupPipeline posted by
        //    RuntimeProcessHost.Dispose; cancelling the enumerator
        //    here would drop those entries and hang the caller.
        _commandQueue.CompleteAdding();

        // 3. Wait for the owner thread to drain the queue and
        //    exit. The thread loops on GetConsumingEnumerable()
        //    and exits as soon as both CompleteAdding and an
        //    empty queue are observed.
        bool threadJoined = true;
        if (_thread.IsAlive)
        {
            threadJoined = _thread.Join(ThreadJoinTimeout);
        }

        if (threadJoined)
        {
            // 4. The owner thread has exited, so no one is reading
            //    from the command queue. It is safe to dispose it.
            _disposeCts.Dispose();
            _commandQueue.Dispose();
        }
        else
        {
            // The owner thread did not exit within the join
            // timeout. It may still be reading from the queue, so
            // completing or disposing it would race with the live
            // thread. Leave the resources alive for the process
            // lifetime. Setting _disposed=1 (above) is sufficient
            // to make ExecuteAsync throw ObjectDisposedException,
            // which is the correct public contract after Dispose().
        }
    }

    /// <summary>
    /// The owner thread's main loop. Captures
    /// <see cref="Environment.CurrentManagedThreadId"/> in
    /// <see cref="_ownerThreadId"/> before entering the pump so
    /// the value is observable from any thread as soon as
    /// <see cref="OwnerThreadId"/> is read. Consumes work items
    /// from <c>_commandQueue</c> one at a time and exits cleanly
    /// when the queue is completed AND drained. The enumeration
    /// is intentionally NOT cancelled by <c>_disposeCts</c>:
    /// <see cref="Dispose"/> calls <c>CompleteAdding</c> on the
    /// queue, the loop drains every work item that was enqueued
    /// before disposal (including the <c>CleanupPipeline</c>
    /// posted by <see cref="Hosting.RuntimeProcessHost.Dispose"/>),
    /// and the loop exits naturally when the queue is empty.
    /// Cancelling the enumeration would silently drop the work
    /// items that were enqueued before <c>CompleteAdding</c>,
    /// hanging any caller waiting for the resulting
    /// <see cref="Task{TResult}"/>. Each work item is responsible
    /// for completing its own <see cref="TaskCompletionSource{TResult}"/>;
    /// an exception inside the work delegate is caught and routed
    /// onto the TCS so a buggy work item cannot poison the owner.
    /// </summary>
    private void RunLoop()
    {
        // Capture the owner thread ID BEFORE entering the pump so
        // that the very first ExecuteAsync callers can resolve it
        // deterministically even when the work is enqueued faster
        // than the pump spins up.
        Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);

        // No SynchronizationContext is installed on purpose. The
        // affinity invariant is enforced by the
        // RuntimeOwnershipLease.ownerManagedThreadId check against
        // OwnerThreadId, not by posting continuations to the
        // owner thread. Delegates that await must block on the
        // result (e.g. .GetAwaiter().GetResult()).
        try
        {
            foreach (Action command in _commandQueue.GetConsumingEnumerable())
            {
                try
                {
                    command();
                }
                catch
                {
                    // Work items own their own state. Anything
                    // that escapes their try/catch is a contract
                    // violation; we drop it on the floor and move
                    // on, otherwise a single buggy command would
                    // poison the entire owner.
                }
            }
        }
        finally
        {
            // No SynchronizationContext to restore (we never
            // installed one). The finally block is here for
            // symmetry with the predecessor implementation and to
            // make the thread's "no ambient context" invariant
            // explicit to future maintainers.
        }
    }
}
