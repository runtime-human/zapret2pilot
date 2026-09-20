using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
    private readonly IBrokerAuthenticatedSession session;
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
        IBrokerAuthenticatedSession session,
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
        SemaphoreSlim requestSlots = new(
            BrokerProtocolLimits.IngressQueueCapacity,
            BrokerProtocolLimits.IngressQueueCapacity);
        SemaphoreSlim responseWriteGate = new(1, 1);

        HashSet<Task> inFlight = [];
        bool terminalShutdownRequest = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? payload = await BrokerPipeFrameIO.ReadPayloadAsync(
                    pipe,
                    BrokerProtocolLimits.MaxFrameBytes,
                    cancellationToken).ConfigureAwait(false);
                if (payload is null)
                {
                    // Ordinary transport disconnect. Already-admitted runtime
                    // work must continue under Kernel authority, while this
                    // authenticated connection is released immediately so a
                    // fresh challenge/reconnect can be admitted.
                    break;
                }

                BrokerProtocolDecodeResult decode =
                    BrokerProtocolCodec.DecodeRequest(payload);
                if (decode.Status != BrokerProtocolDecodeStatus.Success
                    || decode.Envelope is null)
                {
                    LogRequestRejected(logger, decode.Status);
                    break;
                }

                await requestSlots.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                BrokerRequestEnvelope request = decode.Envelope;

                // Do not await runtime dispatch on the reader loop. A Start
                // may remain in flight until a later Stop supersedes it; the
                // Stop must be readable and dispatchable on this connection.
                Task dispatch = DispatchAndWriteAsync(
                    pipe,
                    request,
                    responseWriteGate,
                    requestSlots,
                    cancellationToken);

                inFlight.Add(dispatch);
                RemoveCompleted(inFlight);

                if (request.Request is ShutdownBrokerRequest)
                {
                    terminalShutdownRequest = true;
                    break;
                }
            }
        }
        finally
        {
            RemoveCompleted(inFlight);

            if (inFlight.Count == 0)
            {
                responseWriteGate.Dispose();
                requestSlots.Dispose();
            }
            else if (cancellationToken.IsCancellationRequested
                || terminalShutdownRequest)
            {
                // Host teardown and explicit ShutdownBroker must not dispose
                // session/transport dependencies while dispatch tasks still
                // use them. ShutdownBroker additionally needs its response
                // flushed before OnResponseFlushed terminates the Broker.
                try
                {
                    await Task.WhenAll(inFlight).ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    ex is IOException
                        or ObjectDisposedException
                        or OperationCanceledException)
                {
                    LogPendingResponseDrainFailed(logger, ex);
                }
                finally
                {
                    responseWriteGate.Dispose();
                    requestSlots.Dispose();
                }
            }
            else
            {
                // Ordinary disconnect/protocol close: release the
                // authenticated connection now. Keep per-connection resources
                // alive only until already-admitted operations unwind. Their
                // late responses may fail because the old pipe is gone; that
                // has no effect on Kernel state and is observed by the drain.
                ScheduleDetachedDrain(
                    inFlight,
                    responseWriteGate,
                    requestSlots);
            }
        }
    }

    private async Task DispatchAndWriteAsync(
        NamedPipeServerStream pipe,
        BrokerRequestEnvelope request,
        SemaphoreSlim responseWriteGate,
        SemaphoreSlim requestSlots,
        CancellationToken serverCancellationToken)
    {
        try
        {
            // Do not bind accepted runtime work to ordinary client
            // disconnect. The server lifetime token cancels only when the
            // Broker itself is shutting down.
            BrokerResponseEnvelope response = await session
                .DispatchAsync(request, serverCancellationToken)
                .ConfigureAwait(false);

            byte[] responsePayload =
                BrokerResponseProtocolCodec.EncodeResponse(response);

            await responseWriteGate.WaitAsync(serverCancellationToken)
                .ConfigureAwait(false);
            try
            {
                await BrokerPipeFrameIO.WritePayloadAsync(
                    pipe,
                    responsePayload,
                    BrokerProtocolLimits.MaxFrameBytes,
                    serverCancellationToken).ConfigureAwait(false);

                session.OnResponseFlushed(request, response);
            }
            finally
            {
                responseWriteGate.Release();
            }
        }
        finally
        {
            requestSlots.Release();
        }
    }

    private void RemoveCompleted(HashSet<Task> inFlight)
    {
        Task[] completed = inFlight
            .Where(static task => task.IsCompleted)
            .ToArray();

        foreach (Task task in completed)
        {
            _ = inFlight.Remove(task);
            ObserveDispatchTask(task);
        }
    }

    private void ScheduleDetachedDrain(
        HashSet<Task> inFlight,
        SemaphoreSlim responseWriteGate,
        SemaphoreSlim requestSlots)
    {
        Task drain = Task.WhenAll(inFlight);

        _ = drain.ContinueWith(
            completed =>
            {
                ObserveDispatchTask(completed);
                responseWriteGate.Dispose();
                requestSlots.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ObserveDispatchTask(Task task)
    {
        if (task.IsFaulted
            && task.Exception is AggregateException aggregate)
        {
            LogRequestDispatchFailed(
                logger,
                aggregate.GetBaseException());
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

    [LoggerMessage(
        EventId = 1808,
        Level = LogLevel.Debug,
        Message = "One or more Broker responses could not be written after transport disconnect.")]
    private static partial void LogPendingResponseDrainFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1809,
        Level = LogLevel.Warning,
        Message = "A Broker request dispatch or response write failed.")]
    private static partial void LogRequestDispatchFailed(
        ILogger logger,
        Exception exception);
}
