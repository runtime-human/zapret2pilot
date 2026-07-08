using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zapret2Pilot.Runtime.Threading;

/// <summary>
/// Owns a dedicated background thread named <c>"Z2P-RuntimeAffinity"</c>
/// and a bounded command queue. All work submitted via
/// <see cref="ExecuteAsync{T}(Func{CancellationToken, T}, CancellationToken)"/>
/// or <see cref="ExecuteAsync(Action{CancellationToken}, CancellationToken)"/>
/// runs synchronously on the owner thread, serialized. The caller's
/// thread is never blocked: the returned <see cref="Task{TResult}"/>
/// completes when the owner thread finishes the work.
/// </summary>
/// <remarks>
/// <para>
/// The owner thread is the single thread-affinity anchor for the
/// runtime pipeline. The ownership mutex lease
/// (<see cref="Ownership.RuntimeOwnershipLease"/>) is acquired
/// and disposed on this thread, satisfying the lease's
/// <c>ownerManagedThreadId</c> invariant without a
/// <see cref="SynchronizationContext"/>.
/// </para>
/// <para>
/// Work delegates MUST be synchronous. If a delegate needs to
/// perform asynchronous I/O, it should block on the result
/// (e.g., <c>.GetAwaiter().GetResult()</c>). The owner thread
/// is dedicated and will not service other work while blocked.
/// </para>
/// </remarks>
public interface IRuntimeAffinityOwner : IDisposable
{
    /// <summary>
    /// The managed thread ID of the owner thread. Stable for the
    /// lifetime of the owner. Used for diagnostics and
    /// thread-affinity assertions.
    /// </summary>
    int OwnerThreadId { get; }

    /// <summary>
    /// Schedules <paramref name="work"/> to run synchronously on
    /// the owner thread. Returns a <see cref="Task{TResult}"/> that
    /// completes when the delegate finishes.
    /// </summary>
    /// <typeparam name="T">Result type of the synchronous delegate.</typeparam>
    /// <param name="work">
    /// The synchronous delegate to execute. MUST NOT be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed by the work and combined with
    /// the owner's internal disposal token. When the token is
    /// cancelled, the work is signalled to stop; the
    /// <see cref="Task{TResult}"/> transitions to
    /// <see cref="TaskStatus.Canceled"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that completes when the owner
    /// thread finishes the work.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="work"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The owner has been disposed.
    /// </exception>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, T> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules <paramref name="work"/> to run synchronously on
    /// the owner thread. Returns a <see cref="Task"/> that
    /// completes when the delegate finishes.
    /// </summary>
    /// <param name="work">
    /// The synchronous delegate to execute. MUST NOT be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed by the work and combined with
    /// the owner's internal disposal token.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the owner thread
    /// finishes the work.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="work"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The owner has been disposed.
    /// </exception>
    Task ExecuteAsync(
        Action<CancellationToken> work,
        CancellationToken cancellationToken = default);
}
