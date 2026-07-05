using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// Internal contract used by <see cref="RuntimeKernelLoop"/> to
/// execute the asynchronous <see cref="RuntimeEffectIntent"/>s
/// emitted by the reducer. The runner observes the
/// intent's <see cref="RuntimeEffectIntent.Deadline"/>, drives
/// the host call on a thread-pool thread, and returns a
/// <see cref="RuntimeKernelCommand.EffectCompleted"/> that the
/// loop can post back into the command channel regardless of
/// whether the host succeeded, failed, was cancelled or crossed
/// an irreversible boundary.
/// </summary>
/// <remarks>
/// <para>
/// The interface is intentionally <c>internal</c> — it is a
/// plumbing seam between the kernel and the host, not a public
/// runtime API. The runner is responsible for translating
/// <see cref="RuntimeCancellationReason"/>s and the
/// irreversible-boundary rule (a successful start, or the moment
/// the stop pipeline begins, can no longer return a plain
/// <c>Cancelled</c> outcome; the runner must surface
/// <c>RollbackRequired</c> / <c>RecoveryRequired</c> instead).
/// </para>
/// <para>
/// Implementations MUST be safe to call from any thread: the
/// loop dispatches the work via <see cref="Task.Run"/> so the
/// dedicated kernel thread is never blocked. The returned
/// <see cref="Task{TResult}"/> must always complete — never
/// fault — and surface every failure inside the
/// <see cref="RuntimeKernelCommand.EffectCompleted.Result"/>.
/// The loop itself swallows unexpected exceptions raised by the
/// runner, but the contract is "log and convert", not "let it
/// escape".
/// </para>
/// </remarks>
internal interface IRuntimeEffectRunner
{
    /// <summary>
    /// Runs the supplied <see cref="RuntimeEffectIntent"/> to
    /// completion and returns a
    /// <see cref="RuntimeKernelCommand.EffectCompleted"/> that
    /// carries the matching
    /// <see cref="RuntimeEffectIntent.OperationId"/> /
    /// <see cref="RuntimeEffectIntent.Generation"/> identity.
    /// </summary>
    /// <param name="intent">
    /// Effect intent emitted by the reducer. The runner only
    /// supports <see cref="RuntimeEffectKind.StartProcess"/> and
    /// <see cref="RuntimeEffectKind.StopProcess"/>; any other
    /// kind MUST be answered with a failed completion carrying a
    /// typed <c>UnsupportedEffectKind</c> error.
    /// </param>
    /// <param name="timeProvider">
    /// Monotonic and wall-clock time source used to enforce
    /// <see cref="RuntimeEffectIntent.Deadline"/>. The runner
    /// relies on <see cref="TimeProvider.GetTimestamp"/> and
    /// <see cref="TimeProvider.GetElapsedTime"/> for deadline
    /// detection; <see cref="TimeProvider.GetUtcNow"/> is used
    /// only for the wall-clock comparison when
    /// <see cref="RuntimeEffectIntent.Deadline"/> is already in
    /// the past at intent dispatch time.
    /// </param>
    /// <param name="cancellationToken">
    /// Outer cancellation token (the kernel worker CTS). When
    /// the token fires before the irreversible boundary has
    /// been crossed the runner returns
    /// <see cref="RuntimeCancellationReason.HostShutdown"/>;
    /// when the token fires after the boundary has been crossed
    /// the runner returns
    /// <see cref="RuntimeCancellationReason.HostShutdown"/>
    /// together with a <c>RollbackRequired</c> /
    /// <c>RecoveryRequired</c> failure.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that always completes with
    /// a <see cref="RuntimeKernelCommand.EffectCompleted"/>;
    /// the result captures success, failure, the
    /// <see cref="RuntimeCancellationReason"/> (when the run
    /// was cancelled) and the
    /// <see cref="RuntimeKernelCommand.EffectCompleted.CrossedIrreversibleBoundary"/>
    /// flag (when the run crossed the boundary before
    /// completing).
    /// </returns>
    Task<RuntimeKernelCommand.EffectCompleted> RunAsync(
        RuntimeEffectIntent intent,
        TimeProvider timeProvider,
        CancellationToken cancellationToken);
}
