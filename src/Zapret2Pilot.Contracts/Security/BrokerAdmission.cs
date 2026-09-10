using System.Security.Cryptography;
using System.Text;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Security;

public sealed record BrokerClientBinding(
    AppSessionId AppSessionId,
    int ProcessId,
    long ProcessCreationTimeFileTime,
    uint WindowsSessionId,
    string UserSid,
    LogonSessionId LogonSessionId,
    int IntegrityLevelRid);

public sealed record BrokerPeerIdentity(
    int ProcessId,
    long ProcessCreationTimeFileTime,
    uint WindowsSessionId,
    string UserSid,
    LogonSessionId LogonSessionId,
    int IntegrityLevelRid);

public sealed record BrokerHandshakeChallenge(
    BrokerSessionId BrokerSessionId,
    ReadOnlyMemory<byte> Nonce,
    DateTimeOffset ExpiresAtUtc);

public sealed record BrokerAuthenticationTranscript(
    BrokerProtocolVersion Protocol,
    AppSessionId AppSessionId,
    BrokerSessionId BrokerSessionId,
    int ProcessId,
    long ProcessCreationTimeFileTime,
    uint WindowsSessionId,
    ReadOnlyMemory<byte> ChallengeNonce,
    ReadOnlyMemory<byte> ClientNonce)
{
    internal byte[] ToBytes()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Protocol.Major);
            writer.Write(Protocol.Minor);
            writer.Write(AppSessionId.Value.ToByteArray());
            writer.Write(BrokerSessionId.Value.ToByteArray());
            writer.Write(ProcessId);
            writer.Write(ProcessCreationTimeFileTime);
            writer.Write(WindowsSessionId);
            WriteBytes(writer, ChallengeNonce.Span);
            WriteBytes(writer, ClientNonce.Span);
        }

        return stream.ToArray();
    }

    private static void WriteBytes(BinaryWriter writer, ReadOnlySpan<byte> value)
    {
        writer.Write(value.Length);
        writer.Write(value);
    }
}

public static class BrokerAuthenticator
{
    public const int SecretSizeBytes = 32;
    public const int NonceSizeBytes = 32;
    public const int ProofSizeBytes = 32;

    public static byte[] CreateProof(
        ReadOnlySpan<byte> bootstrapSecret,
        BrokerAuthenticationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (bootstrapSecret.Length != SecretSizeBytes)
        {
            throw new ArgumentException("Bootstrap secret must contain exactly 256 bits.", nameof(bootstrapSecret));
        }

        using HMACSHA256 hmac = new(bootstrapSecret.ToArray());
        return hmac.ComputeHash(transcript.ToBytes());
    }

    public static bool VerifyProof(
        ReadOnlySpan<byte> bootstrapSecret,
        BrokerAuthenticationTranscript transcript,
        ReadOnlySpan<byte> proof)
    {
        if (proof.Length != ProofSizeBytes)
        {
            return false;
        }

        byte[] expected = CreateProof(bootstrapSecret, transcript);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, proof);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }
}

public enum BrokerAdmissionRejectionReason
{
    None,
    ReplayedChallenge,
    ExpiredChallenge,
    UnsupportedProtocol,
    StaleApplicationSession,
    WrongProcessId,
    ProcessCreationTimeMismatch,
    WrongWindowsSession,
    WrongUserSid,
    WrongLogonSession,
    WrongIntegrityLevel,
    InvalidNonce,
    InvalidBootstrapProof,
}

public sealed record BrokerAdmissionDecision(
    bool Accepted,
    BrokerAdmissionRejectionReason RejectionReason,
    AppSessionId? AppSessionId,
    BrokerSessionId? BrokerSessionId,
    BrokerProtocolVersion? Protocol);

public sealed class BrokerAdmissionGate
{
    private readonly BrokerClientBinding expected;
    private readonly byte[] bootstrapSecret;
    private int challengeConsumed;

