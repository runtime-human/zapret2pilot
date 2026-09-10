using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerHandshakeAndPreAuthTests
{
    private static readonly byte[] Secret = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();

    [Fact]
    public void SerializedChallengeToSerializedHelloCompletesAdmissionWithoutSharedChallengeObject()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);

        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        Assert.Equal(BrokerChallengeIssueStatus.Issued, issued.Status);
        BrokerChallengeMessage serverChallenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle serverHandle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);

        byte[] challengeFrame = BrokerChallengeFrameCodec.Encode(serverChallenge);
        byte[] clientTransportBytes = challengeFrame.ToArray();
        BrokerChallengeFrameDecodeResult clientDecoded = BrokerChallengeFrameCodec.Decode(clientTransportBytes);
        BrokerChallengeMessage clientChallenge = Assert.IsType<BrokerChallengeMessage>(clientDecoded.Challenge);
        Assert.Equal(BrokerChallengeFrameDecodeStatus.Success, clientDecoded.Status);
        Assert.NotSame(serverChallenge, clientChallenge);

        BrokerRequestEnvelope clientHello = CreateHelloEnvelope(expected, peer, clientChallenge, Secret);
        byte[] helloPayload = BrokerProtocolCodec.EncodeRequest(clientHello);
        byte[] helloFrame = BrokerFrameCodec.Encode(helloPayload, BrokerProtocolLimits.MaxPreAuthHelloFrameBytes);
        BrokerFrameDecodeResult brokerFrame = BrokerFrameCodec.Decode(helloFrame, BrokerProtocolLimits.MaxPreAuthHelloFrameBytes);
        BrokerProtocolDecodeResult brokerDecoded = BrokerProtocolCodec.DecodeRequest(Assert.IsType<byte[]>(brokerFrame.Payload));
        BrokerRequestEnvelope brokerHello = Assert.IsType<BrokerRequestEnvelope>(brokerDecoded.Envelope);

        BrokerAdmissionDecision decision = session.TryAdmit(serverHandle, peer, brokerHello);

        Assert.True(decision.Accepted);
        Assert.Equal(expected.AppSessionId, decision.AppSessionId);
        Assert.Equal(serverChallenge.BrokerSessionId, decision.BrokerSessionId);
    }

    [Fact]
    public void ChallengeCodecRejectsMalformedPayload()
    {
        byte[] malformedPayload = "{not-json"u8.ToArray();
        byte[] frame = BrokerFrameCodec.Encode(malformedPayload, BrokerProtocolLimits.MaxChallengeFrameBytes);

        BrokerChallengeFrameDecodeResult result = BrokerChallengeFrameCodec.Decode(frame);

        Assert.Equal(BrokerChallengeFrameDecodeStatus.Malformed, result.Status);
        Assert.Null(result.Challenge);
    }

    [Fact]
    public void ChallengeCodecRejectsOversizedPrefixBeforeAllocation()
    {
        byte[] prefix = new byte[BrokerProtocolLimits.LengthPrefixBytes];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, BrokerProtocolLimits.MaxChallengeFrameBytes + 1);

        BrokerChallengeFrameDecodeResult result = BrokerChallengeFrameCodec.Decode(prefix);

        Assert.Equal(BrokerChallengeFrameDecodeStatus.Oversized, result.Status);
        Assert.Null(result.Challenge);
    }

    [Fact]
    public void ChallengeCodecRejectsProtocolMismatch()
    {
        BrokerChallengeMessage challenge = new(
            new BrokerProtocolVersion(2, 0),
            new BrokerProtocolRange(new(2, 0), new(2, 0)),
            BrokerSessionId.New(),
            RandomNumberGenerator.GetBytes(BrokerAuthenticator.NonceSizeBytes),
            checked((int)BrokerProtocolLimits.HandshakeTimeout.TotalMilliseconds));

        byte[] frame = BrokerChallengeFrameCodec.EncodeUncheckedForTest(challenge);
        BrokerChallengeFrameDecodeResult result = BrokerChallengeFrameCodec.Decode(frame);

        Assert.Equal(BrokerChallengeFrameDecodeStatus.UnsupportedProtocol, result.Status);
        Assert.Null(result.Challenge);
    }

    [Fact]
    public void ExpiredChallengeIsRejectedByMonotonicElapsedTime()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, Secret);

        timeProvider.Advance(BrokerProtocolLimits.HandshakeTimeout + TimeSpan.FromMilliseconds(1));
        BrokerAdmissionDecision decision = session.TryAdmit(handle, peer, hello);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ExpiredChallenge, decision.RejectionReason);
    }

    [Fact]
    public void WallClockJumpAloneDoesNotExpireChallenge()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, Secret);

        timeProvider.JumpUtc(TimeSpan.FromDays(30));
        BrokerAdmissionDecision decision = session.TryAdmit(handle, peer, hello);

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void WrongBrokerSessionIdIsRejected()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, Secret) with
        {
            BrokerSessionId = BrokerSessionId.New(),
        };

        BrokerAdmissionDecision decision = session.TryAdmit(handle, peer, hello);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.WrongBrokerSession, decision.RejectionReason);
    }

    [Fact]
    public void WrongProofIsRejected()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, Secret);
        BrokerHelloRequest body = Assert.IsType<BrokerHelloRequest>(hello.Request);
        byte[] badProof = body.Proof.ToArray();
        badProof[0] ^= 0xff;
        hello = hello with { Request = body with { Proof = badProof } };

        BrokerAdmissionDecision decision = session.TryAdmit(handle, peer, hello);

        Assert.False(decision.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.InvalidBootstrapProof, decision.RejectionReason);
    }

    [Fact]
    public void BadFirstHelloBurnsOnlyItsChallengeAndFreshRotationCanAuthenticate()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);
        BrokerChallengeIssueResult first = session.TryIssueChallenge(peer);
        BrokerChallengeMessage firstChallenge = Assert.IsType<BrokerChallengeMessage>(first.Challenge);
        BrokerChallengeHandle firstHandle = Assert.IsType<BrokerChallengeHandle>(first.Handle);
        BrokerRequestEnvelope badHello = CreateHelloEnvelope(expected, peer, firstChallenge, new byte[32]);

        BrokerAdmissionDecision rejected = session.TryAdmit(firstHandle, peer, badHello);
        BrokerChallengeIssueResult second = session.TryIssueChallenge(peer);
        BrokerChallengeMessage secondChallenge = Assert.IsType<BrokerChallengeMessage>(second.Challenge);
        BrokerAdmissionDecision admitted = session.TryAdmit(
            Assert.IsType<BrokerChallengeHandle>(second.Handle),
            peer,
            CreateHelloEnvelope(expected, peer, secondChallenge, Secret));

        Assert.Equal(BrokerAdmissionRejectionReason.InvalidBootstrapProof, rejected.RejectionReason);
        Assert.Equal(BrokerChallengeIssueStatus.Issued, second.Status);
        Assert.True(admitted.Accepted);
    }

    [Fact]
    public void SuccessfulAdmissionRejectsReplayAndInvalidatesParallelOutstandingChallenge()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);
        BrokerChallengeIssueResult first = session.TryIssueChallenge(peer);
        BrokerChallengeIssueResult parallel = session.TryIssueChallenge(peer);
        BrokerChallengeMessage firstChallenge = Assert.IsType<BrokerChallengeMessage>(first.Challenge);
        BrokerChallengeMessage parallelChallenge = Assert.IsType<BrokerChallengeMessage>(parallel.Challenge);
        BrokerChallengeHandle firstHandle = Assert.IsType<BrokerChallengeHandle>(first.Handle);
        BrokerChallengeHandle parallelHandle = Assert.IsType<BrokerChallengeHandle>(parallel.Handle);
        BrokerRequestEnvelope firstHello = CreateHelloEnvelope(expected, peer, firstChallenge, Secret);

        BrokerAdmissionDecision admitted = session.TryAdmit(firstHandle, peer, firstHello);
        BrokerAdmissionDecision replay = session.TryAdmit(firstHandle, peer, firstHello);
        session.ReleaseAuthenticatedConnection();
        BrokerAdmissionDecision staleParallel = session.TryAdmit(
            parallelHandle,
            peer,
            CreateHelloEnvelope(expected, peer, parallelChallenge, Secret));

        Assert.True(admitted.Accepted);
        Assert.False(replay.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ReplayedChallenge, replay.RejectionReason);
        Assert.False(staleParallel.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ReplayedChallenge, staleParallel.RejectionReason);
    }

    [Fact]
    public void WrongPeerCannotConsumeChallengeBudgetWhileExpectedClientCanAuthenticate()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity expectedPeer = CreatePeer();
        BrokerPeerIdentity attacker = expectedPeer with { ProcessId = expectedPeer.ProcessId + 1 };
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);

        for (int i = 0; i < 32; i++)
        {
            BrokerChallengeIssueResult attackerResult = session.TryIssueChallenge(attacker);
            Assert.Equal(BrokerChallengeIssueStatus.PeerRejected, attackerResult.Status);
            Assert.Null(attackerResult.Challenge);
        }

        BrokerChallengeIssueResult clientResult = session.TryIssueChallenge(expectedPeer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(clientResult.Challenge);
        BrokerAdmissionDecision admitted = session.TryAdmit(
            Assert.IsType<BrokerChallengeHandle>(clientResult.Handle),
            expectedPeer,
            CreateHelloEnvelope(expected, expectedPeer, challenge, Secret));

        Assert.Equal(BrokerChallengeIssueStatus.Issued, clientResult.Status);
        Assert.True(admitted.Accepted);
    }

    [Fact]
    public void PreAuthFloodIsBoundedByLiveChallengeAndIssueBudgets()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, Secret, timeProvider);

        BrokerChallengeIssueResult first = session.TryIssueChallenge(peer);
        BrokerChallengeIssueResult second = session.TryIssueChallenge(peer);
        BrokerChallengeIssueResult liveCapacity = session.TryIssueChallenge(peer);
        session.AbandonChallenge(Assert.IsType<BrokerChallengeHandle>(first.Handle));
        session.AbandonChallenge(Assert.IsType<BrokerChallengeHandle>(second.Handle));
        BrokerChallengeIssueResult third = session.TryIssueChallenge(peer);
        BrokerChallengeIssueResult fourth = session.TryIssueChallenge(peer);
        session.AbandonChallenge(Assert.IsType<BrokerChallengeHandle>(third.Handle));
        session.AbandonChallenge(Assert.IsType<BrokerChallengeHandle>(fourth.Handle));
        BrokerChallengeIssueResult rateLimited = session.TryIssueChallenge(peer);

        Assert.Equal(BrokerChallengeIssueStatus.Issued, first.Status);
        Assert.Equal(BrokerChallengeIssueStatus.Issued, second.Status);
        Assert.Equal(BrokerChallengeIssueStatus.CapacityExceeded, liveCapacity.Status);
        Assert.Equal(BrokerChallengeIssueStatus.Issued, third.Status);
        Assert.Equal(BrokerChallengeIssueStatus.Issued, fourth.Status);
        Assert.Equal(BrokerChallengeIssueStatus.RateLimited, rateLimited.Status);

        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(BrokerChallengeIssueStatus.Issued, session.TryIssueChallenge(peer).Status);
    }

    [Fact]
    public void DisposedPreAuthSessionRejectsFurtherChallengeIssuance()
    {
        BrokerPreAuthenticationSession session = new(CreateExpected(), Secret, new ManualTimeProvider());
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.TryIssueChallenge(CreatePeer()));
    }

    private static BrokerRequestEnvelope CreateHelloEnvelope(
        BrokerClientBinding expected,
        BrokerPeerIdentity peer,
        BrokerChallengeMessage challenge,
        ReadOnlySpan<byte> secret)
    {
        byte[] clientNonce = Enumerable.Range(80, BrokerAuthenticator.NonceSizeBytes)
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
}
