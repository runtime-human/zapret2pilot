namespace Zapret2Pilot.Runtime.Supervisor;

/// <summary>
/// Lifecycle states of the <c>RuntimeSupervisor</c>. The supervisor
/// is a façade; <see cref="Kernel.RuntimeKernelLoop"/> is the single
/// owner of the runtime start / stop state machine. These states are
/// the public projection of the kernel's lifecycle state machine for
/// UI, logging and tests.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Stopped"/> — the supervisor is idle. The
///         runtime is not running and no transition is in progress.
///         This is the initial state and the state entered after
///         <see cref="Stopping"/> completes successfully.</item>
///   <item><see cref="Starting"/> — a start request is in progress.
///         The <see cref="Guard.ICrashLoopGuard"/> has already
///         accepted the restart; the supervisor is waiting for
///         <see cref="Hosting.IRuntimeProcessHost.StartAsync"/> to
///         return.</item>
///   <item><see cref="Running"/> — the runtime is up. The supervisor
///         reached this state either directly from
///         <see cref="Starting"/> or because a health snapshot
///         transitioned into <see cref="Health.RuntimeHealthState.Healthy"/>
///         and the supervisor recorded the success with the
///         guard.</item>
///   <item><see cref="Stopping"/> — a stop request is in progress
///         (either user-initiated or triggered by an
///         <see cref="Health.RuntimeHealthState.Exited"/> health
///         snapshot). The supervisor is waiting for
///         <see cref="Hosting.IRuntimeProcessHost.StopAsync"/> to
///         return.</item>
///   <item><see cref="StartBlocked"/> — the
///         <see cref="Guard.ICrashLoopGuard"/> refused the restart.
///         The supervisor carries the <see cref="Guard.CrashLoopGuardResult"/>
///         in <see cref="RuntimeSupervisorState.GuardResult"/> so the
///         UI can surface a "too many crashes, retry in N seconds" or
///         "permanent lockout, restart the app" message.</item>
/// </list>
/// </remarks>
public enum RuntimeSupervisorStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    StartBlocked,
}
