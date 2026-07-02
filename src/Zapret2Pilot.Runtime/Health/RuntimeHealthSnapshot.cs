using System;

namespace Zapret2Pilot.Runtime.Health;

/// <summary>
/// Immutable snapshot of the runtime process health, produced by
/// <see cref="RuntimeHealthMonitor"/> on every probe. Snapshots are
/// pushed through a <c>BehaviorSubject&lt;RuntimeHealthSnapshot&gt;</c>
/// so subscribers always see the most recent value plus every
/// transition.
/// </summary>
public sealed record class RuntimeHealthSnapshot
{
    public RuntimeHealthSnapshot(
        RuntimeHealthState state,
        int? processId,
        DateTimeOffset observedAtUtc)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentException(
                $"Unknown {nameof(RuntimeHealthState)} value: {state}.",
                nameof(state));
        }

        if (processId is not null && processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "Process id must be a positive integer when supplied.");
        }

        State = state;
        ProcessId = processId;
        ObservedAtUtc = observedAtUtc;
    }

    /// <summary>
    /// Current health state of the runtime process.
    /// </summary>
    public RuntimeHealthState State { get; }

    /// <summary>
    /// Process id of the runtime process, or <c>null</c> when no
    /// process is currently owned by the host
    /// (<see cref="RuntimeHealthState.Unknown"/>).
    /// </summary>
    public int? ProcessId { get; }

    /// <summary>
    /// Wall-clock UTC timestamp recorded when the probe built the
    /// snapshot.
    /// </summary>
    public DateTimeOffset ObservedAtUtc { get; }
}
