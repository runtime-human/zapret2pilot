using System;
using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerResponseCodecTests
{
    [Fact]
    public static void RuntimeSnapshotRoundTripsThroughStrictCodec()
    {
        BrokerResponseEnvelope response = NewEnvelope(
            BrokerResponseStatus.Ok,
            new BrokerRuntimeSnapshotResponse(
                new BrokerRuntimeSnapshot(
                    new RuntimeGeneration(7),
                    BrokerRuntimeState.Running,
                    PreparedPlanId.New(),
                    BrokerOperationId.New())));

        byte[] payload = BrokerResponseProtocolCodec.EncodeResponse(response);
        BrokerResponseDecodeResult decoded =
            BrokerResponseProtocolCodec.DecodeResponse(payload);

        Assert.Equal(BrokerProtocolDecodeStatus.Success, decoded.Status);
        Assert.NotNull(decoded.Envelope);
        Assert.Equal(response.Protocol, decoded.Envelope!.Protocol);
        Assert.Equal(response.AppSessionId, decoded.Envelope.AppSessionId);
        Assert.Equal(response.BrokerSessionId, decoded.Envelope.BrokerSessionId);
        Assert.Equal(response.OperationId, decoded.Envelope.OperationId);
        Assert.Equal(response.Status, decoded.Envelope.Status);

        BrokerRuntimeSnapshotResponse body =
            Assert.IsType<BrokerRuntimeSnapshotResponse>(
                decoded.Envelope.Response);
        Assert.Equal(7, body.Snapshot.Generation.Value);
        Assert.Equal(BrokerRuntimeState.Running, body.Snapshot.State);
    }

    [Fact]
    public static void UnknownMemberFailsClosed()
    {
        BrokerResponseEnvelope response = NewEnvelope(
            BrokerResponseStatus.Rejected,
            new BrokerErrorResponse("Rejected", "No."));

        string json = System.Text.Encoding.UTF8.GetString(
            BrokerResponseProtocolCodec.EncodeResponse(response));
        string hostile = json.Replace(
            "\"status\"",
            "\"unexpected\":true,\"status\"",
            StringComparison.Ordinal);

        BrokerResponseDecodeResult decoded =
            BrokerResponseProtocolCodec.DecodeResponse(
                System.Text.Encoding.UTF8.GetBytes(hostile));

        Assert.Equal(BrokerProtocolDecodeStatus.Malformed, decoded.Status);
        Assert.Null(decoded.Envelope);
    }

    [Fact]
    public static void MutationResponseMustMatchEnvelopeOperation()
    {
        BrokerOperationId envelopeOperation = BrokerOperationId.New();
        BrokerResponseEnvelope invalid = new(
            BrokerProtocolVersion.V1,
            AppSessionId.New(),
            BrokerSessionId.New(),
            envelopeOperation,
            BrokerResponseStatus.Accepted,
            new BrokerMutationAcceptedResponse(
                BrokerOperationId.New(),
                new RuntimeGeneration(1)));

        Assert.Throws<ArgumentException>(
            () => BrokerResponseProtocolCodec.EncodeResponse(invalid));
    }

    [Fact]
    public static void OversizedPayloadIsRejectedBeforeJsonDecode()
    {
        byte[] oversized = new byte[BrokerProtocolLimits.MaxFrameBytes + 1];

        BrokerResponseDecodeResult decoded =
            BrokerResponseProtocolCodec.DecodeResponse(oversized);

        Assert.Equal(BrokerProtocolDecodeStatus.Oversized, decoded.Status);
        Assert.Null(decoded.Envelope);
    }

    private static BrokerResponseEnvelope NewEnvelope(
        BrokerResponseStatus status,
        IBrokerResponse body)
        => new(
            BrokerProtocolVersion.V1,
            AppSessionId.New(),
            BrokerSessionId.New(),
            BrokerOperationId.New(),
            status,
            body);
}
