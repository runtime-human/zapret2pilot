using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Broker.Runtime;

/// <summary>
/// Retained lifetime proof for the admitted Control Plane process.
/// Transport disconnect/reconnect does not complete this lease; only death of
/// the verified process object does.
/// </summary>
public interface IBrokerAppSessionLease : IDisposable
{
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Binds Broker lifetime to the admitted Control Plane process without making
/// transport connectivity a lifecycle authority.
/// </summary>
public sealed class BrokerSessionLifetimeService : IHostedService, IDisposable
{
    private readonly IBrokerAppSessionLease appSessionLease;
    private readonly IBrokerLifetimeController lifetimeController;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly ILogger<BrokerSessionLifetimeService> logger;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly object sync = new();
    private Task? watchTask;
    private bool disposed;

    public BrokerSessionLifetimeService(
        IBrokerAppSessionLease appSessionLease,
        IBrokerLifetimeController lifetimeController,
        IHostApplicationLifetime applicationLifetime,
        ILogger<BrokerSessionLifetimeService> logger)
    {
        ArgumentNullException.ThrowIfNull(appSessionLease);
        ArgumentNullException.ThrowIfNull(lifetimeController);
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        this.appSessionLease = appSessionLease;
        this.lifetimeController = lifetimeController;
        this.applicationLifetime = applicationLifetime;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            watchTask ??= WatchAppSessionAsync(lifetimeCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? task;
        lock (sync)
        {
            task = watchTask;
        }

        lifetimeCts.Cancel();

        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // Normal Broker shutdown stops the lease watcher.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCts.Cancel();
        appSessionLease.Dispose();
        lifetimeCts.Dispose();
    }

    private async Task WatchAppSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await appSessionLease.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        logger.LogWarning(
            "The admitted Control Plane process exited. Broker terminal cleanup is starting.");

        using CancellationTokenSource shutdownBudget =
            new(BrokerProtocolLimits.BrokerShutdownTimeout);

        try
        {
            Result<Unit> result = await lifetimeController
                .ShutdownAsync(shutdownBudget.Token)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                logger.LogCritical(
                    "Runtime cleanup after Control Plane loss failed: {Code} {Message}. The privileged Broker will still terminate.",
                    result.Error.Code,
                    result.Error.Message);
                applicationLifetime.StopApplication();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogCritical(
                "Runtime cleanup after Control Plane loss exceeded the Broker shutdown budget. The privileged Broker will terminate.");
            applicationLifetime.StopApplication();
        }
        catch (Exception ex)
        {
            logger.LogCritical(
                ex,
                "Runtime cleanup after Control Plane loss threw. The privileged Broker will terminate.");
            applicationLifetime.StopApplication();
        }
    }
}
