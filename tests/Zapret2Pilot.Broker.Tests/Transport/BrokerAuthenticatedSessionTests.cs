using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Broker.Transport;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Broker.Tests.Transport;

public sealed class BrokerAuthenticatedSessionTests
{
    [Fact]
    public static async Task UnauthenticatedRequestFailsClosedBeforeDispatcher()
    {
        SessionFixture fixture = SessionFixture.Create();

        using (fixture.Session)
        {
            BrokerResponseEnvelope response = await fixture.Session.DispatchAsync(
                fixture.CreateRequest(1, new GetRuntimeSnapshotRequest()),
                CancellationToken.None);

            Assert.Equal(BrokerResponseStatus.Unauthorized, response.Status);
            Assert.Equal(0, fixture.Dispatcher.CallCount);
        }
    }

    [Fact]
    public static async Task SuccessfulSerializedIdentityAdmissionEnablesBoundedDispatch()
    {
        SessionFixture fixture = SessionFixture.Create();

        using (fixture.Session)
        {
            BrokerAdmissionDecision admission = fixture.Authenticate();
            Assert.True(admission.Accepted);
            Assert.True(fixture.Session.IsAuthenticated);

            BrokerResponseEnvelope response = await fixture.Session.DispatchAsync(
                fixture.CreateRequest(2, new GetRuntimeSnapshotRequest()),
                CancellationToken.None);

            Assert.Equal(BrokerResponseStatus.Ok, response.Status);
            Assert.Equal(1, fixture.Dispatcher.CallCount);
        }
    }

    [Fact]
    public static async Task DisconnectDoesNotTerminateRuntimeAndReconnectRequiresFreshChallenge()
    {
        SessionFixture fixture = SessionFixture.Create();

        using (fixture.Session)
        {
            Assert.True(fixture.Authenticate().Accepted);

            fixture.Session.ReleaseAuthenticatedConnection();

            BrokerResponseEnvelope disconnected = await fixture.Session.DispatchAsync(
                fixture.CreateRequest(2, new GetRuntimeSnapshotRequest()),
                CancellationToken.None);
            Assert.Equal(BrokerResponseStatus.Unauthorized, disconnected.Status);
            Assert.Equal(0, fixture.Lifetime.TerminateCallCount);

            Assert.True(fixture.Authenticate(sequence: 3).Accepted);

            BrokerResponseEnvelope reconnected = await fixture.Session.DispatchAsync(
                fixture.CreateRequest(4, new GetRuntimeSnapshotRequest()),
                CancellationToken.None);
            Assert.Equal(BrokerResponseStatus.Ok, reconnected.Status);
            Assert.Equal(1, fixture.Dispatcher.CallCount);
            Assert.Equal(0, fixture.Lifetime.TerminateCallCount);
        }
    }

    [Fact]
    public static async Task SessionBindingAndFreshnessAreCheckedBeforeDispatcher()
    {
        SessionFixture fixture = SessionFixture.Create();

        using (fixture.Session)
        {
            Assert.True(fixture.Authenticate().Accepted);

            BrokerRequestEnvelope wrongSession = fixture.CreateRequest(
                2,
                new GetRuntimeSnapshotRequest()) with
            {
                AppSessionId = AppSessionId.New(),
            };

            BrokerResponseEnvelope wrongSessionResponse =
                await fixture.Session.DispatchAsync(wrongSession, CancellationToken.None);
            Assert.Equal(BrokerResponseStatus.Unauthorized, wrongSessionResponse.Status);

            BrokerRequestEnvelope expired = fixture.CreateRequest(
                3,
                new GetRuntimeSnapshotRequest()) with
            {
                IssuedAtUtc = fixture.TimeProvider.GetUtcNow() - TimeSpan.FromSeconds(10),
                DeadlineUtc = fixture.TimeProvider.GetUtcNow() - TimeSpan.FromSeconds(1),
            };

            BrokerResponseEnvelope expiredResponse =
                await fixture.Session.DispatchAsync(expired, CancellationToken.None);
            Assert.Equal(BrokerResponseStatus.Stale, expiredResponse.Status);
            Assert.Equal(0, fixture.Dispatcher.CallCount);
        }
    }

