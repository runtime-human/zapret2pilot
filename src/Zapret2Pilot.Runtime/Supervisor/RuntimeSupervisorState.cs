using System;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Hosting;

namespace Zapret2Pilot.Runtime.Supervisor;

/// <summary>
/// Immutable snapshot of the <c>RuntimeSupervisor</c>'s public
/// state. The supervisor publishes a new instance on every
/// transition through its
/// <see cref="IRuntimeSupervisor.StateChanged"/> observable.
/// </summary>
/// <remarks>
/// <para>
/// Every transition carries the wall-clock <see cref="Timestamp"/>
/// recorded by the supervisor's <see cref="TimeProvider"/>. Tests
/// inject a deterministic <see cref="TimeProvider"/> so the
/// timestamp is stable and the
/// <see cref="StateChanged"/> observable becomes trivially
/// assertable.
/// </para>
/// <para>
/// The optional <see cref="LastStartResult"/>,
/// <see cref="GuardResult"/> and <see cref="LastError"/> fields let
/// the UI surface enough context to explain the transition without
/// having to query the underlying components. They are <c>null</c>
/// when the supervisor has no recent value to report.
/// </para>
/// </remarks>
public sealed record RuntimeSupervisorState
{
    /// <summary>
    /// Creates a new <see cref="RuntimeSupervisorState"/> snapshot.
    /// </summary>
    /// <param name="status">
    /// Current <see cref="RuntimeSupervisorStatus"/>. Must be a
    /// defined enum value.
    /// </param>
    /// <param name="lastStartResult">
    /// Most recent success payload returned by
    /// <see cref="IRuntimeProcessHost.StartAsync"/>, or <c>null</c>
    /// when the supervisor has not yet started a runtime (or when
    /// the last start failed).
    /// </param>
    /// <param name="guardResult">
    /// Most recent <see cref="CrashLoopGuardResult"/>. Carried in
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/> snapshots
    /// so the UI knows whether the block is a temporary backoff or
    /// a permanent lockout. <c>null</c> when the guard is in its
    /// initial state.
    /// </param>
    /// <param name="lastError">
    /// Most recent <see cref="ErrorInfo"/> surfaced by the
    /// supervisor (a failed start, a failed stop, or a guard block).
    /// <c>null</c> when the supervisor has no error to report.
    /// </param>
    /// <param name="timestamp">
    /// Wall-clock timestamp recorded by the supervisor's
    /// <see cref="TimeProvider"/>. Required.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="status"/> is not a defined
    /// <see cref="RuntimeSupervisorStatus"/> value.
    /// </exception>
    public RuntimeSupervisorState(
        RuntimeSupervisorStatus status,
        RuntimeProcessHostResult? lastStartResult,
        CrashLoopGuardResult? guardResult,
        ErrorInfo? lastError,
        DateTimeOffset timestamp)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException(
                $"Unknown {nameof(RuntimeSupervisorStatus)} value: {status}.",
                nameof(status));
        }

        Status = status;
        LastStartResult = lastStartResult;
        GuardResult = guardResult;
        LastError = lastError;
        Timestamp = timestamp;
    }

    /// <summary>
    /// Current <see cref="RuntimeSupervisorStatus"/>.
    /// </summary>
    public RuntimeSupervisorStatus Status { get; init; }

    /// <summary>
    /// Most recent success payload returned by
    /// <see cref="IRuntimeProcessHost.StartAsync"/>, or <c>null</c>
    /// when the supervisor has not yet started a runtime (or when
    /// the last start failed).
    /// </summary>
    public RuntimeProcessHostResult? LastStartResult { get; init; }

    /// <summary>
    /// Most recent <see cref="CrashLoopGuardResult"/>. Carried in
    /// <see cref="RuntimeSupervisorStatus.StartBlocked"/> snapshots
    /// so the UI knows whether the block is a temporary backoff or
    /// a permanent lockout. <c>null</c> when the guard is in its
    /// initial state.
    /// </summary>
    public CrashLoopGuardResult? GuardResult { get; init; }

    /// <summary>
    /// Most recent <see cref="ErrorInfo"/> surfaced by the
    /// supervisor (a failed start, a failed stop, or a guard block).
    /// <c>null</c> when the supervisor has no error to report.
    /// </summary>
    public ErrorInfo? LastError { get; init; }

    /// <summary>
    /// Wall-clock timestamp recorded by the supervisor's
    /// <see cref="TimeProvider"/> when the state was published.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}
