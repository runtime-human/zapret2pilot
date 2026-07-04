using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Runtime.Hosting;

/// <summary>
/// Public contract that exposes the Runtime Kernel's process host
/// surface to the rest of the system. The supervisor
/// (<see cref="Zapret2Pilot.Runtime.Supervisor.IRuntimeSupervisor"/>)
/// depends only on this abstraction so the host can be replaced by
/// a fake in tests without spinning up the dedicated kernel
/// worker, the ownership mutex, the lock file store or the Windows
/// Job Object.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be thread-safe: <see cref="StartAsync"/> and
/// <see cref="StopAsync"/> may be called from any thread. The
/// production <see cref="RuntimeProcessHost"/> enqueues its work
/// onto the dedicated <c>RuntimeKernelWorker</c> thread, so the
/// ownership-mutex and the Job Object lifetime are preserved
/// regardless of the caller's thread.
/// </para>
/// <para>
/// Both methods return a <see cref="Result{T}"/> carrying either a
/// success payload or an <see cref="ErrorInfo"/>. The supervisor
/// inspects the result to drive its state machine and to feed
/// <see cref="Zapret2Pilot.Runtime.Guard.ICrashLoopGuard"/> with
/// <c>RecordFailure</c> / <c>RecordSuccess</c>.
/// </para>
/// </remarks>
public interface IRuntimeProcessHost
{
    /// <summary>
    /// Starts the runtime process. The actual launch is marshalled
    /// onto the Runtime Kernel's dedicated worker thread inside the
    /// production implementation; the call returns a
    /// <see cref="Task{TResult}"/> the caller can await.
    /// </summary>
    /// <param name="context">
    /// Start context carrying the compiled plan, asset manifest,
    /// workspace directory and verified runtime executable path.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed at well-defined points in the
    /// pipeline. The token is not honoured while the ownership
    /// mutex is held or while the operating system is performing
    /// non-cancellable work.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with a
    /// <see cref="RuntimeProcessHostResult"/> describing the launched
    /// process on success, or a <see cref="ErrorInfo"/> on failure.
    /// </returns>
    Task<Result<RuntimeProcessHostResult>> StartAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the runtime process. The actual stop pipeline is
    /// marshalled onto the dedicated kernel worker thread inside the
    /// production implementation; the call returns a
    /// <see cref="Task{TResult}"/> the caller can await.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token observed before the stop pipeline runs.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with <see cref="Unit.Instance"/> on
    /// success, or a <see cref="ErrorInfo"/> on failure.
    /// </returns>
    Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default);
}
