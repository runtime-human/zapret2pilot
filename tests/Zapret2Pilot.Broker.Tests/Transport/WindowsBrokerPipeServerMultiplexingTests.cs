using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using Xunit;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Broker.Transport;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;
using ContractGeneration = Zapret2Pilot.Contracts.Identity.RuntimeGeneration;

namespace Zapret2Pilot.Broker.Tests.Transport;

public sealed class WindowsBrokerPipeServerMultiplexingTests
{
    [Fact]
    public static async Task StopCanBeDispatchedAndAnsweredWhileStartIsStillInFlight()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string pipeName =
            $"z2p-multiplex-{Guid.NewGuid():N}";
        AppSessionId appSessionId = AppSessionId.New();
        BrokerSessionId brokerSessionId = BrokerSessionId.New();

        BrokerPeerIdentity peer = new(
            ProcessId: Environment.ProcessId,
            ProcessCreationTimeFileTime: 123456789,
            WindowsSessionId: 1,
            UserSid: "S-1-5-21-1000",
            LogonSessionId: new LogonSessionId(7, 8),
            IntegrityLevelRid: 0x2000);

        using FakeAuthenticatedSession session = new(
            appSessionId,
            brokerSessionId);
        FakeLeaseBinder leaseBinder = new();

        using WindowsBrokerPipeServer server = new(
            new BrokerPipeServerOptions(
                pipeName,
                peer.UserSid),
            new TestPipeFactory(),
            new FakePeerResolver(peer),
            session,
            leaseBinder,
            NullLogger<WindowsBrokerPipeServer>.Instance);

        await server.StartAsync(
            TestContext.Current.CancellationToken);

        await using NamedPipeClientStream client = new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(
            5_000,
            TestContext.Current.CancellationToken);

        byte[] challengeFrame = await ReadFrameAsync(
            client,
            BrokerProtocolLimits.MaxChallengeFrameBytes,
            TestContext.Current.CancellationToken);

        BrokerChallengeFrameDecodeResult challenge =
            BrokerChallengeFrameCodec.Decode(challengeFrame);
        Assert.Equal(
            BrokerChallengeFrameDecodeStatus.Success,
            challenge.Status);

        await WriteRequestAsync(
            client,
            CreateHello(
                appSessionId,
                brokerSessionId,
                sequence: 1),
            TestContext.Current.CancellationToken);

        BrokerOperationId startOperation =
            BrokerOperationId.New();
        BrokerRequestEnvelope start = CreateRequest(
            appSessionId,
            brokerSessionId,
            startOperation,
            sequence: 2,
            new StartPreparedPlanRequest(
                PreparedPlanId.New(),
                new ContractGeneration(1)));

        await WriteRequestAsync(
            client,
            start,
            TestContext.Current.CancellationToken);

        await session.StartEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        BrokerOperationId stopOperation =
            BrokerOperationId.New();
        BrokerRequestEnvelope stop = CreateRequest(
            appSessionId,
            brokerSessionId,
            stopOperation,
            sequence: 3,
            new StopGenerationRequest(
                new ContractGeneration(1),
                BrokerStopReason.UserRequested));

        await WriteRequestAsync(
            client,
            stop,
            TestContext.Current.CancellationToken);

        await session.StopEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        BrokerResponseEnvelope stopResponse =
            await ReadResponseAsync(
                client,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            stopOperation,
            stopResponse.OperationId);
        Assert.Equal(
            BrokerResponseStatus.Accepted,
            stopResponse.Status);
        Assert.False(
            session.StartReleased.Task.IsCompleted,
            "Start must still be in flight when Stop response is delivered.");

        session.ReleaseStart();

        BrokerResponseEnvelope startResponse =
            await ReadResponseAsync(
                client,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            startOperation,
            startResponse.OperationId);
        Assert.Equal(
            BrokerResponseStatus.Rejected,
            startResponse.Status);

        await client.DisposeAsync();