    public BrokerAdmissionGate(
        BrokerClientBinding expected,
        ReadOnlySpan<byte> bootstrapSecret,
        BrokerHandshakeChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(challenge);
        if (bootstrapSecret.Length != BrokerAuthenticator.SecretSizeBytes)
        {
            throw new ArgumentException("Bootstrap secret must contain exactly 256 bits.", nameof(bootstrapSecret));
        }

        if (challenge.Nonce.Length != BrokerAuthenticator.NonceSizeBytes)
        {
            throw new ArgumentException("Broker challenge nonce must contain exactly 256 bits.", nameof(challenge));
        }

        this.expected = expected;
        this.bootstrapSecret = bootstrapSecret.ToArray();
        Challenge = challenge;
    }

    public BrokerHandshakeChallenge Challenge { get; }

    public BrokerAdmissionDecision TryAdmit(
        BrokerPeerIdentity peer,
        BrokerHelloRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(request);

        // A challenge represents exactly one authentication attempt. Burn it
        // before evaluating attacker-controlled fields so concurrent/retried
        // invalid proofs cannot turn one nonce into a reusable verification oracle.
        if (Interlocked.CompareExchange(ref challengeConsumed, 1, 0) != 0)
        {
            return Reject(BrokerAdmissionRejectionReason.ReplayedChallenge);
        }

        if (now > Challenge.ExpiresAtUtc)
        {
            return Reject(BrokerAdmissionRejectionReason.ExpiredChallenge);
        }

        BrokerProtocolNegotiationResult negotiation = BrokerProtocolNegotiator.Negotiate(
            request.SupportedProtocols,
            BrokerProtocolRange.Current);
        if (!negotiation.Accepted || negotiation.SelectedVersion is null)
        {
            return Reject(BrokerAdmissionRejectionReason.UnsupportedProtocol);
        }

        if (request.AppSessionId != expected.AppSessionId)
        {
            return Reject(BrokerAdmissionRejectionReason.StaleApplicationSession);
        }

        if (peer.ProcessId != expected.ProcessId)
        {
            return Reject(BrokerAdmissionRejectionReason.WrongProcessId);
        }

        if (peer.ProcessCreationTimeFileTime != expected.ProcessCreationTimeFileTime)
        {
            return Reject(BrokerAdmissionRejectionReason.ProcessCreationTimeMismatch);
        }

        if (peer.WindowsSessionId != expected.WindowsSessionId)
        {
            return Reject(BrokerAdmissionRejectionReason.WrongWindowsSession);
        }

        if (!string.Equals(peer.UserSid, expected.UserSid, StringComparison.Ordinal))
        {
            return Reject(BrokerAdmissionRejectionReason.WrongUserSid);
        }

        if (peer.LogonSessionId != expected.LogonSessionId)
        {
            return Reject(BrokerAdmissionRejectionReason.WrongLogonSession);
        }

        if (peer.IntegrityLevelRid != expected.IntegrityLevelRid)
        {
            return Reject(BrokerAdmissionRejectionReason.WrongIntegrityLevel);
        }

        if (request.ClientNonce.Length != BrokerAuthenticator.NonceSizeBytes)
        {
            return Reject(BrokerAdmissionRejectionReason.InvalidNonce);
        }

        BrokerAuthenticationTranscript transcript = new(
            negotiation.SelectedVersion.Value,
            expected.AppSessionId,
            Challenge.BrokerSessionId,
            peer.ProcessId,
            peer.ProcessCreationTimeFileTime,
            peer.WindowsSessionId,
            Challenge.Nonce,
            request.ClientNonce);
        if (!BrokerAuthenticator.VerifyProof(bootstrapSecret, transcript, request.Proof.Span))
        {
            return Reject(BrokerAdmissionRejectionReason.InvalidBootstrapProof);
        }

        return new(
            true,
            BrokerAdmissionRejectionReason.None,
            expected.AppSessionId,
            Challenge.BrokerSessionId,
            negotiation.SelectedVersion);
    }

    private static BrokerAdmissionDecision Reject(BrokerAdmissionRejectionReason reason)
        => new(false, reason, null, null, null);
}

public sealed record BrokerPipeSecurityPolicy(
    bool RejectRemoteClients,
    bool RequireExplicitDacl,
    bool SameUserAclIsAuthorization,
    int MaximumPipeInstances)
{
    public static BrokerPipeSecurityPolicy Required { get; } = new(
        RejectRemoteClients: true,
        RequireExplicitDacl: true,
        SameUserAclIsAuthorization: false,
        MaximumPipeInstances: BrokerProtocolLimits.MaxConcurrentConnections);
}