    [Fact]
    public static async Task AuthenticatedRequestBurstIsBounded()
    {
        SessionFixture fixture = SessionFixture.Create();

        using (fixture.Session)
        {
            Assert.True(fixture.Authenticate().Accepted);

            for (int sequence = 2; sequence <= BrokerProtocolLimits.RequestBurstCapacity + 1; sequence++)
            {
                BrokerResponseEnvelope accepted = await fixture.Session.DispatchAsync(
                    fixture.CreateRequest(sequence, new GetRuntimeSnapshotRequest()),
                    CancellationToken.None);
                Assert.Equal(BrokerResponseStatus.Ok, accepted.Status);
            }

            BrokerResponseEnvelope limited = await fixture.Session.DispatchAsync(
                fixture.CreateRequest(
                    BrokerProtocolLimits.RequestBurstCapacity + 2,
                    new GetRuntimeSnapshotRequest()),
                CancellationToken.None);

            Assert.Equal(BrokerResponseStatus.Busy, limited.Status);
            Assert.Equal(
                BrokerProtocolLimits.RequestBurstCapacity,
                fixture.Dispatcher.CallCount);
        }
    }

    [Fact]
    public static async Task ShutdownTerminatesOnlyAfterAcceptedResponseIsFlushed()
    {
        SessionFixture fixture = SessionFixture.Create();

        using (fixture.Session)
        {
            Assert.True(fixture.Authenticate().Accepted);

            BrokerRequestEnvelope request = fixture.CreateRequest(
                2,
                new ShutdownBrokerRequest());
            fixture.Dispatcher.ShutdownAccepted = true;

            BrokerResponseEnvelope response = await fixture.Session.DispatchAsync(
                request,
                CancellationToken.None);

            Assert.Equal(BrokerResponseStatus.Accepted, response.Status);
            Assert.Equal(0, fixture.Lifetime.TerminateCallCount);

            fixture.Session.OnResponseFlushed(request, response);

            Assert.Equal(1, fixture.Lifetime.TerminateCallCount);
        }
    }

    private sealed class SessionFixture
    {
        private readonly byte[] clientSecret;

        private SessionFixture(
            BrokerAuthenticatedSession session,
            BrokerClientBinding binding,
            BrokerPeerIdentity peer,
            byte[] clientSecret,
            FakeDispatcher dispatcher,
            FakeLifetimeController lifetime,
            FrozenTimeProvider timeProvider)
        {
            Session = session;
            Binding = binding;
            Peer = peer;
            this.clientSecret = clientSecret;
            Dispatcher = dispatcher;
            Lifetime = lifetime;
            TimeProvider = timeProvider;
        }

        public BrokerAuthenticatedSession Session { get; }

        public BrokerClientBinding Binding { get; }

        public BrokerPeerIdentity Peer { get; }

        public FakeDispatcher Dispatcher { get; }

        public FakeLifetimeController Lifetime { get; }

        public FrozenTimeProvider TimeProvider { get; }

        public static SessionFixture Create()
        {
            FrozenTimeProvider timeProvider = new();
            AppSessionId appSessionId = AppSessionId.New();
            BrokerClientBinding binding = new(
                appSessionId,
                ProcessId: 4242,
                ProcessCreationTimeFileTime: 123456789,
                WindowsSessionId: 3,
                UserSid: "S-1-5-21-1000",
                LogonSessionId: new LogonSessionId(11, 22),
                IntegrityLevelRid: 0x2000);
            BrokerPeerIdentity peer = new(
                binding.ProcessId,
                binding.ProcessCreationTimeFileTime,
                binding.WindowsSessionId,
                binding.UserSid,
                binding.LogonSessionId,
                binding.IntegrityLevelRid);

            byte[] clientSecret = RandomNumberGenerator.GetBytes(
                BrokerAuthenticator.SecretSizeBytes);
            byte[] brokerSecret = clientSecret.ToArray();
            FakeDispatcher dispatcher = new();
            FakeLifetimeController lifetime = new();
            BrokerAuthenticatedSession session = new(
                binding,
                brokerSecret,
                dispatcher,
                lifetime,
                timeProvider);

            return new(
                session,
                binding,
                peer,
                clientSecret,
                dispatcher,
                lifetime,
                timeProvider);
        }

