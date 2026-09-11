using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;

namespace Zapret2Pilot.Contracts.Transport;

public static class BrokerProtocolLimits
{
    public const int LengthPrefixBytes = 4;
    public const int MaxFrameBytes = 65_536;
    public const int MaxChallengeFrameBytes = 1_024;
    public const int MaxPreAuthHelloFrameBytes = 4_096;
    public const int MaxConcurrentConnections = 2;
    public const int MaxAuthenticatedConnections = 1;
    public const int MaxPreAuthChallenges = MaxConcurrentConnections;
    public const int MaxPreAuthChallengesPerSecond = 2;
    public const int PreAuthChallengeBurstCapacity = 4;
    public const int MaxInFlightQueries = 8;
    public const int MaxConcurrentMutations = 1;
    public const int IngressQueueCapacity = 32;
    public const int ResponseQueueCapacity = 16;
    public const int OperationLedgerCapacity = 256;
    public const int MaxRequestsPerSecond = 16;
    public const int RequestBurstCapacity = 32;

    public static TimeSpan OperationLedgerTtl { get; } = TimeSpan.FromMinutes(2);
    public static TimeSpan HandshakeTimeout { get; } = TimeSpan.FromSeconds(3);
    public static TimeSpan PreAuthChallengeRetryAfter { get; } = TimeSpan.FromMilliseconds(500);
    public static TimeSpan FrameHeaderTimeout { get; } = TimeSpan.FromSeconds(2);
    public static TimeSpan FrameBodyTimeout { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan QueryDeadline { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan MaxRequestLifetime { get; } = TimeSpan.FromSeconds(15);
    public static TimeSpan ResponseEnqueueTimeout { get; } = TimeSpan.FromSeconds(1);
    public static TimeSpan ResponseWriteTimeout { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan BrokerShutdownTimeout { get; } = TimeSpan.FromSeconds(5);
}

public enum BrokerFrameDecodeStatus
{
    Success,
    NeedMoreData,
    InvalidLength,
    Oversized,
}

public sealed record BrokerFrameDecodeResult(
    BrokerFrameDecodeStatus Status,
    byte[]? Payload,
    int ConsumedBytes);

public static class BrokerFrameCodec
{
    public static byte[] Encode(ReadOnlySpan<byte> payload)
        => Encode(payload, BrokerProtocolLimits.MaxFrameBytes);

    public static byte[] Encode(ReadOnlySpan<byte> payload, int maxPayloadBytes)
    {
        ValidateMaximum(maxPayloadBytes);
        if (payload.IsEmpty || payload.Length > maxPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        byte[] frame = new byte[BrokerProtocolLimits.LengthPrefixBytes + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(0, BrokerProtocolLimits.LengthPrefixBytes),
            payload.Length);
        payload.CopyTo(frame.AsSpan(BrokerProtocolLimits.LengthPrefixBytes));
        return frame;
    }

    public static BrokerFrameDecodeResult Decode(ReadOnlySpan<byte> input)
        => Decode(input, BrokerProtocolLimits.MaxFrameBytes);

    public static BrokerFrameDecodeResult Decode(ReadOnlySpan<byte> input, int maxPayloadBytes)
    {
        ValidateMaximum(maxPayloadBytes);
        if (input.Length < BrokerProtocolLimits.LengthPrefixBytes)
        {
            return new(BrokerFrameDecodeStatus.NeedMoreData, null, 0);
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
            input[..BrokerProtocolLimits.LengthPrefixBytes]);
        if (payloadLength <= 0)
        {
            return new(BrokerFrameDecodeStatus.InvalidLength, null, 0);
        }

        if (payloadLength > maxPayloadBytes)
        {
            return new(BrokerFrameDecodeStatus.Oversized, null, 0);
        }

        int frameLength = BrokerProtocolLimits.LengthPrefixBytes + payloadLength;
        if (input.Length < frameLength)
        {
            return new(BrokerFrameDecodeStatus.NeedMoreData, null, 0);
        }

        return new(
            BrokerFrameDecodeStatus.Success,
            input.Slice(BrokerProtocolLimits.LengthPrefixBytes, payloadLength).ToArray(),
            frameLength);
    }

    private static void ValidateMaximum(int maxPayloadBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPayloadBytes, BrokerProtocolLimits.MaxFrameBytes);
    }
}

public enum BrokerProtocolDecodeStatus
{
    Success,
    Malformed,
    InvalidSemantics,
    Oversized,
    UnknownMessage,
    UnsupportedProtocol,
}

public sealed record BrokerProtocolDecodeResult(
    BrokerProtocolDecodeStatus Status,
    BrokerRequestEnvelope? Envelope);

public static class BrokerProtocolCodec
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    public static byte[] EncodeRequest(BrokerRequestEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        BrokerRequestSemanticValidationResult semanticValidation = BrokerRequestSemanticValidator.Validate(envelope);
        if (!semanticValidation.Accepted)
        {
            throw new ArgumentException(
                $"Broker request violates semantic contract: {semanticValidation.RejectionReason}.",
                nameof(envelope));
        }

        (string kind, JsonElement body) = SerializeRequest(envelope.Request);
        BrokerWireEnvelope wire = new(
            envelope.Protocol,
            kind,
            envelope.AppSessionId.Value,
            envelope.BrokerSessionId.Value,
            envelope.OperationId.Value,
            envelope.Sequence.Value,
            envelope.IssuedAtUtc.ToUnixTimeMilliseconds(),
            envelope.DeadlineUtc.ToUnixTimeMilliseconds(),
            body);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(wire, StrictJson);
        if (payload.Length > BrokerProtocolLimits.MaxFrameBytes)
        {
            throw new InvalidOperationException("Encoded broker request exceeds the protocol frame limit.");
        }

        return payload;
    }

    public static BrokerProtocolDecodeResult DecodeRequest(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }

        if (payload.Length > BrokerProtocolLimits.MaxFrameBytes)
        {
            return new(BrokerProtocolDecodeStatus.Oversized, null);
        }

        try
        {
            BrokerWireEnvelope? wire = JsonSerializer.Deserialize<BrokerWireEnvelope>(payload, StrictJson);
            if (wire is null
                || string.IsNullOrWhiteSpace(wire.Kind)
                || wire.AppSessionId == Guid.Empty
                || wire.BrokerSessionId == Guid.Empty
                || wire.OperationId == Guid.Empty
                || wire.Sequence <= 0)
            {
                return new(BrokerProtocolDecodeStatus.Malformed, null);
            }

            if (wire.Protocol != BrokerProtocolVersion.V1)
            {
                return new(BrokerProtocolDecodeStatus.UnsupportedProtocol, null);
            }

            if (!TryDeserializeRequest(wire.Kind, wire.Body, out IBrokerRequest? request))
            {
                return new(BrokerProtocolDecodeStatus.UnknownMessage, null);
            }

            DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(wire.IssuedAtUnixMs);
            DateTimeOffset deadline = DateTimeOffset.FromUnixTimeMilliseconds(wire.DeadlineUnixMs);
            BrokerRequestEnvelope envelope = new(
                wire.Protocol,
                new AppSessionId(wire.AppSessionId),
                new BrokerSessionId(wire.BrokerSessionId),
                new BrokerOperationId(wire.OperationId),
                new RequestSequence(wire.Sequence),
                issuedAt,
                deadline,
                request!);

            BrokerRequestSemanticValidationResult semanticValidation = BrokerRequestSemanticValidator.Validate(envelope);
            if (!semanticValidation.Accepted)
            {
                return new(BrokerProtocolDecodeStatus.InvalidSemantics, null);
            }

            return new(BrokerProtocolDecodeStatus.Success, envelope);
        }
        catch (JsonException)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }
        catch (NotSupportedException)
        {
            return new(BrokerProtocolDecodeStatus.Malformed, null);
        }
    }

