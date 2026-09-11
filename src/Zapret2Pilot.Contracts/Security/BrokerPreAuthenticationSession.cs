using System.Security.Cryptography;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Security;

public readonly record struct BrokerChallengeHandle(Guid Value)
{
    public static BrokerChallengeHandle New() => new(Guid.NewGuid());
}

public enum BrokerChallengeIssueStatus
{
    Issued,
    PeerRejected,
    CapacityExceeded,
    RateLimited,
    AlreadyAuthenticated,
}

public sealed record BrokerChallengeIssueResult(
    BrokerChallengeIssueStatus Status,
    BrokerChallengeMessage? Challenge,
    BrokerChallengeHandle? Handle,
    TimeSpan? RetryAfter);

/// <summary>
/// Owns the broker-side bootstrap secret and bounded pre-authentication state for one AppSession.
/// The byte[] passed to the constructor transfers ownership to this instance and is zeroed on Dispose.
/// Concrete Windows bootstrap-secret transport/storage remains a #17 responsibility.
/// </summary>
public sealed class BrokerPreAuthenticationSession : IDisposable
{
    private readonly object sync = new();
    private readonly BrokerClientBinding expected;
    private readonly byte[] bootstrapSecret;
    private readonly TimeProvider timeProvider;
    private readonly BrokerRequestRateGate challengeRateGate;
    private readonly Dictionary<BrokerChallengeHandle, ChallengeState> challenges = [];
    private readonly BrokerSessionId brokerSessionId = BrokerSessionId.New();
    private bool authenticated;
    private bool disposed;

    public BrokerPreAuthenticationSession(
        BrokerClientBinding expected,
        byte[] bootstrapSecret,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(bootstrapSecret);
        if (bootstrapSecret.Length != BrokerAuthenticator.SecretSizeBytes)
        {
            throw new ArgumentException("Bootstrap secret must contain exactly 256 bits.", nameof(bootstrapSecret));
        }

        this.expected = expected;
        this.bootstrapSecret = bootstrapSecret;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        challengeRateGate = new(
            BrokerProtocolLimits.MaxPreAuthChallengesPerSecond,
            BrokerProtocolLimits.PreAuthChallengeBurstCapacity,
            this.timeProvider);
    }

    public BrokerSessionId BrokerSessionId => brokerSessionId;

    public BrokerChallengeIssueResult TryIssueChallenge(BrokerPeerIdentity peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        lock (sync)
        {
            ThrowIfDisposed();
            BrokerAdmissionRejectionReason peerRejection = BrokerPeerIdentityValidator.Validate(expected, peer);
            if (peerRejection != BrokerAdmissionRejectionReason.None)
            {
                return new(BrokerChallengeIssueStatus.PeerRejected, null, null, null);
            }

            RemoveExpiredChallenges();
            if (authenticated)
            {
                return new(BrokerChallengeIssueStatus.AlreadyAuthenticated, null, null, null);
            }

            if (challenges.Count >= BrokerProtocolLimits.MaxPreAuthChallenges)
            {
                return new(BrokerChallengeIssueStatus.CapacityExceeded, null, null, null);
            }

            if (!challengeRateGate.TryAcquire())
            {
                return new(
                    BrokerChallengeIssueStatus.RateLimited,
                    null,
                    null,
                    BrokerProtocolLimits.PreAuthChallengeRetryAfter);
            }

            BrokerChallengeHandle handle = BrokerChallengeHandle.New();
            byte[] nonce = RandomNumberGenerator.GetBytes(BrokerAuthenticator.NonceSizeBytes);
            BrokerChallengeMessage challenge = new(
                BrokerProtocolVersion.V1,
                BrokerProtocolRange.Current,
                brokerSessionId,
                nonce,
                checked((int)BrokerProtocolLimits.HandshakeTimeout.TotalMilliseconds));
            challenges.Add(
                handle,
                new ChallengeState(challenge, peer, timeProvider.GetTimestamp()));
            return new(BrokerChallengeIssueStatus.Issued, challenge, handle, null);
        }
    }

