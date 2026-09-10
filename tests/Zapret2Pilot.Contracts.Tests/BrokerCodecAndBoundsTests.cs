using System.Buffers.Binary;
using System.Text;
using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerCodecAndBoundsTests
{
    [Fact]
    public void OversizedFrameIsRejectedBeforePayloadAllocation()
    {
        byte[] prefix = new byte[BrokerProtocolLimits.LengthPrefixBytes];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, BrokerProtocolLimits.MaxFrameBytes + 1);

        BrokerFrameDecodeResult result = BrokerFrameCodec.Decode(prefix);

        Assert.Equal(BrokerFrameDecodeStatus.Oversized, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void FragmentedFrameRequestsMoreDataWithoutConsumingInput()
    {
        byte[] partial = new byte[BrokerProtocolLimits.LengthPrefixBytes + 2];
        BinaryPrimitives.WriteInt32LittleEndian(partial, 10);
        partial[4] = (byte)'{';
        partial[5] = (byte)'}';

        BrokerFrameDecodeResult result = BrokerFrameCodec.Decode(partial);

        Assert.Equal(BrokerFrameDecodeStatus.NeedMoreData, result.Status);
        Assert.Equal(0, result.ConsumedBytes);
    }

    [Fact]
    public void MalformedJsonIsRejected()
    {
        byte[] payload = "{ definitely-not-json"u8.ToArray();

        BrokerProtocolDecodeResult result = BrokerProtocolCodec.DecodeRequest(payload);

        Assert.Equal(BrokerProtocolDecodeStatus.Malformed, result.Status);
        Assert.Null(result.Envelope);
    }

    [Fact]
    public void UnknownMessageKindIsRejected()
    {
        string json = $$"""
            {
              "protocol":{"major":1,"minor":0},
              "kind":"executeCommand",
              "appSessionId":"{{Guid.NewGuid()}}",
              "brokerSessionId":"{{Guid.NewGuid()}}",
              "operationId":"{{Guid.NewGuid()}}",
              "sequence":1,
              "issuedAtUnixMs":1789027200000,
              "deadlineUnixMs":1789027215000,
              "body":{}
            }
            """;

        BrokerProtocolDecodeResult result = BrokerProtocolCodec.DecodeRequest(Encoding.UTF8.GetBytes(json));

        Assert.Equal(BrokerProtocolDecodeStatus.UnknownMessage, result.Status);
    }

    [Fact]
    public void UnknownFieldsAreRejectedInsteadOfSilentlyIgnored()
    {
        string json = $$"""
            {
              "protocol":{"major":1,"minor":0},
              "kind":"getRuntimeSnapshot",
              "appSessionId":"{{Guid.NewGuid()}}",
              "brokerSessionId":"{{Guid.NewGuid()}}",
              "operationId":"{{Guid.NewGuid()}}",
              "sequence":1,
              "issuedAtUnixMs":1789027200000,
              "deadlineUnixMs":1789027215000,
              "unexpectedAuthority":"cmd.exe",
              "body":{}
            }
            """;

        BrokerProtocolDecodeResult result = BrokerProtocolCodec.DecodeRequest(Encoding.UTF8.GetBytes(json));

        Assert.Equal(BrokerProtocolDecodeStatus.Malformed, result.Status);
    }

    [Fact]
    public void OperationIdDuplicateAndStaleSemanticsAreDeterministic()
    {
        BrokerOperationLedger ledger = new(BrokerProtocolLimits.OperationLedgerCapacity, BrokerProtocolLimits.OperationLedgerTtl);
        BrokerOperationId operationId = BrokerOperationId.New();
        Sha256Digest fingerprint = Sha256Digest.Compute("start-plan-A"u8);
        DateTimeOffset now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

        BrokerOperationRegistration first = ledger.Register(operationId, new RequestSequence(10), fingerprint, now);
        BrokerOperationRegistration duplicateInFlight = ledger.Register(operationId, new RequestSequence(10), fingerprint, now);
        ledger.Complete(operationId);
        BrokerOperationRegistration duplicateCompleted = ledger.Register(operationId, new RequestSequence(10), fingerprint, now);
        BrokerOperationRegistration staleUnknown = ledger.Register(
            BrokerOperationId.New(),
            new RequestSequence(9),
            Sha256Digest.Compute("other"u8),
            now);
        BrokerOperationRegistration conflict = ledger.Register(
            operationId,
            new RequestSequence(11),
            Sha256Digest.Compute("different-request"u8),
            now);

        Assert.Equal(BrokerOperationRegistration.New, first);
        Assert.Equal(BrokerOperationRegistration.DuplicateInFlight, duplicateInFlight);
        Assert.Equal(BrokerOperationRegistration.DuplicateCompleted, duplicateCompleted);
        Assert.Equal(BrokerOperationRegistration.Stale, staleUnknown);
        Assert.Equal(BrokerOperationRegistration.Conflict, conflict);
    }

    [Fact]
    public void IngressBufferRejectsFloodWhenCapacityIsReached()
    {
        BrokerIngressBuffer buffer = new(BrokerProtocolLimits.IngressQueueCapacity);
        BrokerRequestEnvelope request = CreateSnapshotRequest();

        for (int i = 0; i < BrokerProtocolLimits.IngressQueueCapacity; i++)
        {
            Assert.True(buffer.TryEnqueue(request));
        }

        Assert.False(buffer.TryEnqueue(request));
        Assert.Equal(BrokerProtocolLimits.IngressQueueCapacity, buffer.Count);
    }

    [Fact]
    public void ResponseBufferAppliesBackpressureWhenCapacityIsReached()
    {
        BrokerResponseBuffer buffer = new(BrokerProtocolLimits.ResponseQueueCapacity);
        BrokerResponseEnvelope response = CreateSnapshotResponse();

        for (int i = 0; i < BrokerProtocolLimits.ResponseQueueCapacity; i++)
        {
            Assert.True(buffer.TryEnqueue(response));
        }

        Assert.False(buffer.TryEnqueue(response));
        Assert.Equal(BrokerProtocolLimits.ResponseQueueCapacity, buffer.Count);
    }

    [Fact]
    public void MutationConcurrencyIsOneAndQueriesAreBounded()
    {
        BrokerConcurrencyGate gate = new(
            BrokerProtocolLimits.MaxInFlightQueries,
            BrokerProtocolLimits.MaxConcurrentMutations);

        using BrokerConcurrencyLease mutation = Assert.IsType<BrokerConcurrencyLease>(gate.TryAcquireMutation());
        Assert.Null(gate.TryAcquireMutation());

        List<BrokerConcurrencyLease> queryLeases = [];
        try
        {
            for (int i = 0; i < BrokerProtocolLimits.MaxInFlightQueries; i++)
            {
                queryLeases.Add(Assert.IsType<BrokerConcurrencyLease>(gate.TryAcquireQuery()));
            }

            Assert.Null(gate.TryAcquireQuery());
        }
        finally
        {
            foreach (BrokerConcurrencyLease lease in queryLeases)
            {
                lease.Dispose();
            }
        }
    }

    [Fact]
    public void StaleRuntimeGenerationIsRejectedBeforeMutationDispatch()
    {
        RuntimeGeneration expected = new(12);
        RuntimeGeneration current = new(13);

        BrokerGenerationDecision decision = BrokerGenerationGuard.Validate(expected, current);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerGenerationRejectionReason.StaleGeneration, decision.RejectionReason);
    }

    [Fact]
    public void RequestDeadlineIsFailClosed()
    {
        DateTimeOffset now = new(2026, 9, 10, 8, 0, 20, TimeSpan.Zero);
        BrokerRequestEnvelope request = CreateSnapshotRequest() with
        {
            IssuedAtUtc = now - TimeSpan.FromSeconds(20),
            DeadlineUtc = now - TimeSpan.FromSeconds(5),
        };

        BrokerRequestFreshnessDecision decision = BrokerRequestFreshnessGuard.Validate(request, now);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerRequestFreshnessRejectionReason.Expired, decision.RejectionReason);
    }

    private static BrokerRequestEnvelope CreateSnapshotRequest()
    {
        DateTimeOffset issuedAt = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        return new BrokerRequestEnvelope(
            BrokerProtocolVersion.V1,
            AppSessionId.New(),
            BrokerSessionId.New(),
            BrokerOperationId.New(),
            new RequestSequence(1),
            issuedAt,
            issuedAt + BrokerProtocolLimits.MaxRequestLifetime,
            new GetRuntimeSnapshotRequest());
    }

    private static BrokerResponseEnvelope CreateSnapshotResponse()
    {
        AppSessionId appSessionId = AppSessionId.New();
        BrokerSessionId brokerSessionId = BrokerSessionId.New();
        BrokerOperationId operationId = BrokerOperationId.New();
        return new BrokerResponseEnvelope(
            BrokerProtocolVersion.V1,
            appSessionId,
            brokerSessionId,
            operationId,
            BrokerResponseStatus.Ok,
            new BrokerRuntimeSnapshotResponse(
                new BrokerRuntimeSnapshot(new RuntimeGeneration(1), BrokerRuntimeState.Stopped, null, null)));
    }
}
