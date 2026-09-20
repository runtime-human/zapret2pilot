using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.Broker.Runtime;

/// <summary>
/// Single terminal-lifetime path for the session-scoped Runtime Broker.
/// Authenticated ShutdownBroker requests and App-session death must converge
/// here so neither transport nor the Control Plane becomes a second runtime
/// lifecycle authority.
/// </summary>
public interface IBrokerLifetimeController
{
    Task<Result<Unit>> ShutdownAsync(CancellationToken cancellationToken);
}

public sealed class BrokerLifetimeController : IBrokerLifetimeController
{
    private readonly IRuntimeSupervisor supervisor;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly SemaphoreSlim shutdownGate = new(1, 1);
    private bool shutdownAccepted;

    public BrokerLifetimeController(
        IRuntimeSupervisor supervisor,
        IHostApplicationLifetime applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        this.supervisor = supervisor;
        this.applicationLifetime = applicationLifetime;
    }

    public async Task<Result<Unit>> ShutdownAsync(CancellationToken cancellationToken)
    {
        await shutdownGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (shutdownAccepted)
            {
                return Result.Success(Unit.Instance);
            }

            Result<Unit> stop = await supervisor
                .StopAsync(cancellationToken)
                .ConfigureAwait(false);
            if (stop.IsFailure)
            {
                return stop;
            }

            shutdownAccepted = true;
            applicationLifetime.StopApplication();
            return Result.Success(Unit.Instance);
        }
        finally
        {
            shutdownGate.Release();
        }
    }
}
