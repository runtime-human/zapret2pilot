using System.Text.Json;
using System.Text.Json.Serialization;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;

namespace Zapret2Pilot.Contracts.Transport;

public enum BrokerChallengeFrameDecodeStatus
{
    Success,
    NeedMoreData,
    InvalidLength,
    Oversized,
    Malformed,
    UnsupportedProtocol,
    InvalidSemantics,
}

public sealed record BrokerChallengeFrameDecodeResult(
    BrokerChallengeFrameDecodeStatus Status,
    BrokerChallengeMessage? Challenge,
    int ConsumedBytes);

public static class BrokerChallengeFrameCodec
{
    private const string ChallengeKind = "brokerChallenge";

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    public static byte[] Encode(BrokerChallengeMessage challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        BrokerChallengeFrameDecodeStatus status = Validate(challenge);
        if (status != BrokerChallengeFrameDecodeStatus.Success)
        {
            throw new ArgumentException($"Broker challenge violates wire contract: {status}.", nameof(challenge));
        }

        BrokerChallengeWireEnvelope wire = new(
            challenge.HandshakeProtocol,
            ChallengeKind,
            challenge.SupportedProtocols,
            challenge.BrokerSessionId.Value,
            challenge.ServerNonce.ToArray(),
            challenge.LifetimeMilliseconds);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(wire, StrictJson);
        return BrokerFrameCodec.Encode(payload, BrokerProtocolLimits.MaxChallengeFrameBytes);
    }

    public static BrokerChallengeFrameDecodeResult Decode(ReadOnlySpan<byte> input)
    {
        BrokerFrameDecodeResult frame = BrokerFrameCodec.Decode(input, BrokerProtocolLimits.MaxChallengeFrameBytes);
        if (frame.Status != BrokerFrameDecodeStatus.Success)
        {
            return new(MapFrameStatus(frame.Status), null, frame.ConsumedBytes);
        }

        try
        {
            BrokerChallengeWireEnvelope? wire = JsonSerializer.Deserialize<BrokerChallengeWireEnvelope>(
                frame.Payload,
                StrictJson);
            if (wire is null || !string.Equals(wire.Kind, ChallengeKind, StringComparison.Ordinal))
            {
                return new(BrokerChallengeFrameDecodeStatus.Malformed, null, frame.ConsumedBytes);
            }

            BrokerChallengeMessage challenge = new(
                wire.HandshakeProtocol,
                wire.SupportedProtocols,
                new BrokerSessionId(wire.BrokerSessionId),
                wire.ServerNonce,
                wire.LifetimeMilliseconds);
            BrokerChallengeFrameDecodeStatus semanticStatus = Validate(challenge);
            return semanticStatus == BrokerChallengeFrameDecodeStatus.Success
                ? new(semanticStatus, challenge, frame.ConsumedBytes)
                : new(semanticStatus, null, frame.ConsumedBytes);
        }
        catch (JsonException)
        {
            return new(BrokerChallengeFrameDecodeStatus.Malformed, null, frame.ConsumedBytes);
        }
        catch (NotSupportedException)
        {
            return new(BrokerChallengeFrameDecodeStatus.Malformed, null, frame.ConsumedBytes);
        }
    }

    private static BrokerChallengeFrameDecodeStatus Validate(BrokerChallengeMessage challenge)
    {
        if (challenge.HandshakeProtocol != BrokerProtocolVersion.V1)
        {
            return BrokerChallengeFrameDecodeStatus.UnsupportedProtocol;
        }

        BrokerProtocolNegotiationResult negotiation = BrokerProtocolNegotiator.Negotiate(
            challenge.SupportedProtocols,
            BrokerProtocolRange.Current);
        if (!negotiation.Accepted)
        {
            return negotiation.RejectionReason == BrokerProtocolRejectionReason.UnsupportedMajorVersion
                ? BrokerChallengeFrameDecodeStatus.UnsupportedProtocol
                : BrokerChallengeFrameDecodeStatus.InvalidSemantics;
        }

        if (challenge.BrokerSessionId.Value == Guid.Empty
            || challenge.ServerNonce.Length != BrokerAuthenticator.NonceSizeBytes
            || challenge.LifetimeMilliseconds <= 0
            || challenge.LifetimeMilliseconds > BrokerProtocolLimits.HandshakeTimeout.TotalMilliseconds)
        {
            return BrokerChallengeFrameDecodeStatus.InvalidSemantics;
        }

        return BrokerChallengeFrameDecodeStatus.Success;
    }

    private static BrokerChallengeFrameDecodeStatus MapFrameStatus(BrokerFrameDecodeStatus status)
        => status switch
        {
            BrokerFrameDecodeStatus.NeedMoreData => BrokerChallengeFrameDecodeStatus.NeedMoreData,
            BrokerFrameDecodeStatus.InvalidLength => BrokerChallengeFrameDecodeStatus.InvalidLength,
            BrokerFrameDecodeStatus.Oversized => BrokerChallengeFrameDecodeStatus.Oversized,
            _ => BrokerChallengeFrameDecodeStatus.Malformed,
        };

    private sealed record BrokerChallengeWireEnvelope(
        BrokerProtocolVersion HandshakeProtocol,
        string Kind,
        BrokerProtocolRange SupportedProtocols,
        Guid BrokerSessionId,
        byte[] ServerNonce,
        int LifetimeMilliseconds);
}
