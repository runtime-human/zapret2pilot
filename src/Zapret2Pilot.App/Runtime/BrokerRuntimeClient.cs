using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Contracts.Client;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.App.Runtime;

/// <summary>
/// Bounded session abstraction used by <see cref="BrokerRuntimeClient"/>.
/// </summary>
public interface IRuntimeBrokerSession
{
    BrokerRuntimeSnapshot CurrentSnapshot { get; }

    IObservable<BrokerRuntimeSnapshot> SnapshotChanged { get; }

    Task<BrokerRuntimeSnapshot> GetRuntimeSnapshotAsync(
        CancellationToken cancellationToken);

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

public interface IConnectableRuntimeBrokerSession :
    IRuntimeBrokerSession,
    IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
}

public interface IRuntimeBrokerSessionFactory
{
    IConnectableRuntimeBrokerSession Create(
        Zapret2Pilot.Contracts.Security.BrokerClientBinding clientBinding,
        string pipeName,
        byte[] bootstrapSecret);
}

/// <summary>
/// App-side projection over the session-scoped Runtime Broker.
/// It contains no reducer, process host, generation authority or lifecycle
/// state machine. The session may be attached once after the one-shot
/// elevation/bootstrap handshake completes.
/// </summary>
public sealed class BrokerRuntimeClient : IRuntimeClient, IDisposable
{
    private static readonly RuntimeClientSnapshot DisconnectedSnapshot = new(
        new RuntimeGeneration(0),
        BrokerRuntimeState.Stopped,
        ActivePlanId: null,
        ActiveOperationId: null);

    private readonly object sync = new();
    private readonly BehaviorSubject<RuntimeClientSnapshot> snapshots =
        new(DisconnectedSnapshot);

    private IRuntimeBrokerSession? session;
    private IDisposable? sessionSubscription;
    private bool disposed;

    public BrokerRuntimeClient(IRuntimeBrokerSession? session = null)
    {
        if (session is not null)
        {
            AttachSession(session);
        }
    }

    public RuntimeClientSnapshot CurrentSnapshot => snapshots.Value;

    public IObservable<RuntimeClientSnapshot> SnapshotChanged
        => snapshots.AsObservable();

    public bool IsConnected
    {
        get
        {
            lock (sync)
            {
                return session is not null;
            }
        }
    }

    public void AttachSession(IRuntimeBrokerSession brokerSession)
    {
        ArgumentNullException.ThrowIfNull(brokerSession);
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (sync)
        {
            if (session is not null)
            {
                throw new InvalidOperationException(
                    "A Runtime Broker session is already attached.");
            }

            session = brokerSession;
            snapshots.OnNext(
                RuntimeClientSnapshot.FromBroker(
                    brokerSession.CurrentSnapshot));
            sessionSubscription = brokerSession.SnapshotChanged.Subscribe(
                snapshot => snapshots.OnNext(
                    RuntimeClientSnapshot.FromBroker(snapshot)));
        }
    }

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
            .StartPreparedPlanAsync(
                preparedPlanId,
                expectedGeneration,
                cancellationToken)
            .ConfigureAwait(false);

        return RuntimeClientSnapshot.FromBroker(snapshot);
    }

    public async Task<RuntimeClientSnapshot> StopGenerationAsync(
        RuntimeGeneration generation,
        BrokerStopReason reason,
        CancellationToken cancellationToken)
    {
        BrokerRuntimeSnapshot snapshot = await RequireSession()
            .StopGenerationAsync(
                generation,
                reason,
                cancellationToken)
            .ConfigureAwait(false);

        return RuntimeClientSnapshot.FromBroker(snapshot);
    }

    public Task ShutdownBrokerAsync(CancellationToken cancellationToken)
        => RequireSession().ShutdownBrokerAsync(cancellationToken);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (sync)
        {
            sessionSubscription?.Dispose();
            sessionSubscription = null;
            session = null;
        }

        snapshots.OnCompleted();
        snapshots.Dispose();
    }

    private IRuntimeBrokerSession RequireSession()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (sync)
        {
            return session ?? throw new InvalidOperationException(
                "Runtime Broker session is not connected.");
        }
    }
}
