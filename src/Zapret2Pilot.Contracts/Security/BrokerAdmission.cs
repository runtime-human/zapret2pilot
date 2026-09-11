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

public sealed record BrokerAuthenticationTranscript(
    BrokerProtocolVersion Protocol,
    BrokerProtocolRange ClientSupportedProtocols,
    BrokerProtocolRange ServerSupportedProtocols,
    AppSessionId AppSessionId,
    BrokerSessionId BrokerSessionId,
    int ProcessId,
    long ProcessCreationTimeFileTime,
    uint WindowsSessionId,
    ReadOnlyMemory<byte> ChallengeNonce,
    ReadOnlyMemory<byte> ClientNonce)
{
    public static BrokerAuthenticationTranscript Create(
        BrokerProtocolVersion protocol,
        BrokerProtocolRange clientSupportedProtocols,
        BrokerChallengeMessage challenge,
        AppSessionId appSessionId,
        BrokerPeerIdentity peer,
        ReadOnlyMemory<byte> clientNonce)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(peer);
        return new(
            protocol,
            clientSupportedProtocols,
            challenge.SupportedProtocols,
            appSessionId,
            challenge.BrokerSessionId,
            peer.ProcessId,
            peer.ProcessCreationTimeFileTime,
            peer.WindowsSessionId,
            challenge.ServerNonce,
            clientNonce);
    }

    internal byte[] ToBytes()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteVersion(writer, Protocol);
            WriteRange(writer, ClientSupportedProtocols);
            WriteRange(writer, ServerSupportedProtocols);
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

    private static void WriteRange(BinaryWriter writer, BrokerProtocolRange range)
    {
        WriteVersion(writer, range.Minimum);
        WriteVersion(writer, range.Maximum);
    }

    private static void WriteVersion(BinaryWriter writer, BrokerProtocolVersion version)
    {
        writer.Write(version.Major);
        writer.Write(version.Minor);
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
        ValidateSecret(bootstrapSecret);
        byte[] transcriptBytes = transcript.ToBytes();
        try
        {
            return HMACSHA256.HashData(bootstrapSecret, transcriptBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transcriptBytes);
        }
    }

    public static bool VerifyProof(
        ReadOnlySpan<byte> bootstrapSecret,
        BrokerAuthenticationTranscript transcript,
        ReadOnlySpan<byte> proof)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ValidateSecret(bootstrapSecret);
        if (proof.Length != ProofSizeBytes)
        {
            return false;
        }

        byte[] transcriptBytes = transcript.ToBytes();
        Span<byte> expected = stackalloc byte[ProofSizeBytes];
        try
        {
            _ = HMACSHA256.HashData(bootstrapSecret, transcriptBytes, expected);
            return CryptographicOperations.FixedTimeEquals(expected, proof);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(transcriptBytes);
        }
    }

    private static void ValidateSecret(ReadOnlySpan<byte> bootstrapSecret)
    {
        if (bootstrapSecret.Length != SecretSizeBytes)
        {
            throw new ArgumentException("Bootstrap secret must contain exactly 256 bits.", nameof(bootstrapSecret));
        }
    }
}

public enum BrokerAdmissionRejectionReason
{
    None,
    ReplayedChallenge,
    ExpiredChallenge,
    UnsupportedProtocol,
    InvalidRequestSemantics,
    WrongBrokerSession,
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

internal static class BrokerPeerIdentityValidator
{
    public static BrokerAdmissionRejectionReason Validate(
        BrokerClientBinding expected,
        BrokerPeerIdentity peer)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(peer);
        if (peer.ProcessId != expected.ProcessId)
        {
            return BrokerAdmissionRejectionReason.WrongProcessId;
        }

        if (peer.ProcessCreationTimeFileTime != expected.ProcessCreationTimeFileTime)
        {
            return BrokerAdmissionRejectionReason.ProcessCreationTimeMismatch;
        }

        if (peer.WindowsSessionId != expected.WindowsSessionId)
        {
            return BrokerAdmissionRejectionReason.WrongWindowsSession;
        }

        if (!string.Equals(peer.UserSid, expected.UserSid, StringComparison.Ordinal))
        {
            return BrokerAdmissionRejectionReason.WrongUserSid;
        }

        if (peer.LogonSessionId != expected.LogonSessionId)
        {
            return BrokerAdmissionRejectionReason.WrongLogonSession;
        }

        return peer.IntegrityLevelRid == expected.IntegrityLevelRid
            ? BrokerAdmissionRejectionReason.None
            : BrokerAdmissionRejectionReason.WrongIntegrityLevel;
    }
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
