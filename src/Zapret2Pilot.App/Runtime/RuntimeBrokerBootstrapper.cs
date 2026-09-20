using System;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.App.Runtime;

public sealed record RuntimeBrokerBootstrapResult(
    bool Succeeded,
    string? ErrorCode = null,
    string? Message = null)
{
    public static RuntimeBrokerBootstrapResult Success { get; } =
        new(true);
}

public interface IRuntimeBrokerBootstrapper
{
    Task<RuntimeBrokerBootstrapResult> StartBrokerAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Unelevated Control-Plane coordinator for one session-scoped Broker.
///
/// It creates the App binding and secret, creates the bootstrap pipe before
/// elevation, launches only the known sibling broker executable through UAC,
/// transfers the secret after exact process verification, then attaches the
/// authenticated runtime session to the projection-only BrokerRuntimeClient.
/// </summary>
public sealed partial class RuntimeBrokerBootstrapper :
    IRuntimeBrokerBootstrapper,
    IHostedService,
    IDisposable
{
    private const int UacCancelledErrorCode = 1223;

    private readonly IAppProcessBindingProvider bindingProvider;
    private readonly IBrokerExecutableLocator executableLocator;
    private readonly IElevatedBrokerLauncher brokerLauncher;
    private readonly IBrokerBootstrapServerFactory bootstrapServerFactory;
    private readonly BrokerRuntimeClient runtimeClient;
    private readonly ILogger<RuntimeBrokerBootstrapper> logger;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);

    private NamedPipeRuntimeBrokerSession? session;
    private IElevatedBrokerProcess? brokerProcess;
    private bool started;
    private bool disposed;

    public RuntimeBrokerBootstrapper(
        IAppProcessBindingProvider bindingProvider,
        IBrokerExecutableLocator executableLocator,
        IElevatedBrokerLauncher brokerLauncher,
        IBrokerBootstrapServerFactory bootstrapServerFactory,
        BrokerRuntimeClient runtimeClient,
        ILogger<RuntimeBrokerBootstrapper> logger)
    {
        ArgumentNullException.ThrowIfNull(bindingProvider);
        ArgumentNullException.ThrowIfNull(executableLocator);
        ArgumentNullException.ThrowIfNull(brokerLauncher);
        ArgumentNullException.ThrowIfNull(bootstrapServerFactory);
        ArgumentNullException.ThrowIfNull(runtimeClient);
        ArgumentNullException.ThrowIfNull(logger);

        this.bindingProvider = bindingProvider;
        this.executableLocator = executableLocator;
        this.brokerLauncher = brokerLauncher;
        this.bootstrapServerFactory = bootstrapServerFactory;
        this.runtimeClient = runtimeClient;
        this.logger = logger;
    }

    // Host start is intentionally passive. The existing lifecycle pipeline
    // invokes StartBrokerAsync from OwnershipRecovery after the shell is
    // visible and storage/deployment verification has completed.
    Task IHostedService.StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<RuntimeBrokerBootstrapResult> StartBrokerAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await lifecycleGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (started)
            {
                return RuntimeBrokerBootstrapResult.Success;
            }

            AppSessionId appSessionId = AppSessionId.New();
            BrokerClientBinding binding =
                bindingProvider.Create(appSessionId);

            string bootstrapPipeName =
                $"z2p-bootstrap-{Guid.NewGuid():N}";
            string runtimePipeName =
                $"z2p-runtime-{appSessionId.Value:N}";
            string brokerExecutable =
                executableLocator.ResolveBrokerExecutablePath();

            byte[] secret = RandomNumberGenerator.GetBytes(
                BrokerAuthenticator.SecretSizeBytes);

            IElevatedBrokerProcess? launchedBroker = null;
            NamedPipeRuntimeBrokerSession? candidateSession = null;

