using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Broker.Transport;

public sealed record BrokerPipeServerOptions(
    string PipeName,
    string ExpectedUserSid);

/// <summary>
/// Concrete local Named Pipe transport for one Broker AppSession.
/// It performs bounded I/O and #16 authentication, then forwards only
/// admitted typed requests to <see cref="BrokerAuthenticatedSession"/>.
/// </summary>
public sealed partial class WindowsBrokerPipeServer : IHostedService, IDisposable
{
    private readonly BrokerPipeServerOptions options;
    private readonly IBrokerNamedPipeFactory pipeFactory;
    private readonly IBrokerPeerIdentityResolver peerResolver;
    private readonly BrokerAuthenticatedSession session;
    private readonly IBrokerAppSessionLeaseBinder leaseBinder;
    private readonly ILogger<WindowsBrokerPipeServer> logger;
    private readonly CancellationTokenSource serverCts = new();
    private readonly object sync = new();
    private Task[] workers = [];
    private bool disposed;

    public WindowsBrokerPipeServer(
        BrokerPipeServerOptions options,
        IBrokerNamedPipeFactory pipeFactory,
        IBrokerPeerIdentityResolver peerResolver,
        BrokerAuthenticatedSession session,
        IBrokerAppSessionLeaseBinder leaseBinder,
        ILogger<WindowsBrokerPipeServer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeFactory);
        ArgumentNullException.ThrowIfNull(peerResolver);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(leaseBinder);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.pipeFactory = pipeFactory;
        this.peerResolver = peerResolver;
        this.session = session;
        this.leaseBinder = leaseBinder;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            if (workers.Length != 0)
            {
                return Task.CompletedTask;
            }

            // Create the first instance synchronously and fail host startup if
            // the pipe namespace is already occupied. Only subsequent instances
            // omit FILE_FLAG_FIRST_PIPE_INSTANCE.
            NamedPipeServerStream first = pipeFactory.Create(
                options.PipeName,
                options.ExpectedUserSid,
                firstInstance: true);

            workers =
            [
                RunAcceptLoopAsync(first, serverCts.Token),
                RunAcceptLoopAsync(initialServer: null, serverCts.Token),
            ];
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        serverCts.Cancel();

        Task[] snapshot;
        lock (sync)
        {
            snapshot = workers;
        }

