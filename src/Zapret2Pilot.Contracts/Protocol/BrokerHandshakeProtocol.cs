using Zapret2Pilot.Contracts.Identity;

namespace Zapret2Pilot.Contracts.Protocol;

public sealed record BrokerChallengeMessage(
    BrokerProtocolVersion HandshakeProtocol,
    BrokerProtocolRange SupportedProtocols,
    BrokerSessionId BrokerSessionId,
    ReadOnlyMemory<byte> ServerNonce,
    int LifetimeMilliseconds);
