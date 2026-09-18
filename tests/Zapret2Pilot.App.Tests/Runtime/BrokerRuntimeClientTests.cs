using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.App.Runtime;
using Zapret2Pilot.Contracts.Client;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.App.Tests.Runtime;

public sealed class BrokerRuntimeClientTests
{
    [Fact]
    public static void CurrentSnapshotAndEventsArePureBrokerProjections()
    {
        PreparedPlanId planId = PreparedPlanId.New();
        BrokerOperationId operationId = BrokerOperationId.New();
        BrokerRuntimeSnapshot initial = new(
            new RuntimeGeneration(7),
            BrokerRuntimeState.Running,
            planId,
            operationId);

        using FakeRuntimeBrokerSession session = new(initial);
        BrokerRuntimeClient client = new(session);

        Assert.Equal(RuntimeClientSnapshot.FromBroker(initial), client.CurrentSnapshot);

        RuntimeClientSnapshot? observed = null;
        using IDisposable subscription = client.SnapshotChanged.Subscribe(
            snapshot => observed = snapshot);

        BrokerRuntimeSnapshot next = new(
            new RuntimeGeneration(8),
            BrokerRuntimeState.Stopping,
            planId,
            BrokerOperationId.New());

        session.Publish(next);

        Assert.Equal(RuntimeClientSnapshot.FromBroker(next), observed);
    }

    [Fact]
    public static async Task LifecycleMethodsForwardOnlyBoundedBrokerIdentities()
    {
        BrokerRuntimeSnapshot snapshot = new(
            new RuntimeGeneration(9),
            BrokerRuntimeState.Stopped,
            ActivePlanId: null,
            ActiveOperationId: null);

        using FakeRuntimeBrokerSession session = new(snapshot);
        BrokerRuntimeClient client = new(session);
        PreparedPlanId planId = PreparedPlanId.New();
        RuntimeGeneration generation = new(9);

        Assert.Equal(
            RuntimeClientSnapshot.FromBroker(snapshot),
            await client.GetRuntimeSnapshotAsync(CancellationToken.None));

        Assert.Equal(
            RuntimeClientSnapshot.FromBroker(snapshot),
            await client.StartPreparedPlanAsync(
                planId,
                generation,
                CancellationToken.None));

        Assert.Equal(
            RuntimeClientSnapshot.FromBroker(snapshot),
            await client.StopGenerationAsync(
                generation,
                BrokerStopReason.UserRequested,
                CancellationToken.None));

        await client.ShutdownBrokerAsync(CancellationToken.None);

        Assert.Equal(1, session.GetSnapshotCallCount);
        Assert.Equal(1, session.StartCallCount);
        Assert.Equal(planId, session.LastPreparedPlanId);
        Assert.Equal(generation, session.LastExpectedGeneration);
        Assert.Equal(1, session.StopCallCount);
        Assert.Equal(generation, session.LastStopGeneration);
        Assert.Equal(BrokerStopReason.UserRequested, session.LastStopReason);
        Assert.Equal(1, session.ShutdownCallCount);
    }

    [Fact]
    public static async Task DisconnectedClientFailsClosedForBrokerOperations()
    {
        BrokerRuntimeClient client = new(session: null);

        Assert.Equal(BrokerRuntimeState.Stopped, client.CurrentSnapshot.State);
        Assert.Equal(0, client.CurrentSnapshot.Generation.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetRuntimeSnapshotAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StartPreparedPlanAsync(
                PreparedPlanId.New(),
                new RuntimeGeneration(1),
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StopGenerationAsync(
                new RuntimeGeneration(1),
                BrokerStopReason.UserRequested,
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ShutdownBrokerAsync(CancellationToken.None));
    }

    private sealed class FakeRuntimeBrokerSession : IRuntimeBrokerSession, IDisposable
    {
        private readonly BehaviorSubject<BrokerRuntimeSnapshot> snapshots;

        public FakeRuntimeBrokerSession(BrokerRuntimeSnapshot initialSnapshot)
        {
            snapshots = new BehaviorSubject<BrokerRuntimeSnapshot>(initialSnapshot);
        }

        public BrokerRuntimeSnapshot CurrentSnapshot => snapshots.Value;

        public IObservable<BrokerRuntimeSnapshot> SnapshotChanged => snapshots;

        public int GetSnapshotCallCount { get; private set; }

        public int StartCallCount { get; private set; }

        public PreparedPlanId? LastPreparedPlanId { get; private set; }

        public RuntimeGeneration? LastExpectedGeneration { get; private set; }

        public int StopCallCount { get; private set; }

        public RuntimeGeneration? LastStopGeneration { get; private set; }

        public BrokerStopReason? LastStopReason { get; private set; }

        public int ShutdownCallCount { get; private set; }

        public Task<BrokerRuntimeSnapshot> GetRuntimeSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetSnapshotCallCount++;
            return Task.FromResult(CurrentSnapshot);
        }

        public Task<BrokerRuntimeSnapshot> StartPreparedPlanAsync(
            PreparedPlanId preparedPlanId,
            RuntimeGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCallCount++;
            LastPreparedPlanId = preparedPlanId;
            LastExpectedGeneration = expectedGeneration;
            return Task.FromResult(CurrentSnapshot);
        }

        public Task<BrokerRuntimeSnapshot> StopGenerationAsync(
            RuntimeGeneration generation,
            BrokerStopReason reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCallCount++;
            LastStopGeneration = generation;
            LastStopReason = reason;
            return Task.FromResult(CurrentSnapshot);
        }

        public Task ShutdownBrokerAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShutdownCallCount++;
            return Task.CompletedTask;
        }

        public void Publish(BrokerRuntimeSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            snapshots.OnNext(snapshot);
        }

        public void Dispose()
        {
            snapshots.Dispose();
        }
    }
}