    private static (string Kind, JsonElement Body) SerializeRequest(IBrokerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            BrokerHelloRequest value => ("hello", JsonSerializer.SerializeToElement(value, StrictJson)),
            GetCapabilitiesRequest value => ("getCapabilities", JsonSerializer.SerializeToElement(value, StrictJson)),
            GetRuntimeSnapshotRequest value => ("getRuntimeSnapshot", JsonSerializer.SerializeToElement(value, StrictJson)),
            PrepareBundleRequest value => ("prepareBundle", JsonSerializer.SerializeToElement(value, StrictJson)),
            PreparePlanRequest value => ("preparePlan", JsonSerializer.SerializeToElement(value, StrictJson)),
            StartPreparedPlanRequest value => ("startPreparedPlan", JsonSerializer.SerializeToElement(value, StrictJson)),
            StopGenerationRequest value => ("stopGeneration", JsonSerializer.SerializeToElement(value, StrictJson)),
            ShutdownBrokerRequest value => ("shutdownBroker", JsonSerializer.SerializeToElement(value, StrictJson)),
            _ => throw new NotSupportedException($"Unsupported broker request type: {request.GetType().FullName}"),
        };
    }

    private static bool TryDeserializeRequest(
        string kind,
        JsonElement body,
        out IBrokerRequest? request)
    {
        request = kind switch
        {
            "hello" => body.Deserialize<BrokerHelloRequest>(StrictJson),
            "getCapabilities" => body.Deserialize<GetCapabilitiesRequest>(StrictJson),
            "getRuntimeSnapshot" => body.Deserialize<GetRuntimeSnapshotRequest>(StrictJson),
            "prepareBundle" => body.Deserialize<PrepareBundleRequest>(StrictJson),
            "preparePlan" => body.Deserialize<PreparePlanRequest>(StrictJson),
            "startPreparedPlan" => body.Deserialize<StartPreparedPlanRequest>(StrictJson),
            "stopGeneration" => body.Deserialize<StopGenerationRequest>(StrictJson),
            "shutdownBroker" => body.Deserialize<ShutdownBrokerRequest>(StrictJson),
            _ => null,
        };
        return request is not null;
    }

    private sealed record BrokerWireEnvelope(
        BrokerProtocolVersion Protocol,
        string Kind,
        Guid AppSessionId,
        Guid BrokerSessionId,
        Guid OperationId,
        long Sequence,
        long IssuedAtUnixMs,
        long DeadlineUnixMs,
        JsonElement Body);
}

