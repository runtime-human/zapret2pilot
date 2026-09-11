using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerAdmissionTests
{
    [Fact]
    public void SameUserUnrelatedProcessIsNotAuthorized()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer() with { ProcessId = expected.ProcessId + 1 };
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());

        BrokerChallengeIssueResult result = session.TryIssueChallenge(peer);

        Assert.Equal(BrokerChallengeIssueStatus.PeerRejected, result.Status);
        Assert.Null(result.Challenge);
    }

    [Fact]
    public void PidReuseIsRejectedByCreationTimeEvenWhenPidMatches()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer() with
        {
            ProcessId = expected.ProcessId,
            ProcessCreationTimeFileTime = expected.ProcessCreationTimeFileTime + 10_000,
        };
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());

        BrokerChallengeIssueResult result = session.TryIssueChallenge(peer);

        Assert.Equal(BrokerChallengeIssueStatus.PeerRejected, result.Status);
        Assert.Null(result.Challenge);
    }

    [Theory]
    [InlineData(AdmissionMismatch.WindowsSession)]
    [InlineData(AdmissionMismatch.UserSid)]
    [InlineData(AdmissionMismatch.LogonSession)]
    [InlineData(AdmissionMismatch.Integrity)]
    public void WrongSecurityIdentityIsRejectedBeforeChallengeIssuance(AdmissionMismatch mismatch)
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = mismatch switch
        {
            AdmissionMismatch.WindowsSession => CreatePeer() with { WindowsSessionId = expected.WindowsSessionId + 1 },
            AdmissionMismatch.UserSid => CreatePeer() with { UserSid = "S-1-5-21-999-999-999-1001" },
            AdmissionMismatch.LogonSession => CreatePeer() with { LogonSessionId = new LogonSessionId(99, 77) },
            AdmissionMismatch.Integrity => CreatePeer() with { IntegrityLevelRid = 0x1000 },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null),
        };
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());

        BrokerChallengeIssueResult result = session.TryIssueChallenge(peer);

        Assert.Equal(BrokerChallengeIssueStatus.PeerRejected, result.Status);
        Assert.Null(result.Challenge);
    }

    [Fact]
    public void InvalidBootstrapProofIsRejectedAndChallengeCannotBeRetried()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope valid = CreateHelloEnvelope(expected, peer, challenge, clientSecret);
        BrokerHelloRequest body = Assert.IsType<BrokerHelloRequest>(valid.Request);
        byte[] forgedProof = body.Proof.ToArray();
        forgedProof[0] ^= 0xff;
        BrokerRequestEnvelope forged = valid with { Request = body with { Proof = forgedProof } };

        BrokerAdmissionDecision rejected = session.TryAdmit(handle, peer, forged);
        BrokerAdmissionDecision replay = session.TryAdmit(handle, peer, valid);

        Assert.Equal(BrokerAdmissionRejectionReason.InvalidBootstrapProof, rejected.RejectionReason);
        Assert.False(replay.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ReplayedChallenge, replay.RejectionReason);
    }

    [Fact]
    public void ExpiredHandshakeChallengeIsRejected()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), timeProvider);
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, clientSecret);
        timeProvider.Advance(BrokerProtocolLimits.HandshakeTimeout + TimeSpan.FromTicks(1));

        BrokerAdmissionDecision decision = session.TryAdmit(handle, peer, hello);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ExpiredChallenge, decision.RejectionReason);
    }

    private static BrokerRequestEnvelope CreateHelloEnvelope(
        BrokerClientBinding expected,
        BrokerPeerIdentity peer,
        BrokerChallengeMessage challenge,
        ReadOnlySpan<byte> secret)
    {
        byte[] clientNonce = Enumerable.Range(65, BrokerAuthenticator.NonceSizeBytes)
            .Select(static value => (byte)value)
            .ToArray();
        BrokerProtocolNegotiationResult negotiation = BrokerProtocolNegotiator.Negotiate(
            BrokerProtocolRange.Current,
            challenge.SupportedProtocols);
        BrokerProtocolVersion selected = Assert.IsType<BrokerProtocolVersion>(negotiation.SelectedVersion);
        BrokerAuthenticationTranscript transcript = BrokerAuthenticationTranscript.Create(
            selected,
            BrokerProtocolRange.Current,
            challenge,
            expected.AppSessionId,
            peer,
            clientNonce);
        byte[] proof = BrokerAuthenticator.CreateProof(secret, transcript);
        DateTimeOffset issuedAt = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        return new BrokerRequestEnvelope(
            selected,
            expected.AppSessionId,
            challenge.BrokerSessionId,
            BrokerOperationId.New(),
            new RequestSequence(1),
            issuedAt,
            issuedAt + BrokerProtocolLimits.MaxRequestLifetime,
            new BrokerHelloRequest(BrokerProtocolRange.Current, expected.AppSessionId, clientNonce, proof));
    }

    private static byte[] CreateSecret()
        => Enumerable.Range(1, BrokerAuthenticator.SecretSizeBytes)
            .Select(static value => (byte)value)
            .ToArray();

    private static BrokerClientBinding CreateExpected()
        => new(
            AppSessionId.New(),
            ProcessId: 4242,
            ProcessCreationTimeFileTime: 133_800_000_000_000_000,
            WindowsSessionId: 3,
            UserSid: "S-1-5-21-111-222-333-1001",
            LogonSessionId: new LogonSessionId(42, 7),
            IntegrityLevelRid: 0x2000);

    private static BrokerPeerIdentity CreatePeer()
        => new(
            ProcessId: 4242,
            ProcessCreationTimeFileTime: 133_800_000_000_000_000,
            WindowsSessionId: 3,
            UserSid: "S-1-5-21-111-222-333-1001",
            LogonSessionId: new LogonSessionId(42, 7),
            IntegrityLevelRid: 0x2000);

    public enum AdmissionMismatch
    {
        WindowsSession,
        UserSid,
        LogonSession,
        Integrity,
    }
}