        public BrokerAdmissionDecision Authenticate(long sequence = 1)
        {
            BrokerChallengeIssueResult issued = Session.TryIssueChallenge(Peer);
            Assert.Equal(BrokerChallengeIssueStatus.Issued, issued.Status);
            Assert.NotNull(issued.Challenge);
            Assert.NotNull(issued.Handle);

            byte[] clientNonce = RandomNumberGenerator.GetBytes(
                BrokerAuthenticator.NonceSizeBytes);
            BrokerAuthenticationTranscript transcript =
                BrokerAuthenticationTranscript.Create(
                    BrokerProtocolVersion.V1,
                    BrokerProtocolRange.Current,
                    issued.Challenge!,
                    Binding.AppSessionId,
                    Peer,
                    clientNonce);
            byte[] proof = BrokerAuthenticator.CreateProof(
                clientSecret,
                transcript);

            BrokerRequestEnvelope hello = new(
                BrokerProtocolVersion.V1,
                Binding.AppSessionId,
                Session.BrokerSessionId,
                BrokerOperationId.New(),
                new RequestSequence(sequence),
                TimeProvider.GetUtcNow(),
                TimeProvider.GetUtcNow() + BrokerProtocolLimits.MaxRequestLifetime,
                new BrokerHelloRequest(
                    BrokerProtocolRange.Current,
                    Binding.AppSessionId,
                    clientNonce,
                    proof));

            return Session.TryAuthenticate(
                issued.Handle!.Value,
                Peer,
                hello);
        }

        public BrokerRequestEnvelope CreateRequest(
            long sequence,
            IBrokerRequest request)
        {
            DateTimeOffset now = TimeProvider.GetUtcNow();
            return new(
                BrokerProtocolVersion.V1,
                Binding.AppSessionId,
                Session.BrokerSessionId,
                BrokerOperationId.New(),
                new RequestSequence(sequence),
                now,
                now + BrokerProtocolLimits.MaxRequestLifetime,
                request);
        }
    }

    private sealed class FakeDispatcher : IBrokerRequestDispatcher
    {
        public int CallCount { get; private set; }

        public bool ShutdownAccepted { get; set; }

        public Task<BrokerResponseEnvelope> DispatchAsync(
            BrokerRequestEnvelope request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            BrokerResponseStatus status =
                request.Request is ShutdownBrokerRequest && ShutdownAccepted
                    ? BrokerResponseStatus.Accepted
                    : BrokerResponseStatus.Ok;
            IBrokerResponse body = request.Request is GetRuntimeSnapshotRequest
                ? new BrokerRuntimeSnapshotResponse(
                    new BrokerRuntimeSnapshot(
                        new RuntimeGeneration(1),
                        BrokerRuntimeState.Stopped,
                        ActivePlanId: null,
                        ActiveOperationId: null))
                : new BrokerMutationAcceptedResponse(
                    request.OperationId,
                    new RuntimeGeneration(1));

            return Task.FromResult(
                new BrokerResponseEnvelope(
                    request.Protocol,
                    request.AppSessionId,
                    request.BrokerSessionId,
                    request.OperationId,
                    status,
                    body));
        }
    }

    private sealed class FakeLifetimeController : IBrokerLifetimeController
    {
        public int TerminateCallCount { get; private set; }

        public Task<Result<Unit>> StopRuntimeAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(Unit.Instance));

        public void TerminateBroker()
        {
            TerminateCallCount++;
        }
    }

    private sealed class FrozenTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Now =
            new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override long GetTimestamp() => 0;
    }
}
