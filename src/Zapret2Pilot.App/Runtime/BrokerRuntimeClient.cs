using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Contracts.Client;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.App.Runtime;

/// <summary>
/// Bounded session abstraction used by <see cref="BrokerRuntimeClient"/>.
/// Task 5 will bind this surface to the authenticated Named Pipe session.
/// </summary>
public interface IRuntimeBrokerSession
{
    BrokerRuntimeSnapshot CurrentSnapshot { get; }

    IObservable<BrokerRuntimeSnapshot> SnapshotChanged { get; }

    Task<BrokerRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken);

    Task<BrokerRuntimeSnapshot> StartPreparedPlanAsync(
        PreparedPlanId preparedPlanId,
        RuntimeGeneration expectedGeneration,
        CancellationToken cancellationToken);

    Task<BrokerRuntimeSnapshot> StopGenerationAsync(
        RuntimeGeneration generation,
        BrokerStopReason reason,
        CancellationToken cancellationToken);

    Task ShutdownBrokerAsync(CancellationToken cancellationToken);
}

/// <summary>
/// App-side projection over the session-scoped Runtime Broker.
/// It contains no reducer, process host, generation authority or lifecycle state machine.
/// </summary>
public sealed class BrokerRuntimeClient : IRuntimeClient
{
    private static readonly RuntimeClientSnapshot DisconnectedSnapshot = new(
        new RuntimeGeneration(0),
        BrokerRuntimeState.Stopped,
        ActivePlanId: null,
        ActiveOperationId: null);

    private readonly IRuntimeBrokerSession? session;
    private readonly IObservable<RuntimeClientSnapshot> snapshotChanged;

    public BrokerRuntimeClient(IRuntimeBrokerSession? session)
    {
        this.session = session;
        snapshotChanged = session is null
            ? Observable.Never<RuntimeClientSnapshot>()
            : session.SnapshotChanged.Select(
                static snapshot => RuntimeClientSnapshot.FromBroker(snapshot));
    }

    public RuntimeClientSnapshot CurrentSnapshot
        => session is null
            ? DisconnectedSnapshot
            : RuntimeClientSnapshot.FromBroker(session.CurrentSnapshot);

    public IObservable<RuntimeClientSnapshot> SnapshotChanged => snapshotChanged;

    public async Task<RuntimeClientSnapshot> GetRuntimeSnapshotAsync(
        CancellationToken cancellationToken)
    {
        BrokerRuntimeSnapshot snapshot = await RequireSession()
            .GetRuntimeSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        return RuntimeClientSnapshot.FromBroker(snapshot);
    }

    public async Task<RuntimeClientSnapshot> StartPreparedPlanAsync(
        PreparedPlanId preparedPlanId,
        RuntimeGeneration expectedGeneration,
        CancellationToken cancellationToken)
    {
        BrokerRuntimeSnapshot snapshot = await RequireSession()
            .StartPreparedPlanAsync(preparedPlanId, expectedGeneration, cancellationToken)
            .ConfigureAwait(false);

        return RuntimeClientSnapshot.FromBroker(snapshot);
    }

    public async Task<RuntimeClientSnapshot> StopGenerationAsync(
        RuntimeGeneration generation,
        BrokerStopReason reason,
        CancellationToken cancellationToken)
    {
        BrokerRuntimeSnapshot snapshot = await RequireSession()
            .StopGenerationAsync(generation, reason, cancellationToken)
            .ConfigureAwait(false);

        return RuntimeClientSnapshot.FromBroker(snapshot);
    }

    public Task ShutdownBrokerAsync(CancellationToken cancellationToken)
        => RequireSession().ShutdownBrokerAsync(cancellationToken);

    private IRuntimeBrokerSession RequireSession()
        => session ?? throw new InvalidOperationException(
            "Runtime Broker session is not connected.");
}
