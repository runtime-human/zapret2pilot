using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
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
        FakeHostApplicationLifetime appLifetime = new();
        using BrokerSessionLifetimeService service = CreateService(
            lease,
            controller,
            appLifetime);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(0, controller.ShutdownCallCount);
        Assert.Equal(0, appLifetime.StopApplicationCallCount);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, controller.ShutdownCallCount);
    }

    [Fact]
    public static async Task AppProcessDeathRoutesCleanupThroughLifetimeController()
    {
        using FakeAppSessionLease lease = new();
        FakeBrokerLifetimeController controller = new(Result.Success(Unit.Instance));
        FakeHostApplicationLifetime appLifetime = new();
        using BrokerSessionLifetimeService service = CreateService(
            lease,
            controller,
            appLifetime);

        await service.StartAsync(CancellationToken.None);
        lease.SignalExit();

        await controller.ShutdownObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, controller.ShutdownCallCount);
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
        FakeHostApplicationLifetime appLifetime = new();
        using BrokerSessionLifetimeService service = CreateService(
            lease,
            controller,
            appLifetime);

        await service.StartAsync(CancellationToken.None);
        lease.SignalExit();

        await appLifetime.StopObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, controller.ShutdownCallCount);
        Assert.Equal(1, appLifetime.StopApplicationCallCount);
    }

    private static BrokerSessionLifetimeService CreateService(
        IBrokerAppSessionLease lease,
        IBrokerLifetimeController controller,
        IHostApplicationLifetime appLifetime)
        => new(
            lease,
            controller,
            appLifetime,
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

        public int ShutdownCallCount { get; private set; }

        public TaskCompletionSource<bool> ShutdownObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result<Unit>> ShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShutdownCallCount++;
            ShutdownObserved.TrySetResult(true);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        public int StopApplicationCallCount { get; private set; }

        public TaskCompletionSource<bool> StopObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopApplicationCallCount++;
            StopObserved.TrySetResult(true);
        }
    }
}
