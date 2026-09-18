using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Contracts.Client;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.App.ViewModelTests;

public sealed class FakeRuntimeClient : IRuntimeClient, IDisposable
{
    private readonly BehaviorSubject<RuntimeClientSnapshot> snapshots = new(
        new RuntimeClientSnapshot(
            new RuntimeGeneration(0),
            BrokerRuntimeState.Stopped,
            ActivePlanId: null,
            ActiveOperationId: null));

    public RuntimeClientSnapshot CurrentSnapshot => snapshots.Value;

    public IObservable<RuntimeClientSnapshot> SnapshotChanged => snapshots.AsObservable();

    public Task<RuntimeClientSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentSnapshot);
    }

    public Task<RuntimeClientSnapshot> StartPreparedPlanAsync(
        PreparedPlanId preparedPlanId,
        RuntimeGeneration expectedGeneration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentSnapshot);
    }

    public Task<RuntimeClientSnapshot> StopGenerationAsync(
        RuntimeGeneration generation,
        BrokerStopReason reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentSnapshot);
    }

    public Task ShutdownBrokerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void Publish(RuntimeClientSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshots.OnNext(snapshot);
    }

    public void Dispose()
    {
        snapshots.Dispose();
    }
}
