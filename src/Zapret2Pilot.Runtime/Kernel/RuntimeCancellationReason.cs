namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// Typed reason for cancelling a long-running Runtime Kernel
/// operation. The catalogue mirrors the roadmap §7.4
/// cancellation contract and is used by
/// <see cref="RuntimeEffectIntent"/>,
/// <see cref="RuntimeKernelState"/>,
/// <see cref="RuntimeKernelCommand.Stop"/>,
/// <see cref="RuntimeKernelCommand.EffectCompleted"/> and
/// <see cref="RuntimeProcessEffectRunner"/> to record how an
/// in-flight start / stop pipeline was interrupted.
/// </summary>
/// <remarks>
/// <para>
/// Cancellation reasons are recorded even when the operation
/// completed successfully, because they are part of the
/// durability contract of the operation: the supervisor and
/// the diagnostics surface need to know whether the operation
/// ran to completion, was cancelled by the user, timed out, was
/// superseded by a newer generation, was driven by host
/// shutdown, or was aborted by the safety guard.
/// </para>
/// <list type="bullet">
///   <item><see cref="UserRequested"/> — the user (or another
///         explicit caller) requested cancellation. Most common
///         path: the user pressed Stop.</item>
///   <item><see cref="HostShutdown"/> — the application host is
///         tearing the kernel down. All in-flight effects must
///         drain or be abandoned before disposal completes.</item>
///   <item><see cref="Timeout"/> — the operation's
///         <see cref="RuntimeEffectIntent.Deadline"/> elapsed
///         before the effect produced a completion.</item>
///   <item><see cref="Superseded"/> — a newer operation in the
///         same generation class displaced this one before it
///         could complete.</item>
///   <item><see cref="SafetyAbort"/> — a safety guard
///         (e.g. the crash-loop detector or the runtime
///         supervisor) aborted the operation outside of an
///         explicit user request.</item>
/// </list>
/// </remarks>
public enum RuntimeCancellationReason
{
    UserRequested,
    HostShutdown,
    Timeout,
    Superseded,
    SafetyAbort,
}
