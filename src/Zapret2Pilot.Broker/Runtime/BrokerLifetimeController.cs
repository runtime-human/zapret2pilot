using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.Broker.Runtime;

/// <summary>
/// Single terminal-lifetime path for the session-scoped Runtime Broker.
///
/// Runtime cleanup and process termination are deliberately two phases:
/// ShutdownBroker must be able to flush its Accepted response before the host
/// begins stopping. App-session death, by contrast, terminates the Broker
/// after best-effort cleanup even when cleanup reports a failure.
/// </summary>
public interface IBrokerLifetimeController
{
    Task<Result<Unit>> StopRuntimeAsync(CancellationToken cancellationToken);

    void TerminateBroker();
}

public sealed class BrokerLifetimeController : IBrokerLifetimeController, IDisposable
{
    private readonly IRuntimeSupervisor supervisor;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly SemaphoreSlim shutdownGate = new(1, 1);
    private bool runtimeStopped;
    private int terminationRequested;
    private int disposed;

    public BrokerLifetimeController(
        IRuntimeSupervisor supervisor,
        IHostApplicationLifetime applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        this.supervisor = supervisor;
        this.applicationLifetime = applicationLifetime;
    }

    public async Task<Result<Unit>> StopRuntimeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        await shutdownGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (runtimeStopped)
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

            runtimeStopped = true;
            return Result.Success(Unit.Instance);
        }
        finally
        {
            shutdownGate.Release();
        }
    }

    public void TerminateBroker()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);

        if (Interlocked.Exchange(ref terminationRequested, 1) != 0)
        {
            return;
        }

        applicationLifetime.StopApplication();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        shutdownGate.Dispose();
    }
}