            try
            {
                await using IBrokerBootstrapServer bootstrapServer =
                    bootstrapServerFactory.Create(
                        bootstrapPipeName,
                        binding.UserSid);

                try
                {
                    launchedBroker = brokerLauncher.Launch(
                        brokerExecutable,
                        bootstrapPipeName,
                        binding.ProcessId);
                }
                catch (Win32Exception ex) when (
                    ex.NativeErrorCode == UacCancelledErrorCode)
                {
                    LogElevationCancelled(logger);
                    return new(
                        false,
                        "BrokerElevationCancelled",
                        "Runtime Broker elevation was cancelled.");
                }

                await bootstrapServer.WaitForVerifiedConnectionAsync(
                    launchedBroker,
                    cancellationToken).ConfigureAwait(false);

                BrokerBootstrapMessage bootstrap = new(
                    BrokerProtocolVersion.V1,
                    appSessionId,
                    runtimePipeName,
                    binding,
                    secret);

                await bootstrapServer.SendAsync(
                    bootstrap,
                    cancellationToken).ConfigureAwait(false);

                candidateSession = new NamedPipeRuntimeBrokerSession(
                    binding,
                    runtimePipeName,
                    secret);

                await candidateSession.ConnectAsync(
                    cancellationToken).ConfigureAwait(false);

                runtimeClient.AttachSession(candidateSession);

                session = candidateSession;
                candidateSession = null;
                brokerProcess = launchedBroker;
                launchedBroker = null;
                started = true;

                LogBrokerConnected(logger);
                return RuntimeBrokerBootstrapResult.Success;
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException)
            {
                LogBootstrapFailed(logger, ex);
                return new(
                    false,
                    "RuntimeBrokerBootstrapFailed",
                    ex.Message);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);

                if (candidateSession is not null)
                {
                    await candidateSession.DisposeAsync()
                        .ConfigureAwait(false);
                }

                if (launchedBroker is not null)
                {
                    _ = launchedBroker.TryTerminate();
                    launchedBroker.Dispose();
                }
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return;
        }

        await lifecycleGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            NamedPipeRuntimeBrokerSession? activeSession =
                session;
            IElevatedBrokerProcess? activeProcess =
                brokerProcess;

            session = null;
            brokerProcess = null;
            started = false;

            if (activeSession is not null)
            {
                try
                {
                    using CancellationTokenSource shutdownBudget =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                    shutdownBudget.CancelAfter(
                        BrokerProtocolLimits.BrokerShutdownTimeout);

                    await activeSession.ShutdownBrokerAsync(
                        shutdownBudget.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    ex is IOException
                        or InvalidOperationException
                        or OperationCanceledException)
                {
                    LogGracefulShutdownFailed(logger, ex);
                }
                finally
                {
                    await activeSession.DisposeAsync()
                        .ConfigureAwait(false);
                }
            }

            if (activeProcess is not null)
            {
                try
                {
                    using CancellationTokenSource exitBudget =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                    exitBudget.CancelAfter(
                        BrokerProtocolLimits.BrokerShutdownTimeout);

                    await activeProcess.WaitForExitAsync(
                        exitBudget.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    LogBrokerExitTimedOut(logger);
                    _ = activeProcess.TryTerminate();
                }
                finally
                {
                    activeProcess.Dispose();
                }
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifecycleGate.Dispose();
    }

    [LoggerMessage(
        EventId = 1901,
        Level = LogLevel.Information,
        Message = "Runtime Broker authenticated session connected.")]
    private static partial void LogBrokerConnected(
        ILogger logger);

    [LoggerMessage(
        EventId = 1902,
        Level = LogLevel.Warning,
        Message = "Runtime Broker elevation was cancelled by the user.")]
    private static partial void LogElevationCancelled(
        ILogger logger);

    [LoggerMessage(
        EventId = 1903,
        Level = LogLevel.Error,
        Message = "Runtime Broker bootstrap failed.")]
    private static partial void LogBootstrapFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1904,
        Level = LogLevel.Warning,
        Message = "Graceful Runtime Broker shutdown failed; process exit will still be bounded.")]
    private static partial void LogGracefulShutdownFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1905,
        Level = LogLevel.Warning,
        Message = "Runtime Broker did not exit within the shutdown budget; best-effort termination requested.")]
    private static partial void LogBrokerExitTimedOut(
        ILogger logger);
}
