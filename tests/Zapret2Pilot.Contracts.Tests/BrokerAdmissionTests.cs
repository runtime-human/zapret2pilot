using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerAdmissionTests
{
    private static readonly byte[] Secret = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
    private static readonly byte[] ChallengeNonce = Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray();
    private static readonly byte[] ClientNonce = Enumerable.Range(65, 32).Select(static value => (byte)value).ToArray();
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_user_unrelated_process_is_not_authorized()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer() with { ProcessId = expected.ProcessId + 1 };
        BrokerAdmissionGate gate = CreateGate(expected);
        BrokerHelloRequest request = CreateHello(expected, peer, gate.Challenge);

        BrokerAdmissionDecision decision = gate.TryAdmit(peer, request, Now);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.WrongProcessId, decision.RejectionReason);
    }

    [Fact]
    public void Pid_reuse_is_rejected_by_creation_time_even_when_pid_matches()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer() with
        {
            ProcessId = expected.ProcessId,
            ProcessCreationTimeFileTime = expected.ProcessCreationTimeFileTime + 10_000,
        };
        BrokerAdmissionGate gate = CreateGate(expected);
        BrokerHelloRequest request = CreateHello(expected, peer, gate.Challenge);

        BrokerAdmissionDecision decision = gate.TryAdmit(peer, request, Now);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ProcessCreationTimeMismatch, decision.RejectionReason);
    }

    [Theory]
    [InlineData(AdmissionMismatch.WindowsSession)]
    [InlineData(AdmissionMismatch.UserSid)]
    [InlineData(AdmissionMismatch.LogonSession)]
    [InlineData(AdmissionMismatch.Integrity)]
    public void Wrong_security_identity_is_rejected(AdmissionMismatch mismatch)
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
        BrokerAdmissionGate gate = CreateGate(expected);
        BrokerHelloRequest request = CreateHello(expected, peer, gate.Challenge);

        BrokerAdmissionDecision decision = gate.TryAdmit(peer, request, Now);

        Assert.False(decision.Accepted);
        Assert.NotEqual(BrokerAdmissionRejectionReason.None, decision.RejectionReason);
    }

    [Fact]
    public void Invalid_bootstrap_proof_is_rejected()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        BrokerAdmissionGate gate = CreateGate(expected);
        BrokerHelloRequest valid = CreateHello(expected, peer, gate.Challenge);
        byte[] forgedProof = valid.Proof.ToArray();
        forgedProof[0] ^= 0xFF;
        BrokerHelloRequest forged = valid with { Proof = forgedProof };

        BrokerAdmissionDecision decision = gate.TryAdmit(peer, forged, Now);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.InvalidBootstrapProof, decision.RejectionReason);
    }

    [Fact]
    public void Successful_challenge_is_single_use_and_replay_is_rejected()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        BrokerAdmissionGate gate = CreateGate(expected);
        BrokerHelloRequest request = CreateHello(expected, peer, gate.Challenge);

        BrokerAdmissionDecision first = gate.TryAdmit(peer, request, Now);
        BrokerAdmissionDecision replay = gate.TryAdmit(peer, request, Now);

        Assert.True(first.Accepted);
        Assert.Equal(expected.AppSessionId, first.AppSessionId);
        Assert.Equal(gate.Challenge.BrokerSessionId, first.BrokerSessionId);
        Assert.False(replay.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ReplayedChallenge, replay.RejectionReason);
    }

    [Fact]
    public void Expired_handshake_challenge_is_rejected()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        BrokerAdmissionGate gate = CreateGate(expected);
        BrokerHelloRequest request = CreateHello(expected, peer, gate.Challenge);

        BrokerAdmissionDecision decision = gate.TryAdmit(
            peer,
            request,
            Now + BrokerProtocolLimits.HandshakeTimeout + TimeSpan.FromMilliseconds(1));

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ExpiredChallenge, decision.RejectionReason);
    }

    private static BrokerAdmissionGate CreateGate(BrokerClientBinding expected)
        => new(
            expected,
            Secret,
            new BrokerHandshakeChallenge(
                BrokerSessionId.New(),
                ChallengeNonce,
                Now + BrokerProtocolLimits.HandshakeTimeout));

    private static BrokerHelloRequest CreateHello(
        BrokerClientBinding expected,
        BrokerPeerIdentity peer,
        BrokerHandshakeChallenge challenge)
    {
        BrokerAuthenticationTranscript transcript = new(
            BrokerProtocolVersion.V1,
            expected.AppSessionId,
            challenge.BrokerSessionId,
            peer.ProcessId,
            peer.ProcessCreationTimeFileTime,
            peer.WindowsSessionId,
            challenge.Nonce,
            ClientNonce);
        byte[] proof = BrokerAuthenticator.CreateProof(Secret, transcript);
        return new BrokerHelloRequest(
            BrokerProtocolRange.Current,
            expected.AppSessionId,
            ClientNonce,
            proof);
    }

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
