using System;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Kernel;

/// <summary>
/// Immutable snapshot of the runtime kernel's state machine.
/// The reducer produces a fresh
/// <see cref="RuntimeKernelState"/> on every transition; the
/// loop publishes it through
/// <see cref="RuntimeStatePublisher"/>. Tests, the supervisor
/// façade, and the diagnostics surface consume the snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Deadline"/> and
/// <see cref="CancellationReason"/> fields capture the latest
/// deadline / cancellation reason the kernel was operating
/// under. They are populated when a transition into
/// <see cref="RuntimeKernelStatus.Starting"/> or
/// <see cref="RuntimeKernelStatus.Stopping"/> is committed and
/// are cleared on transition to
/// <see cref="RuntimeKernelStatus.Running"/> /
/// <see cref="RuntimeKernelStatus.Stopped"/> /
/// <see cref="RuntimeKernelStatus.StartBlocked"/>.
/// </para>
/// </remarks>
public sealed record RuntimeKernelState
{
    public RuntimeKernelState(
        RuntimeKernelStatus status,
        AutomationOwner owner,
        RuntimeGeneration generation,
        RuntimeOperationId? pendingOperationId,
        RuntimeProcessHostResult? lastStartResult,
        CrashLoopGuardResult? guardResult,
        ErrorInfo? lastError,
        DateTimeOffset timestamp,
        DateTimeOffset? deadline = null,
        RuntimeCancellationReason? cancellationReason = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException(
                $"Unknown {nameof(RuntimeKernelStatus)} value: {status}.",
                nameof(status));
        }

        Status = status;
        Owner = owner;
        Generation = generation;
        PendingOperationId = pendingOperationId;
        LastStartResult = lastStartResult;
        GuardResult = guardResult;
        LastError = lastError;
        Timestamp = timestamp;
        Deadline = deadline;
        CancellationReason = cancellationReason;
    }

    public RuntimeKernelStatus Status { get; init; }
    public AutomationOwner Owner { get; init; }
    public RuntimeGeneration Generation { get; init; }
    public RuntimeOperationId? PendingOperationId { get; init; }
    public RuntimeProcessHostResult? LastStartResult { get; init; }
    public CrashLoopGuardResult? GuardResult { get; init; }
    public ErrorInfo? LastError { get; init; }
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Wall-clock deadline by which the in-flight start or stop
    /// effect is expected to complete. <c>null</c> when no
    /// deadline is in effect (i.e. for the
    /// <see cref="RuntimeKernelStatus.Stopped"/>,
    /// <see cref="RuntimeKernelStatus.Running"/>, and
    /// <see cref="RuntimeKernelStatus.StartBlocked"/> states).
    /// </summary>
    public DateTimeOffset? Deadline { get; init; }

    /// <summary>
    /// Cancellation reason recorded for the in-flight
    /// operation. <c>null</c> when no cancellation is in
    /// effect.
    /// </summary>
    public RuntimeCancellationReason? CancellationReason { get; init; }
}
