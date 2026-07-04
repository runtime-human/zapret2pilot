using System;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Supervisor;

/// <summary>
/// Public contract for the <c>RuntimeSupervisor</c> — the single
/// owner of the runtime start / stop state machine. The supervisor
/// gates every start through <see cref="Guard.ICrashLoopGuard"/>,
/// records failures and successes against the guard, observes
/// <see cref="Health.IRuntimeHealthMonitor"/> transitions and
/// publishes the resulting state through
/// <see cref="StateChanged"/> so the UI can reflect it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread-safety.</b> All members are safe to call from any
/// thread. <see cref="StartAsync"/> and <see cref="StopAsync"/>
/// serialise internally on a private semaphore; concurrent calls
/// are rejected with a typed failure result rather than blocking.
/// </para>
/// <para>
/// <b>Health observability.</b> The supervisor subscribes to
/// <see cref="Health.IRuntimeHealthMonitor.SnapshotChanged"/> on
/// <c>IHostedService.StartAsync</c> and unsubscribes on
/// <c>IHostedService.StopAsync</c>. A transition into
/// <see cref="Health.RuntimeHealthState.Healthy"/> is the
/// load-bearing signal for a successful start; a transition into
/// <see cref="Health.RuntimeHealthState.Exited"/> is the
/// load-bearing signal for an unexpected runtime crash and
/// triggers an automatic stop.
/// </para>
/// <para>
/// <b>Crash-loop guard integration.</b> Every
/// <see cref="StartAsync"/> call is preceded by a
/// <see cref="Guard.ICrashLoopGuard.Check"/>. A failed start calls
/// <see cref="Guard.ICrashLoopGuard.RecordFailure"/>. A successful
/// <see cref="Health.RuntimeHealthState.Healthy"/> transition calls
/// <see cref="Guard.ICrashLoopGuard.RecordSuccess"/>. The supervisor
/// never reaches into the guard's private state; it only consumes
/// the public surface of <see cref="Guard.ICrashLoopGuard"/>.
/// </para>
/// </remarks>
public interface IRuntimeSupervisor
{
    /// <summary>
    /// Most recent state published by the supervisor. Returns the
    /// <see cref="RuntimeSupervisorStatus.Stopped"/> state with the
    /// current <see cref="TimeProvider"/> timestamp before
    /// <c>IHostedService.StartAsync</c> has been called.
    /// </summary>
    RuntimeSupervisorState CurrentState { get; }

    /// <summary>
    /// Hot observable that emits the current
    /// <see cref="RuntimeSupervisorState"/> on subscription and a
    /// new snapshot on every transition. Backed by a
    /// <c>BehaviorSubject&lt;RuntimeSupervisorState&gt;</c>, so
    /// subscribers always see the most recent value plus every
    /// subsequent transition.
    /// </summary>
    IObservable<RuntimeSupervisorState> StateChanged { get; }

    /// <summary>
    /// Starts the runtime. The supervisor consults
    /// <see cref="Guard.ICrashLoopGuard"/> first: a guard block
    /// surfaces as a <see cref="RuntimeSupervisorStatus.StartBlocked"/>
    /// snapshot and a typed failure result without touching
    /// <see cref="IRuntimeProcessHost"/>. A successful guard check
    /// delegates to <see cref="IRuntimeProcessHost.StartAsync"/> and
    /// publishes <see cref="RuntimeSupervisorStatus.Running"/> on
    /// success or <see cref="RuntimeSupervisorStatus.Stopped"/> on
    /// failure (the failure also calls
    /// <see cref="Guard.ICrashLoopGuard.RecordFailure"/>).
    /// </summary>
    /// <param name="context">
    /// Start context forwarded to
    /// <see cref="IRuntimeProcessHost.StartAsync"/>. Must not be
    /// <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed while the supervisor is
    /// waiting for <see cref="IRuntimeProcessHost.StartAsync"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with a
    /// <see cref="RuntimeProcessHostResult"/> on success, or an
    /// <see cref="ErrorInfo"/> on failure. The
    /// <c>RuntimeSupervisorAlreadyRunning</c> failure is the
    /// typed outcome of a re-entrant start.
    /// </returns>
    Task<Result<RuntimeProcessHostResult>> StartAsync(
        RuntimeProcessStartContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the runtime. Idempotent: <see cref="RuntimeSupervisorStatus.Stopped"/>
    /// and <see cref="RuntimeSupervisorStatus.StartBlocked"/> are
    /// treated as a successful no-op so a defensive
    /// <c>StopAsync</c> on an idle supervisor is safe.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token observed while the supervisor is
    /// waiting for <see cref="IRuntimeProcessHost.StopAsync"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with <see cref="Unit.Instance"/> on
    /// success, or an <see cref="ErrorInfo"/> on failure.
    /// </returns>
    Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default);
}
