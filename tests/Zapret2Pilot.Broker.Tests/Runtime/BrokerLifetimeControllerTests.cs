using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.Broker.Tests.Runtime;

public sealed class BrokerLifetimeControllerTests
{
    [Fact]
    public static async Task SuccessfulShutdownStopsRuntimeAndApplicationExactlyOnce()
    {
        FakeRuntimeSupervisor supervisor = new(Result.Success(Unit.Instance));
        FakeHostApplicationLifetime applicationLifetime = new();
        BrokerLifetimeController controller = new(supervisor, applicationLifetime);

        Result<Unit> first = await controller.ShutdownAsync(CancellationToken.None);
        Result<Unit> second = await controller.ShutdownAsync(CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, supervisor.StopCallCount);
        Assert.Equal(1, applicationLifetime.StopApplicationCallCount);
    }

    [Fact]
    public static async Task FailedRuntimeStopDoesNotTerminateBrokerPrematurely()
    {
        ErrorInfo error = new(
            code: "RuntimeStopFailed",
            message: "Simulated terminal cleanup failure.",
            severity: ErrorSeverity.Error,
            category: ErrorCategory.Runtime);
        FakeRuntimeSupervisor supervisor = new(Result.Failure<Unit>(error));
        FakeHostApplicationLifetime applicationLifetime = new();
        BrokerLifetimeController controller = new(supervisor, applicationLifetime);

        Result<Unit> result = await controller.ShutdownAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("RuntimeStopFailed", result.Error.Code);
        Assert.Equal(1, supervisor.StopCallCount);
        Assert.Equal(0, applicationLifetime.StopApplicationCallCount);
    }

    private sealed class FakeRuntimeSupervisor : IRuntimeSupervisor
    {
        private readonly Result<Unit> stopResult;

        public FakeRuntimeSupervisor(Result<Unit> stopResult)
        {
            this.stopResult = stopResult;
        }

        public int StopCallCount { get; private set; }

        public RuntimeSupervisorState CurrentState { get; } = new(
            RuntimeSupervisorStatus.Stopped,
            lastStartResult: null,
            guardResult: null,
            lastError: null,
            DateTimeOffset.UtcNow);

        public IObservable<RuntimeSupervisorState> StateChanged
            => Observable.Never<RuntimeSupervisorState>();

        public Task<Result<RuntimeProcessHostResult>> StartAsync(
            RuntimeProcessStartContext context,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<Unit>> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCallCount++;
            return Task.FromResult(stopResult);
        }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        public int StopApplicationCallCount { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopApplicationCallCount++;
        }
    }
}
