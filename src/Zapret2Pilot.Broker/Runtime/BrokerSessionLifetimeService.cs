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
public sealed partial class BrokerSessionLifetimeService : IHostedService, IDisposable
{
    private readonly IBrokerAppSessionLease appSessionLease;
    private readonly IBrokerLifetimeController lifetimeController;
    private readonly ILogger<BrokerSessionLifetimeService> logger;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly object sync = new();
    private Task? watchTask;
    private bool disposed;

    public BrokerSessionLifetimeService(
        IBrokerAppSessionLease appSessionLease,
        IBrokerLifetimeController lifetimeController,
        ILogger<BrokerSessionLifetimeService> logger)
    {
        ArgumentNullException.ThrowIfNull(appSessionLease);
        ArgumentNullException.ThrowIfNull(lifetimeController);
        ArgumentNullException.ThrowIfNull(logger);

        this.appSessionLease = appSessionLease;
        this.lifetimeController = lifetimeController;
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

        LogControlPlaneExited(logger);

        using CancellationTokenSource shutdownBudget =
            new(BrokerProtocolLimits.BrokerShutdownTimeout);

        try
        {
            Result<Unit> result = await lifetimeController
                .StopRuntimeAsync(shutdownBudget.Token)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogCleanupFailed(
                    logger,
                    result.Error.Code,
                    result.Error.Message);
            }
        }
        catch (OperationCanceledException)
        {
            LogCleanupTimedOut(logger);
        }
        catch (Exception ex)
        {
            LogCleanupThrew(logger, ex);
        }
        finally
        {
            lifetimeController.TerminateBroker();
        }
    }

    [LoggerMessage(
        EventId = 1701,
        Level = LogLevel.Warning,
        Message = "The admitted Control Plane process exited. Broker terminal cleanup is starting.")]
    private static partial void LogControlPlaneExited(ILogger logger);

    [LoggerMessage(
        EventId = 1702,
        Level = LogLevel.Critical,
        Message = "Runtime cleanup after Control Plane loss failed: {Code} {Message}. The privileged Broker will still terminate.")]
    private static partial void LogCleanupFailed(
        ILogger logger,
        string code,
        string message);

    [LoggerMessage(
        EventId = 1703,
        Level = LogLevel.Critical,
        Message = "Runtime cleanup after Control Plane loss exceeded the Broker shutdown budget. The privileged Broker will terminate.")]
    private static partial void LogCleanupTimedOut(ILogger logger);

    [LoggerMessage(
        EventId = 1704,
        Level = LogLevel.Critical,
        Message = "Runtime cleanup after Control Plane loss threw. The privileged Broker will terminate.")]
    private static partial void LogCleanupThrew(
        ILogger logger,
        Exception exception);
}
