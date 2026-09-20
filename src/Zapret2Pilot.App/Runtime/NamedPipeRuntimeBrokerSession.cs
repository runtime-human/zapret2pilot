using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.App.Runtime;

public sealed class NamedPipeRuntimeBrokerSessionFactory :
    IRuntimeBrokerSessionFactory
{
    public IConnectableRuntimeBrokerSession Create(
        BrokerClientBinding clientBinding,
        string pipeName,
        byte[] bootstrapSecret)
        => new NamedPipeRuntimeBrokerSession(
            clientBinding,
            pipeName,
            bootstrapSecret);
}

/// <summary>
/// Authenticated Control-Plane session over the v1 local Broker Named Pipe.
///
/// Requests are multiplexed over one authenticated full-duplex connection:
/// writes are serialized in request-sequence order, while a dedicated reader
/// routes out-of-order responses back to callers by OperationId. This lets a
/// Stop request reach the Broker while an earlier Start is still waiting for
/// its terminal RuntimeKernel receipt without creating a second lifecycle
/// authority in the Control Plane.
/// </summary>
public sealed class NamedPipeRuntimeBrokerSession :
    IConnectableRuntimeBrokerSession
{
    private readonly BrokerClientBinding clientBinding;
    private readonly string pipeName;
    private readonly byte[] bootstrapSecret;
    private readonly TimeProvider timeProvider;
    private readonly BehaviorSubject<BrokerRuntimeSnapshot> snapshots;
    private readonly object stateSync = new();
    private readonly object snapshotSync = new();
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly SemaphoreSlim requestSlots = new(
        BrokerProtocolLimits.IngressQueueCapacity,
        BrokerProtocolLimits.IngressQueueCapacity);
    private readonly ConcurrentDictionary<
        BrokerOperationId,
        TaskCompletionSource<BrokerResponseEnvelope>> pendingResponses = new();

    private NamedPipeClientStream? pipe;
    private BrokerSessionId? brokerSessionId;
    private CancellationTokenSource? connectionCts;
    private Task? responseReaderTask;
    private long nextSequence;
    private int disposed;

    public NamedPipeRuntimeBrokerSession(
        BrokerClientBinding clientBinding,
        string pipeName,
        ReadOnlySpan<byte> bootstrapSecret,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(clientBinding);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (bootstrapSecret.Length != BrokerAuthenticator.SecretSizeBytes)
        {
            throw new ArgumentException(
                "Bootstrap secret must contain exactly 256 bits.",
                nameof(bootstrapSecret));
        }

        this.clientBinding = clientBinding;
        this.pipeName = pipeName;
        this.bootstrapSecret = bootstrapSecret.ToArray();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        snapshots = new BehaviorSubject<BrokerRuntimeSnapshot>(
            new BrokerRuntimeSnapshot(
                new RuntimeGeneration(0),
                BrokerRuntimeState.Stopped,
                ActivePlanId: null,
                ActiveOperationId: null));
    }

    public BrokerRuntimeSnapshot CurrentSnapshot
    {
        get
        {
            lock (snapshotSync)
            {
                return snapshots.Value;
            }
        }
    }

    public IObservable<BrokerRuntimeSnapshot> SnapshotChanged
        => snapshots.AsObservable();

    public bool IsConnected
    {
        get
        {
            lock (stateSync)
            {
                return pipe is { IsConnected: true }
                    && brokerSessionId is not null;
            }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await connectionGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                return;
            }

            await DisconnectCoreAsync().ConfigureAwait(false);

            NamedPipeClientStream candidate = new(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await candidate.ConnectAsync(
                    checked((int)BrokerProtocolLimits.HandshakeTimeout.TotalMilliseconds),
                    cancellationToken).ConfigureAwait(false);

                byte[]? challengeFrame = await ReadFrameBytesAsync(
                    candidate,
                    BrokerProtocolLimits.MaxChallengeFrameBytes,
                    cancellationToken).ConfigureAwait(false);
                if (challengeFrame is null)
                {
                    throw new EndOfStreamException(
                        "Broker disconnected before sending a challenge.");
                }

                BrokerChallengeFrameDecodeResult challengeDecode =
                    BrokerChallengeFrameCodec.Decode(challengeFrame);
                if (challengeDecode.Status != BrokerChallengeFrameDecodeStatus.Success
                    || challengeDecode.Challenge is null)
                {
                    throw new InvalidDataException(
                        $"Broker challenge was rejected: {challengeDecode.Status}.");
                }

                BrokerChallengeMessage challenge = challengeDecode.Challenge;
                BrokerProtocolNegotiationResult negotiation =
                    BrokerProtocolNegotiator.Negotiate(
                        BrokerProtocolRange.Current,
                        challenge.SupportedProtocols);
                if (!negotiation.Accepted
                    || negotiation.SelectedVersion is null)
                {
                    throw new InvalidDataException(
                        "Broker and Control Plane have no common protocol version.");
                }

                byte[] clientNonce = RandomNumberGenerator.GetBytes(
                    BrokerAuthenticator.NonceSizeBytes);
                byte[] proof = [];
                try
                {
                    BrokerPeerIdentity self = new(
                        clientBinding.ProcessId,
                        clientBinding.ProcessCreationTimeFileTime,
                        clientBinding.WindowsSessionId,
                        clientBinding.UserSid,
                        clientBinding.LogonSessionId,
                        clientBinding.IntegrityLevelRid);

                    BrokerAuthenticationTranscript transcript =
                        BrokerAuthenticationTranscript.Create(
                            negotiation.SelectedVersion.Value,
                            BrokerProtocolRange.Current,
                            challenge,
                            clientBinding.AppSessionId,
                            self,
                            clientNonce);
                    proof = BrokerAuthenticator.CreateProof(
                        bootstrapSecret,
                        transcript);

                    long sequence = NextSequence();
                    DateTimeOffset now = timeProvider.GetUtcNow();
                    BrokerRequestEnvelope hello = new(
                        negotiation.SelectedVersion.Value,
                        clientBinding.AppSessionId,
                        challenge.BrokerSessionId,
                        BrokerOperationId.New(),
                        new RequestSequence(sequence),
                        now,
                        now + BrokerProtocolLimits.MaxRequestLifetime,
                        new BrokerHelloRequest(
                            BrokerProtocolRange.Current,
                            clientBinding.AppSessionId,
                            clientNonce,
                            proof));

                    byte[] helloPayload =
                        BrokerProtocolCodec.EncodeRequest(hello);
                    await WritePayloadAsync(
                        candidate,
                        helloPayload,
                        BrokerProtocolLimits.MaxPreAuthHelloFrameBytes,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(clientNonce);
                    if (proof.Length != 0)
                    {
                        CryptographicOperations.ZeroMemory(proof);
                    }
                }

                CancellationTokenSource readerCts = new();
                lock (stateSync)
                {
                    pipe = candidate;
                    brokerSessionId = challenge.BrokerSessionId;
                    connectionCts = readerCts;
                    responseReaderTask = ReadResponsesAsync(
                        candidate,
                        challenge.BrokerSessionId,
                        readerCts.Token);
                }

                candidate = null!;
            }
            finally
            {
                if (candidate is not null)
                {
                    await candidate.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public Task<BrokerRuntimeSnapshot> GetRuntimeSnapshotAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetRuntimeSnapshotCoreAsync(cancellationToken);
    }

    public async Task<BrokerRuntimeSnapshot> StartPreparedPlanAsync(
        PreparedPlanId preparedPlanId,
        RuntimeGeneration expectedGeneration,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        BrokerResponseEnvelope response = await SendRequestCoreAsync(
            new StartPreparedPlanRequest(
                preparedPlanId,
                expectedGeneration),
            cancellationToken).ConfigureAwait(false);
        EnsureStatus(response, BrokerResponseStatus.Accepted);

        return await GetRuntimeSnapshotCoreAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BrokerRuntimeSnapshot> StopGenerationAsync(
        RuntimeGeneration generation,
        BrokerStopReason reason,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        BrokerResponseEnvelope response = await SendRequestCoreAsync(
            new StopGenerationRequest(generation, reason),
            cancellationToken).ConfigureAwait(false);
        EnsureStatus(response, BrokerResponseStatus.Accepted);

        return await GetRuntimeSnapshotCoreAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ShutdownBrokerAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        BrokerResponseEnvelope response = await SendRequestCoreAsync(
            new ShutdownBrokerRequest(),
            cancellationToken).ConfigureAwait(false);
        EnsureStatus(response, BrokerResponseStatus.Accepted);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await connectionGate.WaitAsync(CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);

            // Wait until every request that acquired a bounded slot has
            // unwound after DisconnectCore failed its pending response. This
            // prevents a caller from racing requestSlots.Release() against
            // SemaphoreSlim.Dispose().
            for (int slot = 0;
                 slot < BrokerProtocolLimits.IngressQueueCapacity;
                 slot++)
            {
                await requestSlots.WaitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            lock (snapshotSync)
            {
                snapshots.OnCompleted();
                snapshots.Dispose();
            }

            CryptographicOperations.ZeroMemory(bootstrapSecret);
        }
        finally
        {
            connectionGate.Release();
            connectionGate.Dispose();
            writeGate.Dispose();
            requestSlots.Dispose();
        }
    }

    private async Task<BrokerRuntimeSnapshot> GetRuntimeSnapshotCoreAsync(
        CancellationToken cancellationToken)
    {
        BrokerResponseEnvelope response = await SendRequestCoreAsync(
            new GetRuntimeSnapshotRequest(),
            cancellationToken).ConfigureAwait(false);
        EnsureStatus(response, BrokerResponseStatus.Ok);

        BrokerRuntimeSnapshotResponse body =
            response.Response as BrokerRuntimeSnapshotResponse
            ?? throw new InvalidDataException(
                "Broker snapshot response body has an unexpected type.");

        lock (snapshotSync)
        {
            snapshots.OnNext(body.Snapshot);
        }

        return body.Snapshot;
    }

    private async Task<BrokerResponseEnvelope> SendRequestCoreAsync(
        IBrokerRequest request,
        CancellationToken cancellationToken)
    {
        await requestSlots.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        BrokerOperationId operationId = BrokerOperationId.New();
        TaskCompletionSource<BrokerResponseEnvelope> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool registered = false;

        try
        {
            await writeGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                NamedPipeClientStream connectedPipe;
                BrokerSessionId sessionId;
                lock (stateSync)
                {
                    connectedPipe = pipe is { IsConnected: true }
                        ? pipe
                        : throw new InvalidOperationException(
                            "Runtime Broker session is not connected.");
                    sessionId = brokerSessionId
                        ?? throw new InvalidOperationException(
                            "Runtime Broker session is not authenticated.");
                }

                long sequence = NextSequence();
                DateTimeOffset now = timeProvider.GetUtcNow();
                BrokerRequestEnvelope envelope = new(
                    BrokerProtocolVersion.V1,
                    clientBinding.AppSessionId,
                    sessionId,
                    operationId,
                    new RequestSequence(sequence),
                    now,
                    now + BrokerProtocolLimits.MaxRequestLifetime,
                    request);

                if (!pendingResponses.TryAdd(operationId, completion))
                {
                    throw new InvalidOperationException(
                        "A duplicate Broker operation id was generated.");
                }

                registered = true;

                byte[] payload = BrokerProtocolCodec.EncodeRequest(envelope);
                try
                {
                    await WritePayloadAsync(
                        connectedPipe,
                        payload,
                        BrokerProtocolLimits.MaxFrameBytes,
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    pendingResponses.TryRemove(operationId, out _);
                    registered = false;
                    InvalidateConnection(connectedPipe);
                    throw;
                }
            }
            finally
            {
                writeGate.Release();
            }

            try
            {
                return await completion.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                // Local caller cancellation only abandons its response wait.
                // It never becomes an implicit remote runtime cancellation.
                pendingResponses.TryRemove(operationId, out _);
                registered = false;
                throw;
            }
        }
        finally
        {
            if (registered)
            {
                pendingResponses.TryRemove(operationId, out _);
            }

            requestSlots.Release();
        }
    }

    private async Task ReadResponsesAsync(
        NamedPipeClientStream connectedPipe,
        BrokerSessionId sessionId,
        CancellationToken cancellationToken)
    {
        Exception? terminalError = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? responsePayload = await ReadPayloadAsync(
                    connectedPipe,
                    BrokerProtocolLimits.MaxFrameBytes,
                    cancellationToken).ConfigureAwait(false);
                if (responsePayload is null)
                {
                    terminalError = new EndOfStreamException(
                        "Runtime Broker disconnected while responses were pending.");
                    return;
                }

                BrokerResponseDecodeResult decode =
                    BrokerResponseProtocolCodec.DecodeResponse(responsePayload);
                if (decode.Status != BrokerProtocolDecodeStatus.Success
                    || decode.Envelope is null)
                {
                    terminalError = new InvalidDataException(
                        $"Broker response was rejected: {decode.Status}.");
                    return;
                }

                BrokerResponseEnvelope response = decode.Envelope;
                if (response.AppSessionId != clientBinding.AppSessionId
                    || response.BrokerSessionId != sessionId)
                {
                    terminalError = new InvalidDataException(
                        "Broker response session identity does not match the authenticated connection.");
                    return;
                }

                if (pendingResponses.TryRemove(
                        response.OperationId,
                        out TaskCompletionSource<BrokerResponseEnvelope>? waiter))
                {
                    waiter.TrySetResult(response);
                }
                // A missing waiter is expected when a local caller cancelled
                // after its request was written. The Broker operation may
                // still complete; its late response is safely discarded.
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Normal local disconnect/disposal.
        }
        catch (Exception ex) when (
            ex is IOException
                or InvalidDataException
                or ObjectDisposedException)
        {
            terminalError = ex;
        }
        finally
        {
            lock (stateSync)
            {
                if (ReferenceEquals(pipe, connectedPipe))
                {
                    brokerSessionId = null;
                }
            }

            if (terminalError is not null)
            {
                FailPendingResponses(terminalError);
            }
        }
    }

    private void FailPendingResponses(Exception error)
    {
        foreach (KeyValuePair<
                     BrokerOperationId,
                     TaskCompletionSource<BrokerResponseEnvelope>> entry
                 in pendingResponses)
        {
            if (pendingResponses.TryRemove(entry.Key, out var waiter))
            {
                waiter.TrySetException(error);
            }
        }
    }

    private void InvalidateConnection(
        NamedPipeClientStream connectedPipe)
    {
        lock (stateSync)
        {
            if (!ReferenceEquals(pipe, connectedPipe))
            {
                return;
            }

            brokerSessionId = null;
            connectionCts?.Cancel();
        }

        connectedPipe.Dispose();
    }

    private static void EnsureStatus(
        BrokerResponseEnvelope response,
        BrokerResponseStatus expected)
    {
        if (response.Status == expected)
        {
            return;
        }

        if (response.Response is BrokerErrorResponse error)
        {
            throw new InvalidOperationException(
                $"Broker rejected the request: {error.Code}: {error.Message}");
        }

        throw new InvalidOperationException(
            $"Broker returned {response.Status}; expected {expected}.");
    }

    private long NextSequence()
    {
        long sequence = Interlocked.Increment(ref nextSequence);
        if (sequence <= 0)
        {
            throw new InvalidOperationException(
                "Broker request sequence overflowed.");
        }

        return sequence;
    }

    private async Task DisconnectCoreAsync()
    {
        NamedPipeClientStream? oldPipe;
        CancellationTokenSource? oldCts;
        Task? oldReader;

        lock (stateSync)
        {
            brokerSessionId = null;
            oldPipe = pipe;
            oldCts = connectionCts;
            oldReader = responseReaderTask;
            pipe = null;
            connectionCts = null;
            responseReaderTask = null;
        }

        oldCts?.Cancel();

        if (oldPipe is not null)
        {
            await oldPipe.DisposeAsync().ConfigureAwait(false);
        }

        if (oldReader is not null)
        {
            try
            {
                await oldReader.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal disconnect.
            }
        }

        oldCts?.Dispose();

        FailPendingResponses(
            new EndOfStreamException(
                "Runtime Broker session was disconnected."));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
    }

    private static async Task<byte[]?> ReadFrameBytesAsync(
        Stream stream,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[BrokerProtocolLimits.LengthPrefixBytes];
        int headerRead = await ReadExactlyOrEofAsync(
            stream,
            header,
            BrokerProtocolLimits.FrameHeaderTimeout,
            cancellationToken).ConfigureAwait(false);
        if (headerRead == 0)
        {
            return null;
        }

        if (headerRead != header.Length)
        {
            throw new EndOfStreamException(
                "Broker disconnected during frame header.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maximumPayloadBytes)
        {
            throw new InvalidDataException(
                "Broker returned an invalid frame length.");
        }

        byte[] body = new byte[length];
        int bodyRead = await ReadExactlyOrEofAsync(
            stream,
            body,
            BrokerProtocolLimits.FrameBodyTimeout,
            cancellationToken).ConfigureAwait(false);
        if (bodyRead != body.Length)
        {
            throw new EndOfStreamException(
                "Broker disconnected during frame body.");
        }

        byte[] frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame, header.Length);
        return frame;
    }

    private static async Task<byte[]?> ReadPayloadAsync(
        Stream stream,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        byte[]? frame = await ReadFrameBytesAsync(
            stream,
            maximumPayloadBytes,
            cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        BrokerFrameDecodeResult decoded = BrokerFrameCodec.Decode(
            frame,
            maximumPayloadBytes);
        if (decoded.Status != BrokerFrameDecodeStatus.Success
            || decoded.Payload is null)
        {
            throw new InvalidDataException(
                $"Broker frame was rejected: {decoded.Status}.");
        }

        return decoded.Payload;
    }

    private static async Task<int> ReadExactlyOrEofAsync(
        Stream stream,
        Memory<byte> buffer,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer[total..],
                deadline.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return total;
            }

            total += read;
        }

        return total;
    }

    private static async Task WritePayloadAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        byte[] frame = BrokerFrameCodec.Encode(
            payload.Span,
            maximumPayloadBytes);

        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(BrokerProtocolLimits.ResponseWriteTimeout);

        await stream.WriteAsync(frame, deadline.Token).ConfigureAwait(false);
        await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
    }
}
