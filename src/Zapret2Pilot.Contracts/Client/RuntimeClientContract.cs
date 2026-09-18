using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.Contracts.Client;

/// <summary>
/// Read-only App projection of broker-owned runtime state.
/// This type carries broker-observed state only; it is not a lifecycle state machine.
/// </summary>
public sealed record RuntimeClientSnapshot(
    RuntimeGeneration Generation,
    BrokerRuntimeState State,
    PreparedPlanId? ActivePlanId,
    BrokerOperationId? ActiveOperationId)
{
    public static RuntimeClientSnapshot FromBroker(BrokerRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new RuntimeClientSnapshot(
            snapshot.Generation,
            snapshot.State,
            snapshot.ActivePlanId,
            snapshot.ActiveOperationId);
    }
}

/// <summary>
/// Bounded App-side lifecycle client for the session-scoped Runtime Broker.
/// The surface intentionally accepts broker identities only and never executable
/// paths, argument vectors, process start information, or arbitrary commands.
/// </summary>
public interface IRuntimeClient
{
    RuntimeClientSnapshot CurrentSnapshot { get; }

    IObservable<RuntimeClientSnapshot> SnapshotChanged { get; }

    Task<RuntimeClientSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken);

    Task<RuntimeClientSnapshot> StartPreparedPlanAsync(
        PreparedPlanId preparedPlanId,
        RuntimeGeneration expectedGeneration,
        CancellationToken cancellationToken);

    Task<RuntimeClientSnapshot> StopGenerationAsync(
        RuntimeGeneration generation,
        BrokerStopReason reason,
        CancellationToken cancellationToken);

    Task ShutdownBrokerAsync(CancellationToken cancellationToken);
}