        using CancellationTokenSource stopBudget =
            new(TimeSpan.FromSeconds(5));
        await server.StopAsync(stopBudget.Token);
    }

    private static BrokerRequestEnvelope CreateHello(
        AppSessionId appSessionId,
        BrokerSessionId brokerSessionId,
        long sequence)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new(
            BrokerProtocolVersion.V1,
            appSessionId,
            brokerSessionId,
            BrokerOperationId.New(),
            new RequestSequence(sequence),
            now,
            now + BrokerProtocolLimits.MaxRequestLifetime,
            new BrokerHelloRequest(
                BrokerProtocolRange.Current,
                appSessionId,
                RandomNumberGenerator.GetBytes(
                    BrokerAuthenticator.NonceSizeBytes),
                RandomNumberGenerator.GetBytes(
                    BrokerAuthenticator.ProofSizeBytes)));
    }

    private static BrokerRequestEnvelope CreateRequest(
        AppSessionId appSessionId,
        BrokerSessionId brokerSessionId,
        BrokerOperationId operationId,
        long sequence,
        IBrokerRequest request)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new(
            BrokerProtocolVersion.V1,
            appSessionId,
            brokerSessionId,
            operationId,
            new RequestSequence(sequence),
            now,
            now + BrokerProtocolLimits.MaxRequestLifetime,
            request);
    }

    private static async Task WriteRequestAsync(
        Stream stream,
        BrokerRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        byte[] payload =
            BrokerProtocolCodec.EncodeRequest(request);
        byte[] frame = BrokerFrameCodec.Encode(payload);

        await stream.WriteAsync(
            frame,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<BrokerResponseEnvelope> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] frame = await ReadFrameAsync(
            stream,
            BrokerProtocolLimits.MaxFrameBytes,
            cancellationToken);

        BrokerFrameDecodeResult framing =
            BrokerFrameCodec.Decode(frame);
        Assert.Equal(
            BrokerFrameDecodeStatus.Success,
            framing.Status);
        Assert.NotNull(framing.Payload);

        BrokerResponseDecodeResult decoded =
            BrokerResponseProtocolCodec.DecodeResponse(
                framing.Payload!);

        Assert.Equal(
            BrokerProtocolDecodeStatus.Success,
            decoded.Status);
        return Assert.IsType<BrokerResponseEnvelope>(
            decoded.Envelope);
    }

    private static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        byte[] header =
            new byte[BrokerProtocolLimits.LengthPrefixBytes];

        await ReadExactlyAsync(
            stream,
            header,
            cancellationToken);

        int length =
            BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert.InRange(
            length,
            1,
            maximumPayloadBytes);

        byte[] payload = new byte[length];
        await ReadExactlyAsync(
            stream,
            payload,
            cancellationToken);

        byte[] frame =
            new byte[header.Length + payload.Length];
        header.CopyTo(frame, 0);
        payload.CopyTo(frame, header.Length);
        return frame;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer[offset..],
                cancellationToken);

            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private sealed class TestPipeFactory :
        IBrokerNamedPipeFactory
    {
        public NamedPipeServerStream Create(
            string name,
            string expectedUserSid,
            bool firstInstance)
        {
            PipeOptions options =
                PipeOptions.Asynchronous;

            if (firstInstance)
            {
                options |= PipeOptions.FirstPipeInstance;
            }

            return new NamedPipeServerStream(
                name,
                PipeDirection.InOut,
                BrokerProtocolLimits.MaxConcurrentConnections,
                PipeTransmissionMode.Byte,
                options);
        }
    }

    private sealed class FakePeerResolver :
        IBrokerPeerIdentityResolver
    {
        private readonly BrokerPeerIdentity identity;

        public FakePeerResolver(
            BrokerPeerIdentity identity)
        {
            this.identity = identity;
        }

        public BrokerResolvedPeer Resolve(
            SafePipeHandle pipeHandle)
            => new(
                identity,
                new FakeLease());

        public BrokerResolvedPeer ResolveServer(
            SafePipeHandle pipeHandle)
            => throw new NotSupportedException();
    }

    private sealed class FakeLeaseBinder :
        IBrokerAppSessionLeaseBinder
    {
        public bool TryBind(
            IBrokerAppSessionLease lease)
        {
            lease.Dispose();
            return true;
        }
    }

    private sealed class FakeLease :
        IBrokerAppSessionLease
    {
        public Task WaitForExitAsync(
            CancellationToken cancellationToken)
            => Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class FakeAuthenticatedSession :
        IBrokerAuthenticatedSession
    {
        private readonly AppSessionId appSessionId;

        public FakeAuthenticatedSession(
            AppSessionId appSessionId,
            BrokerSessionId brokerSessionId)
        {
            this.appSessionId = appSessionId;
            BrokerSessionId = brokerSessionId;
        }

        public BrokerSessionId BrokerSessionId { get; }

        public bool IsAuthenticated { get; private set; }

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> StartReleased { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BrokerChallengeIssueResult TryIssueChallenge(
            BrokerPeerIdentity peer)
        {
            BrokerChallengeHandle handle =
                BrokerChallengeHandle.New();

            return new(
                BrokerChallengeIssueStatus.Issued,
                new BrokerChallengeMessage(
                    BrokerProtocolVersion.V1,
                    BrokerProtocolRange.Current,
                    BrokerSessionId,
                    RandomNumberGenerator.GetBytes(
                        BrokerAuthenticator.NonceSizeBytes),
                    checked((int)BrokerProtocolLimits.HandshakeTimeout.TotalMilliseconds)),
                handle,
                RetryAfter: null);
        }

        public void AbandonChallenge(
            BrokerChallengeHandle challengeHandle)
        {
        }

        public BrokerAdmissionDecision TryAuthenticate(
            BrokerChallengeHandle challengeHandle,
            BrokerPeerIdentity peer,
            BrokerRequestEnvelope helloEnvelope)
        {
            Assert.Equal(
                appSessionId,
                helloEnvelope.AppSessionId);
            Assert.Equal(
                BrokerSessionId,
                helloEnvelope.BrokerSessionId);

            IsAuthenticated = true;
            return new(
                true,
                BrokerAdmissionRejectionReason.None,
                appSessionId,
                BrokerSessionId,
                BrokerProtocolVersion.V1);
        }

        public void ReleaseAuthenticatedConnection()
        {
            IsAuthenticated = false;
        }

        public async Task<BrokerResponseEnvelope> DispatchAsync(
            BrokerRequestEnvelope request,
            CancellationToken cancellationToken = default)
        {
            if (request.Request is StartPreparedPlanRequest)
            {
                StartEntered.TrySetResult(true);

                await StartReleased.Task.WaitAsync(
                    cancellationToken);

                return Error(
                    request,
                    "RuntimeSupervisorStartSupersededByStop");
            }

            if (request.Request is StopGenerationRequest)
            {
                StopEntered.TrySetResult(true);
                return Accepted(request);
            }

            throw new InvalidOperationException(
                "Unexpected request in multiplexing test.");
        }

        public void OnResponseFlushed(
            BrokerRequestEnvelope request,
            BrokerResponseEnvelope response)
        {
        }

        public void ReleaseStart()
        {
            StartReleased.TrySetResult(true);
        }

        public void Dispose()
        {
            StartReleased.TrySetCanceled();
        }

        private static BrokerResponseEnvelope Accepted(
            BrokerRequestEnvelope request)
            => new(
                request.Protocol,
                request.AppSessionId,
                request.BrokerSessionId,
                request.OperationId,
                BrokerResponseStatus.Accepted,
                new BrokerMutationAcceptedResponse(
                    request.OperationId,
                    new ContractGeneration(1)));

        private static BrokerResponseEnvelope Error(
            BrokerRequestEnvelope request,
            string code)
            => new(
                request.Protocol,
                request.AppSessionId,
                request.BrokerSessionId,
                request.OperationId,
                BrokerResponseStatus.Rejected,
                new BrokerErrorResponse(
                    code,
                    "Simulated superseded start."));
    }
}