    public BrokerAdmissionDecision TryAdmit(
        BrokerChallengeHandle handle,
        BrokerPeerIdentity peer,
        BrokerRequestEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(envelope);
        lock (sync)
        {
            ThrowIfDisposed();
            if (!challenges.Remove(handle, out ChallengeState? state))
            {
                return Reject(BrokerAdmissionRejectionReason.ReplayedChallenge);
            }

            try
            {
                if (timeProvider.GetElapsedTime(state.CreatedAtTimestamp, timeProvider.GetTimestamp())
                    > BrokerProtocolLimits.HandshakeTimeout)
                {
                    return Reject(BrokerAdmissionRejectionReason.ExpiredChallenge);
                }

                BrokerAdmissionRejectionReason peerRejection = BrokerPeerIdentityValidator.Validate(expected, peer);
                if (peerRejection != BrokerAdmissionRejectionReason.None || state.Peer != peer)
                {
                    return Reject(peerRejection == BrokerAdmissionRejectionReason.None
                        ? BrokerAdmissionRejectionReason.WrongProcessId
                        : peerRejection);
                }

                BrokerRequestSemanticValidationResult semantic = BrokerRequestSemanticValidator.Validate(envelope);
                if (!semantic.Accepted || envelope.Request is not BrokerHelloRequest hello)
                {
                    return Reject(BrokerAdmissionRejectionReason.InvalidRequestSemantics);
                }

                if (envelope.BrokerSessionId != brokerSessionId
                    || state.Challenge.BrokerSessionId != brokerSessionId)
                {
                    return Reject(BrokerAdmissionRejectionReason.WrongBrokerSession);
                }

                if (envelope.AppSessionId != expected.AppSessionId
                    || hello.AppSessionId != expected.AppSessionId)
                {
                    return Reject(BrokerAdmissionRejectionReason.StaleApplicationSession);
                }

                BrokerProtocolNegotiationResult negotiation = BrokerProtocolNegotiator.Negotiate(
                    hello.SupportedProtocols,
                    state.Challenge.SupportedProtocols);
                if (!negotiation.Accepted
                    || negotiation.SelectedVersion is null
                    || negotiation.SelectedVersion.Value != envelope.Protocol)
                {
                    return Reject(BrokerAdmissionRejectionReason.UnsupportedProtocol);
                }

                BrokerAuthenticationTranscript transcript = BrokerAuthenticationTranscript.Create(
                    negotiation.SelectedVersion.Value,
                    hello.SupportedProtocols,
                    state.Challenge,
                    expected.AppSessionId,
                    peer,
                    hello.ClientNonce);
                if (!BrokerAuthenticator.VerifyProof(bootstrapSecret, transcript, hello.Proof.Span))
                {
                    return Reject(BrokerAdmissionRejectionReason.InvalidBootstrapProof);
                }

                authenticated = true;
                InvalidateOutstandingChallenges();
                return new(
                    true,
                    BrokerAdmissionRejectionReason.None,
                    expected.AppSessionId,
                    brokerSessionId,
                    negotiation.SelectedVersion);
            }
            finally
            {
                ZeroChallenge(state);
            }
        }
    }

    public void AbandonChallenge(BrokerChallengeHandle handle)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (challenges.Remove(handle, out ChallengeState? state))
            {
                ZeroChallenge(state);
            }
        }
    }

    public void ReleaseAuthenticatedConnection()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            authenticated = false;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            authenticated = false;
            InvalidateOutstandingChallenges();
            CryptographicOperations.ZeroMemory(bootstrapSecret);
        }
    }

    private void RemoveExpiredChallenges()
    {
        long now = timeProvider.GetTimestamp();
        BrokerChallengeHandle[] expired = challenges
            .Where(pair => timeProvider.GetElapsedTime(pair.Value.CreatedAtTimestamp, now)
                > BrokerProtocolLimits.HandshakeTimeout)
            .Select(static pair => pair.Key)
            .ToArray();
        foreach (BrokerChallengeHandle handle in expired)
        {
            ChallengeState state = challenges[handle];
            challenges.Remove(handle);
            ZeroChallenge(state);
        }
    }

    private void InvalidateOutstandingChallenges()
    {
        foreach (ChallengeState state in challenges.Values)
        {
            ZeroChallenge(state);
        }

        challenges.Clear();
    }

    private static void ZeroChallenge(ChallengeState state)
    {
        if (state.Challenge.ServerNonce.TryGetArray(out ArraySegment<byte> segment)
            && segment.Array is not null)
        {
            CryptographicOperations.ZeroMemory(segment.Array.AsSpan(segment.Offset, segment.Count));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static BrokerAdmissionDecision Reject(BrokerAdmissionRejectionReason reason)
        => new(false, reason, null, null, null);

    private sealed record ChallengeState(
        BrokerChallengeMessage Challenge,
        BrokerPeerIdentity Peer,
        long CreatedAtTimestamp);
}
