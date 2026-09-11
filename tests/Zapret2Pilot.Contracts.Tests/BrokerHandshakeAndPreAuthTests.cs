using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerHandshakeAndPreAuthTests
{
    [Fact]
    public void SerializedChallengeToSerializedHelloCompletesAdmissionWithoutSharedChallengeObject()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), timeProvider);

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

        BrokerRequestEnvelope clientHello = CreateHelloEnvelope(expected, peer, clientChallenge, clientSecret);
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
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(BrokerAuthenticator.NonceSizeBytes));
        string json = $$"""
            {
              "handshakeProtocol":{"major":2,"minor":0},
              "kind":"brokerChallenge",
              "supportedProtocols":{"minimum":{"major":2,"minor":0},"maximum":{"major":2,"minor":0}},
              "brokerSessionId":"{{Guid.NewGuid()}}",
              "serverNonce":"{{nonce}}",
              "lifetimeMilliseconds":3000
            }
            """;
        byte[] frame = BrokerFrameCodec.Encode(Encoding.UTF8.GetBytes(json), BrokerProtocolLimits.MaxChallengeFrameBytes);

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
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), timeProvider);
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, clientSecret);

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
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), timeProvider);
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, clientSecret);

        timeProvider.JumpUtc(TimeSpan.FromDays(30));
        BrokerAdmissionDecision decision = session.TryAdmit(handle, peer, hello);

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void WrongBrokerSessionIdIsRejected()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, clientSecret) with
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
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());
        BrokerChallengeIssueResult issued = session.TryIssueChallenge(peer);
        BrokerChallengeMessage challenge = Assert.IsType<BrokerChallengeMessage>(issued.Challenge);
        BrokerChallengeHandle handle = Assert.IsType<BrokerChallengeHandle>(issued.Handle);
        BrokerRequestEnvelope hello = CreateHelloEnvelope(expected, peer, challenge, new byte[32]);

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
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), timeProvider);
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
            CreateHelloEnvelope(expected, peer, secondChallenge, clientSecret));

        Assert.Equal(BrokerAdmissionRejectionReason.InvalidBootstrapProof, rejected.RejectionReason);
        Assert.Equal(BrokerChallengeIssueStatus.Issued, second.Status);
        Assert.True(admitted.Accepted);
    }

    [Fact]
    public void SuccessfulAdmissionRejectsReplayAndInvalidatesParallelOutstandingChallenge()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());
        BrokerChallengeIssueResult first = session.TryIssueChallenge(peer);
        BrokerChallengeIssueResult parallel = session.TryIssueChallenge(peer);
        BrokerChallengeMessage firstChallenge = Assert.IsType<BrokerChallengeMessage>(first.Challenge);
        BrokerChallengeMessage parallelChallenge = Assert.IsType<BrokerChallengeMessage>(parallel.Challenge);
        BrokerChallengeHandle firstHandle = Assert.IsType<BrokerChallengeHandle>(first.Handle);
        BrokerChallengeHandle parallelHandle = Assert.IsType<BrokerChallengeHandle>(parallel.Handle);
        BrokerRequestEnvelope firstHello = CreateHelloEnvelope(expected, peer, firstChallenge, clientSecret);

        BrokerAdmissionDecision admitted = session.TryAdmit(firstHandle, peer, firstHello);
        BrokerAdmissionDecision replay = session.TryAdmit(firstHandle, peer, firstHello);
        session.ReleaseAuthenticatedConnection();
        BrokerAdmissionDecision staleParallel = session.TryAdmit(
            parallelHandle,
            peer,
            CreateHelloEnvelope(expected, peer, parallelChallenge, clientSecret));

        Assert.True(admitted.Accepted);
        Assert.False(replay.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ReplayedChallenge, replay.RejectionReason);
        Assert.False(staleParallel.Accepted);
        Assert.Equal(BrokerAdmissionRejectionReason.ReplayedChallenge, staleParallel.RejectionReason);
    }

    [Fact]
    public async Task ParallelBadAndValidChallengesCannotCreateMultipleAdmissions()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());
        BrokerChallengeIssueResult badIssue = session.TryIssueChallenge(peer);
        BrokerChallengeIssueResult validIssue = session.TryIssueChallenge(peer);
        BrokerChallengeMessage badChallenge = Assert.IsType<BrokerChallengeMessage>(badIssue.Challenge);
        BrokerChallengeMessage validChallenge = Assert.IsType<BrokerChallengeMessage>(validIssue.Challenge);
        BrokerChallengeHandle badHandle = Assert.IsType<BrokerChallengeHandle>(badIssue.Handle);
        BrokerChallengeHandle validHandle = Assert.IsType<BrokerChallengeHandle>(validIssue.Handle);

        Task<BrokerAdmissionDecision> bad = Task.Run(() => session.TryAdmit(
            badHandle,
            peer,
            CreateHelloEnvelope(expected, peer, badChallenge, new byte[32])));
        Task<BrokerAdmissionDecision> valid = Task.Run(() => session.TryAdmit(
            validHandle,
            peer,
            CreateHelloEnvelope(expected, peer, validChallenge, clientSecret)));
        BrokerAdmissionDecision[] decisions = await Task.WhenAll(bad, valid);

        Assert.Single(decisions, static decision => decision.Accepted);
        Assert.All(decisions, static decision =>
            Assert.True(decision.Accepted
                || decision.RejectionReason is BrokerAdmissionRejectionReason.InvalidBootstrapProof
                    or BrokerAdmissionRejectionReason.ReplayedChallenge));
    }

    [Fact]
    public void WrongPeerCannotConsumeChallengeBudgetWhileExpectedClientCanAuthenticate()
    {
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity expectedPeer = CreatePeer();
        BrokerPeerIdentity attacker = expectedPeer with { ProcessId = expectedPeer.ProcessId + 1 };
        byte[] clientSecret = CreateSecret();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), new ManualTimeProvider());

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
            CreateHelloEnvelope(expected, expectedPeer, challenge, clientSecret));

        Assert.Equal(BrokerChallengeIssueStatus.Issued, clientResult.Status);
        Assert.True(admitted.Accepted);
    }

    [Fact]
    public void PreAuthFloodIsBoundedByLiveChallengeBurstAndRetryBudget()
    {
        ManualTimeProvider timeProvider = new();
        BrokerClientBinding expected = CreateExpected();
        BrokerPeerIdentity peer = CreatePeer();
        using BrokerPreAuthenticationSession session = new(expected, CreateSecret(), timeProvider);

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
        Assert.Equal(BrokerProtocolLimits.PreAuthChallengeRetryAfter, rateLimited.RetryAfter);

        timeProvider.Advance(BrokerProtocolLimits.PreAuthChallengeRetryAfter - TimeSpan.FromMilliseconds(1));
        Assert.Equal(BrokerChallengeIssueStatus.RateLimited, session.TryIssueChallenge(peer).Status);
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(BrokerChallengeIssueStatus.Issued, session.TryIssueChallenge(peer).Status);
    }

    [Fact]
    public void DisposeZeroesOwnedBootstrapSecretBuffer()
    {
        byte[] ownedSecret = CreateSecret();
        BrokerPreAuthenticationSession session = new(CreateExpected(), ownedSecret, new ManualTimeProvider());

        session.Dispose();

        Assert.All(ownedSecret, static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void DisposedPreAuthSessionRejectsFurtherChallengeIssuance()
    {
        BrokerPreAuthenticationSession session = new(CreateExpected(), CreateSecret(), new ManualTimeProvider());
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = session.TryIssueChallenge(CreatePeer());
        });
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
}