public enum BrokerOperationRegistration
{
    New,
    DuplicateInFlight,
    DuplicateCompleted,
    Conflict,
    Stale,
    CapacityExceeded,
}

public sealed class BrokerOperationLedger
{
    private readonly object sync = new();
    private readonly Dictionary<BrokerOperationId, Entry> entries = [];
    private readonly int capacity;
    private readonly TimeSpan ttl;
    private readonly TimeProvider timeProvider;
    private long highWatermark;

    public BrokerOperationLedger(int capacity, TimeSpan ttl)
        : this(capacity, ttl, TimeProvider.System)
    {
    }

    public BrokerOperationLedger(int capacity, TimeSpan ttl, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.capacity = capacity;
        this.ttl = ttl;
        this.timeProvider = timeProvider;
    }

    public BrokerOperationRegistration Register(
        BrokerOperationId operationId,
        RequestSequence sequence,
        Sha256Digest fingerprint)
    {
        if (operationId.Value == Guid.Empty)
        {
            throw new ArgumentException("Operation ID must not be empty.", nameof(operationId));
        }

        if (sequence.Value <= 0)
        {
            throw new ArgumentException("Request sequence must be positive.", nameof(sequence));
        }

        if (!fingerprint.IsCanonical)
        {
            throw new ArgumentException("Request fingerprint must be a canonical SHA-256 digest.", nameof(fingerprint));
        }

        lock (sync)
        {
            long now = timeProvider.GetTimestamp();
            RemoveExpired(now);
            if (entries.TryGetValue(operationId, out Entry? existing))
            {
                if (existing.Sequence != sequence || existing.Fingerprint != fingerprint)
                {
                    return BrokerOperationRegistration.Conflict;
                }

                return existing.Completed
                    ? BrokerOperationRegistration.DuplicateCompleted
                    : BrokerOperationRegistration.DuplicateInFlight;
            }

            if (sequence.Value <= highWatermark)
            {
                return BrokerOperationRegistration.Stale;
            }

            if (entries.Count >= capacity)
            {
                return BrokerOperationRegistration.CapacityExceeded;
            }

            entries.Add(operationId, new Entry(sequence, fingerprint, now));
            highWatermark = sequence.Value;
            return BrokerOperationRegistration.New;
        }
    }

