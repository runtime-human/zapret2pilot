using System;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;

namespace Zapret2Pilot.Runtime.Supervisor;

/// <summary>
/// Public contract for the <c>RuntimeSupervisor</c>. The
/// supervisor is a façade over
/// <see cref="Zapret2Pilot.Runtime.Kernel.RuntimeKernelLoop"/>: it
/// translates the public start / stop calls into kernel commands,
/// awaits the loop's projected state, and bridges
/// <see cref="Health.IRuntimeHealthMonitor"/> snapshots into
/// <see cref="RuntimeKernelCommand.Observation"/>s. All lifecycle
/// state and guard bookkeeping lives in the loop; the supervisor
/// itself owns no state machine.
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
/// <c>IHostedService.StopAsync</c>. Each snapshot is forwarded
/// to the loop as an <see cref="RuntimeKernelCommand.Observation"/>;
/// the loop decides how to react (record a guard success /
/// failure and / or drive an automatic stop).
/// </para>
/// <para>
/// <b>Crash-loop guard integration.</b> The guard is consulted
/// inside the loop, not in the supervisor. A guard block surfaces
/// as a <see cref="RuntimeSupervisorStatus.StartBlocked"/> snapshot
/// and a typed failure result without any process being launched.
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
    /// new snapshot on every transition. Sourced from
    /// <see cref="RuntimeKernelLoop.StateChanged"/>; subscribers
    /// always see the most recent value plus every subsequent
    /// transition.
    /// </summary>
    IObservable<RuntimeSupervisorState> StateChanged { get; }

    /// <summary>
    /// Starts the runtime. The supervisor posts a
    /// <see cref="RuntimeKernelCommand.Start"/> to the loop and
    /// awaits the projected state until the kernel reaches one of
    /// the terminal statuses (<see cref="RuntimeSupervisorStatus.Running"/>,
    /// <see cref="RuntimeSupervisorStatus.Stopped"/> or
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/>). A
    /// guard block surfaces as a
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/> snapshot
    /// and a typed failure result without launching the runtime.
    /// </summary>
    /// <param name="context">
    /// Start context forwarded to the kernel loop. Must not be
    /// <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed while the supervisor is
    /// waiting for the kernel to publish the terminal snapshot.
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
    /// waiting for the kernel to publish the terminal snapshot.
    /// </param>
    /// <returns>
    /// A <see cref="Result{T}"/> with <see cref="Unit.Instance"/> on
    /// success, or an <see cref="ErrorInfo"/> on failure.
    /// </returns>
    Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default);
}
