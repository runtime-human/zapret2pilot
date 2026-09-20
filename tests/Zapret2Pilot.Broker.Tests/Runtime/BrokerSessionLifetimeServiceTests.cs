using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Broker.Tests.Runtime;

public sealed class BrokerSessionLifetimeServiceTests
{
    [Fact]
    public static async Task LeaseRemainsAliveAcrossOrdinaryTransportSilence()
    {
        using FakeAppSessionLease lease = new();
        FakeBrokerLifetimeController controller = new(Result.Success(Unit.Instance));
        using BrokerSessionLifetimeService service = CreateService(lease, controller);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(0, controller.StopRuntimeCallCount);
        Assert.Equal(0, controller.TerminateCallCount);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, controller.StopRuntimeCallCount);
        Assert.Equal(0, controller.TerminateCallCount);
    }

    [Fact]
    public static async Task AppProcessDeathRoutesCleanupThenTerminatesBroker()
    {
        using FakeAppSessionLease lease = new();
        FakeBrokerLifetimeController controller = new(Result.Success(Unit.Instance));
        using BrokerSessionLifetimeService service = CreateService(lease, controller);

        await service.StartAsync(CancellationToken.None);
        lease.SignalExit();

        await controller.TerminationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, controller.StopRuntimeCallCount);
        Assert.Equal(1, controller.TerminateCallCount);
    }

    [Fact]
    public static async Task AppProcessDeathStillTerminatesBrokerWhenCleanupFails()
    {
        ErrorInfo error = new(
            code: "RuntimeCleanupFailed",
            message: "Simulated cleanup failure.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);

        using FakeAppSessionLease lease = new();
        FakeBrokerLifetimeController controller = new(Result.Failure<Unit>(error));
        using BrokerSessionLifetimeService service = CreateService(lease, controller);

        await service.StartAsync(CancellationToken.None);
        lease.SignalExit();

        await controller.TerminationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, controller.StopRuntimeCallCount);
        Assert.Equal(1, controller.TerminateCallCount);
    }

    private static BrokerSessionLifetimeService CreateService(
        IBrokerAppSessionLease lease,
        IBrokerLifetimeController controller)
        => new(
            lease,
            controller,
            NullLogger<BrokerSessionLifetimeService>.Instance);

    private sealed class FakeAppSessionLease : IBrokerAppSessionLease
    {
        private readonly TaskCompletionSource<bool> exited = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForExitAsync(CancellationToken cancellationToken)
            => exited.Task.WaitAsync(cancellationToken);

        public void SignalExit() => exited.TrySetResult(true);

        public void Dispose() => exited.TrySetCanceled();
    }

    private sealed class FakeBrokerLifetimeController : IBrokerLifetimeController
    {
        private readonly Result<Unit> result;

        public FakeBrokerLifetimeController(Result<Unit> result)
        {
            this.result = result;
        }

        public int StopRuntimeCallCount { get; private set; }

        public int TerminateCallCount { get; private set; }

        public TaskCompletionSource<bool> TerminationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result<Unit>> StopRuntimeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopRuntimeCallCount++;
            return Task.FromResult(result);
        }

        public void TerminateBroker()
        {
            TerminateCallCount++;
            TerminationObserved.TrySetResult(true);
        }
    }
}