    public bool Complete(BrokerOperationId operationId)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(operationId, out Entry? entry))
            {
                return false;
            }

            entry.Completed = true;
            return true;
        }
    }

    private void RemoveExpired(long now)
    {
        BrokerOperationId[] expired = entries
            .Where(pair => timeProvider.GetElapsedTime(pair.Value.CreatedAtTimestamp, now) > ttl)
            .Select(static pair => pair.Key)
            .ToArray();
        foreach (BrokerOperationId operationId in expired)
        {
            entries.Remove(operationId);
        }
    }

    private sealed class Entry(
        RequestSequence sequence,
        Sha256Digest fingerprint,
        long createdAtTimestamp)
    {
        public RequestSequence Sequence { get; } = sequence;
        public Sha256Digest Fingerprint { get; } = fingerprint;
        public long CreatedAtTimestamp { get; } = createdAtTimestamp;
        public bool Completed { get; set; }
    }
}

public sealed class BrokerIngressBuffer
{
    private readonly object sync = new();
    private readonly Queue<BrokerRequestEnvelope> queue;
    private readonly int capacity;

    public BrokerIngressBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
        queue = new Queue<BrokerRequestEnvelope>(capacity);
    }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return queue.Count;
            }
        }
    }

    public bool TryEnqueue(BrokerRequestEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (sync)
        {
            if (queue.Count >= capacity)
            {
                return false;
            }

            queue.Enqueue(request);
            return true;
        }
    }

    public bool TryDequeue(out BrokerRequestEnvelope? request)
    {
        lock (sync)
        {
            if (queue.Count == 0)
            {
                request = null;
                return false;
            }

            request = queue.Dequeue();
            return true;
        }
    }
}

public sealed class BrokerResponseBuffer
{
    private readonly object sync = new();
    private readonly Queue<BrokerResponseEnvelope> queue;
    private readonly int capacity;

    public BrokerResponseBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
        queue = new Queue<BrokerResponseEnvelope>(capacity);
    }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return queue.Count;
            }
        }
    }

    public bool TryEnqueue(BrokerResponseEnvelope response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (sync)
        {
            if (queue.Count >= capacity)
            {
                return false;
            }

            queue.Enqueue(response);
            return true;
        }
    }

    public bool TryDequeue(out BrokerResponseEnvelope? response)
    {
        lock (sync)
        {
            if (queue.Count == 0)
            {
                response = null;
                return false;
            }

            response = queue.Dequeue();
            return true;
        }
    }
}

public sealed class BrokerConcurrencyGate
{
    private readonly object sync = new();
    private readonly int maxQueries;
    private readonly int maxMutations;
    private int activeQueries;
    private int activeMutations;

    public BrokerConcurrencyGate(int maxQueries, int maxMutations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxQueries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMutations);
        this.maxQueries = maxQueries;
        this.maxMutations = maxMutations;
    }

    public BrokerConcurrencyLease? TryAcquireQuery()
    {
        lock (sync)
        {
            if (activeQueries >= maxQueries)
            {
                return null;
            }

            activeQueries++;
            return new BrokerConcurrencyLease(ReleaseQuery);
        }
    }

    public BrokerConcurrencyLease? TryAcquireMutation()
    {
        lock (sync)
        {
            if (activeMutations >= maxMutations)
            {
                return null;
            }

            activeMutations++;
            return new BrokerConcurrencyLease(ReleaseMutation);
        }
    }

    private void ReleaseQuery()
    {
        lock (sync)
        {
            activeQueries--;
        }
    }

    private void ReleaseMutation()
    {
        lock (sync)
        {
            activeMutations--;
        }
    }
}

public sealed class BrokerConcurrencyLease : IDisposable
{
    private Action? release;

    internal BrokerConcurrencyLease(Action release)
    {
        this.release = release;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