        if (snapshot.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(snapshot)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (serverCts.IsCancellationRequested)
        {
            // Normal server shutdown.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        serverCts.Cancel();
        session.Dispose();
        serverCts.Dispose();
    }

    private async Task RunAcceptLoopAsync(
        NamedPipeServerStream? initialServer,
        CancellationToken cancellationToken)
    {
        NamedPipeServerStream? server = initialServer;

        while (!cancellationToken.IsCancellationRequested)
        {
            server ??= pipeFactory.Create(
                options.PipeName,
                options.ExpectedUserSid,
                firstInstance: false);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

                await HandleConnectionAsync(server, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException ex)
            {
                LogIoFailure(logger, ex);
            }
            catch (InvalidDataException ex)
            {
                LogMalformedFrame(logger, ex);
            }
            catch (Exception ex)
            {
                LogConnectionFailure(logger, ex);
            }
            finally
            {
                await server.DisposeAsync().ConfigureAwait(false);
                server = null;
            }
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        BrokerResolvedPeer initialPeer = peerResolver.Resolve(
            pipe.SafePipeHandle);
        bool initialLeaseConsumed = false;
        bool authenticatedConnection = false;
        BrokerChallengeHandle? openChallenge = null;

        try
        {
            BrokerChallengeIssueResult challengeResult =
                session.TryIssueChallenge(initialPeer.Identity);
            if (challengeResult.Status != BrokerChallengeIssueStatus.Issued
                || challengeResult.Challenge is null
                || challengeResult.Handle is null)
            {
                LogChallengeRejected(logger, challengeResult.Status);
                return;
            }

            openChallenge = challengeResult.Handle.Value;

            byte[] challengeFrame =
                BrokerChallengeFrameCodec.Encode(challengeResult.Challenge);
            await BrokerPipeFrameIO.WriteFramedBytesAsync(
                pipe,
                challengeFrame,
                cancellationToken).ConfigureAwait(false);

            byte[]? helloPayload = await BrokerPipeFrameIO.ReadPayloadAsync(
                pipe,
                BrokerProtocolLimits.MaxPreAuthHelloFrameBytes,
                cancellationToken).ConfigureAwait(false);
            if (helloPayload is null)
            {
                return;
            }

            BrokerProtocolDecodeResult helloDecode =
                BrokerProtocolCodec.DecodeRequest(helloPayload);
            if (helloDecode.Status != BrokerProtocolDecodeStatus.Success
                || helloDecode.Envelope is null
                || helloDecode.Envelope.Request is not BrokerHelloRequest)
            {
                LogHelloRejected(logger, helloDecode.Status);
                return;
            }

            // Resolve the kernel-observed pipe peer again immediately before
            // admission. The challenge state also contains the original peer,
            // so a changed/reused identity fails closed.
            BrokerResolvedPeer admissionPeer = peerResolver.Resolve(
                pipe.SafePipeHandle);
            BrokerAdmissionDecision admission;
            using (admissionPeer.ProcessLease)
            {
                admission = session.TryAuthenticate(
                    openChallenge.Value,
                    admissionPeer.Identity,
                    helloDecode.Envelope);
            }

            openChallenge = null;

            if (!admission.Accepted)
            {
                LogAdmissionRejected(logger, admission.RejectionReason);
                return;
            }

            authenticatedConnection = true;

            // Binder takes ownership on success and disposes a replacement
            // lease on reconnect. In either case the caller must not dispose
            // initialPeer.ProcessLease again.
            _ = leaseBinder.TryBind(initialPeer.ProcessLease);
            initialLeaseConsumed = true;

            await RunAuthenticatedLoopAsync(
                pipe,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (openChallenge is BrokerChallengeHandle challenge)
            {
                session.AbandonChallenge(challenge);
            }

            if (authenticatedConnection)
            {
                session.ReleaseAuthenticatedConnection();
            }

            if (!initialLeaseConsumed)
            {
                initialPeer.ProcessLease.Dispose();
            }
        }
    }

    private async Task RunAuthenticatedLoopAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? payload = await BrokerPipeFrameIO.ReadPayloadAsync(
                pipe,
                BrokerProtocolLimits.MaxFrameBytes,
                cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return;
            }

            BrokerProtocolDecodeResult decode =
                BrokerProtocolCodec.DecodeRequest(payload);
            if (decode.Status != BrokerProtocolDecodeStatus.Success
                || decode.Envelope is null)
            {
                LogRequestRejected(logger, decode.Status);
                return;
            }

            BrokerRequestEnvelope request = decode.Envelope;
            BrokerResponseEnvelope response = await session
                .DispatchAsync(request, cancellationToken)
                .ConfigureAwait(false);

            byte[] responsePayload =
                BrokerResponseProtocolCodec.EncodeResponse(response);
            await BrokerPipeFrameIO.WritePayloadAsync(
                pipe,
                responsePayload,
                BrokerProtocolLimits.MaxFrameBytes,
                cancellationToken).ConfigureAwait(false);

            session.OnResponseFlushed(request, response);

            if (request.Request is ShutdownBrokerRequest
                && response.Status == BrokerResponseStatus.Accepted)
            {
                return;
            }
        }
    }

    [LoggerMessage(
        EventId = 1801,
        Level = LogLevel.Debug,
        Message = "Broker pipe connection ended because of bounded I/O failure.")]
    private static partial void LogIoFailure(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1802,
        Level = LogLevel.Warning,
        Message = "Broker pipe rejected malformed or oversized framing.")]
    private static partial void LogMalformedFrame(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1803,
        Level = LogLevel.Error,
        Message = "Broker pipe connection failed.")]
    private static partial void LogConnectionFailure(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1804,
        Level = LogLevel.Warning,
        Message = "Broker pre-auth connection rejected before challenge. Status={Status}.")]
    private static partial void LogChallengeRejected(
        ILogger logger,
        BrokerChallengeIssueStatus status);

    [LoggerMessage(
        EventId = 1805,
        Level = LogLevel.Warning,
        Message = "Broker pre-auth Hello decode rejected. Status={Status}.")]
    private static partial void LogHelloRejected(
        ILogger logger,
        BrokerProtocolDecodeStatus status);

    [LoggerMessage(
        EventId = 1806,
        Level = LogLevel.Warning,
        Message = "Broker pre-auth admission rejected. Reason={Reason}.")]
    private static partial void LogAdmissionRejected(
        ILogger logger,
        BrokerAdmissionRejectionReason reason);

    [LoggerMessage(
        EventId = 1807,
        Level = LogLevel.Warning,
        Message = "Authenticated Broker request decode rejected. Status={Status}.")]
    private static partial void LogRequestRejected(
        ILogger logger,
        BrokerProtocolDecodeStatus status);
}
