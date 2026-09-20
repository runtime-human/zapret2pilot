using System;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Broker.Transport;

public interface IBrokerAuthenticatedSession : IDisposable
{
    BrokerSessionId BrokerSessionId { get; }

    bool IsAuthenticated { get; }

    BrokerChallengeIssueResult TryIssueChallenge(
        BrokerPeerIdentity peer);

    void AbandonChallenge(
        BrokerChallengeHandle challengeHandle);

    BrokerAdmissionDecision TryAuthenticate(
        BrokerChallengeHandle challengeHandle,
        BrokerPeerIdentity peer,
        BrokerRequestEnvelope helloEnvelope);

    void ReleaseAuthenticatedConnection();

    Task<BrokerResponseEnvelope> DispatchAsync(
        BrokerRequestEnvelope request,
        CancellationToken cancellationToken = default);

    void OnResponseFlushed(
        BrokerRequestEnvelope request,
        BrokerResponseEnvelope response);
}

/// <summary>
/// Transport-independent authenticated Broker session.
///
/// This layer owns #16 authentication/session/freshness/rate admission only.
/// It never mutates runtime state itself: accepted requests are forwarded to
/// the single <see cref="IBrokerRequestDispatcher"/>.
/// </summary>
public sealed class BrokerAuthenticatedSession : IBrokerAuthenticatedSession
{
    private readonly BrokerClientBinding expectedClient;
    private readonly BrokerPreAuthenticationSession preAuthentication;
    private readonly IBrokerRequestDispatcher dispatcher;
    private readonly IBrokerLifetimeController lifetimeController;
    private readonly BrokerRequestRateGate requestRateGate;
    private readonly TimeProvider timeProvider;
    private int authenticated;
    private bool disposed;

    public BrokerAuthenticatedSession(
        BrokerClientBinding expectedClient,
        byte[] bootstrapSecret,
        IBrokerRequestDispatcher dispatcher,
        IBrokerLifetimeController lifetimeController,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(expectedClient);
        ArgumentNullException.ThrowIfNull(bootstrapSecret);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(lifetimeController);

        this.expectedClient = expectedClient;
        this.dispatcher = dispatcher;
        this.lifetimeController = lifetimeController;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        preAuthentication = new BrokerPreAuthenticationSession(
            expectedClient,
            bootstrapSecret,
            this.timeProvider);
        requestRateGate = new BrokerRequestRateGate(
            BrokerProtocolLimits.MaxRequestsPerSecond,
            BrokerProtocolLimits.RequestBurstCapacity,
            this.timeProvider);
    }

    public BrokerSessionId BrokerSessionId => preAuthentication.BrokerSessionId;

    public bool IsAuthenticated => Volatile.Read(ref authenticated) != 0;

    public BrokerChallengeIssueResult TryIssueChallenge(BrokerPeerIdentity peer)
    {
        ThrowIfDisposed();
        return preAuthentication.TryIssueChallenge(peer);
    }

    public void AbandonChallenge(BrokerChallengeHandle challengeHandle)
    {
        ThrowIfDisposed();
        preAuthentication.AbandonChallenge(challengeHandle);
    }

    public BrokerAdmissionDecision TryAuthenticate(
        BrokerChallengeHandle challengeHandle,
        BrokerPeerIdentity peer,
        BrokerRequestEnvelope helloEnvelope)
    {
        ThrowIfDisposed();

        BrokerAdmissionDecision decision = preAuthentication.TryAdmit(
            challengeHandle,
            peer,
            helloEnvelope);
        if (decision.Accepted)
        {
            Volatile.Write(ref authenticated, 1);
        }

        return decision;
    }

    /// <summary>
    /// Releases only transport connectivity. Runtime state, generation,
    /// operation ledger and the retained App-process lease remain intact so a
    /// bounded reconnect cannot create a second lifecycle authority.
    /// </summary>
    public void ReleaseAuthenticatedConnection()
    {
        ThrowIfDisposed();

        if (Interlocked.Exchange(ref authenticated, 0) != 0)
        {
            preAuthentication.ReleaseAuthenticatedConnection();
        }
    }

    public async Task<BrokerResponseEnvelope> DispatchAsync(
        BrokerRequestEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        if (!IsAuthenticated)
        {
            return Reject(
                request,
                BrokerResponseStatus.Unauthorized,
                "BrokerSessionNotAuthenticated",
                "The Broker connection is not authenticated.");
        }

        if (request.AppSessionId != expectedClient.AppSessionId
            || request.BrokerSessionId != BrokerSessionId)
        {
            return Reject(
                request,
                BrokerResponseStatus.Unauthorized,
                "BrokerSessionBindingMismatch",
                "The request is not bound to the authenticated App/Broker session.");
        }

        BrokerRequestSemanticValidationResult semantic =
            BrokerRequestSemanticValidator.Validate(request);
        if (!semantic.Accepted)
        {
            return Reject(
                request,
                BrokerResponseStatus.ProtocolError,
                "InvalidBrokerRequestSemantics",
                "The request violates the broker protocol semantic contract.");
        }

        BrokerRequestFreshnessDecision freshness =
            BrokerRequestFreshnessGuard.Validate(
                request,
                timeProvider.GetUtcNow());
        if (!freshness.Accepted)
        {
            return Reject(
                request,
                freshness.RejectionReason == BrokerRequestFreshnessRejectionReason.Expired
                    ? BrokerResponseStatus.Stale
                    : BrokerResponseStatus.Rejected,
                $"BrokerRequest{freshness.RejectionReason}",
                "The request freshness window was rejected.");
        }

        if (!requestRateGate.TryAcquire())
        {
            return Reject(
                request,
                BrokerResponseStatus.Busy,
                "BrokerRequestRateExceeded",
                "The authenticated broker request rate limit is exhausted.");
        }

        if (request.Request is BrokerHelloRequest)
        {
            return Reject(
                request,
                BrokerResponseStatus.ProtocolError,
                "BrokerHelloAlreadyCompleted",
                "Hello is valid only during pre-authentication.");
        }

        return await dispatcher
            .DispatchAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Called by the concrete transport only after the complete response frame
    /// has been written successfully. This prevents ShutdownBroker from
    /// stopping the host before its Accepted response is observable by App.
    /// </summary>
    public void OnResponseFlushed(
        BrokerRequestEnvelope request,
        BrokerResponseEnvelope response)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (request.Request is ShutdownBrokerRequest
            && response.Status == BrokerResponseStatus.Accepted
            && response.OperationId == request.OperationId)
        {
            lifetimeController.TerminateBroker();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Volatile.Write(ref authenticated, 0);
        preAuthentication.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static BrokerResponseEnvelope Reject(
        BrokerRequestEnvelope request,
        BrokerResponseStatus status,
        string code,
        string message)
        => new(
            request.Protocol,
            request.AppSessionId,
            request.BrokerSessionId,
            request.OperationId,
            status,
            new BrokerErrorResponse(code, message));
}
