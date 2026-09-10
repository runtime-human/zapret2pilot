using System.Buffers.Binary;
using System.Text;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerCodecAndBoundsTests
{
    [Fact]
    public void Oversized_frame_is_rejected_before_payload_allocation()
    {
        byte[] prefix = new byte[BrokerProtocolLimits.LengthPrefixBytes];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, BrokerProtocolLimits.MaxFrameBytes + 1);

        BrokerFrameDecodeResult result = BrokerFrameCodec.Decode(prefix);

        Assert.Equal(BrokerFrameDecodeStatus.Oversized, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Fragmented_frame_requests_more_data_without_consuming_input()
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
    public void Malformed_json_is_rejected()
    {
        byte[] payload = "{ definitely-not-json"u8.ToArray();

        BrokerProtocolDecodeResult result = BrokerProtocolCodec.DecodeRequest(payload);

        Assert.Equal(BrokerProtocolDecodeStatus.Malformed, result.Status);
        Assert.Null(result.Envelope);
    }

    [Fact]
    public void Unknown_message_kind_is_rejected()
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
    public void Unknown_fields_are_rejected_instead_of_silently_ignored()
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
    public void Operation_id_duplicate_and_stale_semantics_are_deterministic()
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
    public void Ingress_queue_rejects_flood_when_capacity_is_reached()
    {
        BrokerIngressQueue queue = new(BrokerProtocolLimits.IngressQueueCapacity);
        BrokerRequestEnvelope request = CreateSnapshotRequest();

        for (int i = 0; i < BrokerProtocolLimits.IngressQueueCapacity; i++)
        {
            Assert.True(queue.TryEnqueue(request));
        }

        Assert.False(queue.TryEnqueue(request));
        Assert.Equal(BrokerProtocolLimits.IngressQueueCapacity, queue.Count);
    }

    [Fact]
    public void Mutation_concurrency_is_one_and_queries_are_bounded()
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
    public void Stale_runtime_generation_is_rejected_before_mutation_dispatch()
    {
        RuntimeGeneration expected = new(12);
        RuntimeGeneration current = new(13);

        BrokerGenerationDecision decision = BrokerGenerationGuard.Validate(expected, current);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerGenerationRejectionReason.StaleGeneration, decision.RejectionReason);
    }

    [Fact]
    public void Request_deadline_is_fail_closed()
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
}
