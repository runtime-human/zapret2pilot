using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using Xunit;
using Zapret2Pilot.App.Runtime;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.App.Tests.Runtime;

public sealed class NamedPipeRuntimeBrokerSessionTests
{
    [Fact]
    public static async Task StopCanCompleteBeforeEarlierStartResponse()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string pipeName = $"z2p-app-multiplex-{Guid.NewGuid():N}";
        AppSessionId appSessionId = AppSessionId.New();
        BrokerSessionId brokerSessionId = BrokerSessionId.New();
        byte[] secret = RandomNumberGenerator.GetBytes(
            BrokerAuthenticator.SecretSizeBytes);

        BrokerClientBinding binding = new(
            appSessionId,
            Environment.ProcessId,
            ProcessCreationTimeFileTime: 123456789,
            WindowsSessionId: 1,
            UserSid: "S-1-5-21-1000",
            LogonSessionId: new LogonSessionId(7, 8),
            IntegrityLevelRid: 0x2000);

        TaskCompletionSource<bool> startReceived = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));

        Task serverTask = RunScriptedBrokerAsync(
            pipeName,
            appSessionId,
            brokerSessionId,
            startReceived,
            budget.Token);

        await using NamedPipeRuntimeBrokerSession session = new(
            binding,
            pipeName,
            secret);

        try
        {
            await session.ConnectAsync(budget.Token);

            Task<BrokerRuntimeSnapshot> startTask =
                session.StartPreparedPlanAsync(
                    PreparedPlanId.New(),
                    new RuntimeGeneration(1),
                    budget.Token);

            await startReceived.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                budget.Token);

            Task<BrokerRuntimeSnapshot> stopTask =
                session.StopGenerationAsync(
                    new RuntimeGeneration(1),
                    BrokerStopReason.UserRequested,
                    budget.Token);

            BrokerRuntimeSnapshot stopped =
                await stopTask.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    budget.Token);

            Assert.Equal(BrokerRuntimeState.Stopped, stopped.State);
            Assert.Equal(2, stopped.Generation.Value);

            InvalidOperationException superseded =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await startTask.ConfigureAwait(false));
            Assert.Contains(
                "RuntimeSupervisorStartSupersededByStop",
                superseded.Message,
                StringComparison.Ordinal);

            await serverTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                budget.Token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static async Task RunScriptedBrokerAsync(
        string pipeName,
        AppSessionId appSessionId,
        BrokerSessionId brokerSessionId,
        TaskCompletionSource<bool> startReceived,
        CancellationToken cancellationToken)
    {
        await using NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(cancellationToken);

        BrokerChallengeMessage challenge = new(
            BrokerProtocolVersion.V1,
            BrokerProtocolRange.Current,
            brokerSessionId,
            RandomNumberGenerator.GetBytes(
                BrokerAuthenticator.NonceSizeBytes),
            checked((int)BrokerProtocolLimits.HandshakeTimeout.TotalMilliseconds));

        byte[] challengeFrame =
            BrokerChallengeFrameCodec.Encode(challenge);
        await server.WriteAsync(challengeFrame, cancellationToken);
        await server.FlushAsync(cancellationToken);

        BrokerRequestEnvelope hello =
            await ReadRequestAsync(server, cancellationToken);
        Assert.IsType<BrokerHelloRequest>(hello.Request);
        Assert.Equal(appSessionId, hello.AppSessionId);
        Assert.Equal(brokerSessionId, hello.BrokerSessionId);

        BrokerRequestEnvelope start =
            await ReadRequestAsync(server, cancellationToken);
        Assert.IsType<StartPreparedPlanRequest>(start.Request);
        startReceived.TrySetResult(true);

        // The key assertion is implicit in this bounded read: the Control
        // Plane must put Stop on the wire before Start receives any response.
        BrokerRequestEnvelope stop =
            await ReadRequestAsync(server, cancellationToken);
        Assert.IsType<StopGenerationRequest>(stop.Request);

        await WriteResponseAsync(
            server,
            Accepted(stop, generation: 2),
            cancellationToken);

        await WriteResponseAsync(
            server,
            Rejected(
                start,
                "RuntimeSupervisorStartSupersededByStop"),
            cancellationToken);

        BrokerRequestEnvelope snapshotRequest =
            await ReadRequestAsync(server, cancellationToken);
        Assert.IsType<GetRuntimeSnapshotRequest>(
            snapshotRequest.Request);

        await WriteResponseAsync(
            server,
            new BrokerResponseEnvelope(
                snapshotRequest.Protocol,
                snapshotRequest.AppSessionId,
                snapshotRequest.BrokerSessionId,
                snapshotRequest.OperationId,
                BrokerResponseStatus.Ok,
                new BrokerRuntimeSnapshotResponse(
                    new BrokerRuntimeSnapshot(
                        new RuntimeGeneration(2),
                        BrokerRuntimeState.Stopped,
                        ActivePlanId: null,
                        ActiveOperationId: null))),
            cancellationToken);
    }

    private static BrokerResponseEnvelope Accepted(
        BrokerRequestEnvelope request,
        long generation)
        => new(
            request.Protocol,
            request.AppSessionId,
            request.BrokerSessionId,
            request.OperationId,
            BrokerResponseStatus.Accepted,
            new BrokerMutationAcceptedResponse(
                request.OperationId,
                new RuntimeGeneration(generation)));

    private static BrokerResponseEnvelope Rejected(
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

    private static async Task<BrokerRequestEnvelope> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] payload = await ReadPayloadAsync(
            stream,
            BrokerProtocolLimits.MaxFrameBytes,
            cancellationToken);

        BrokerProtocolDecodeResult decoded =
            BrokerProtocolCodec.DecodeRequest(payload);
        Assert.Equal(
            BrokerProtocolDecodeStatus.Success,
            decoded.Status);
        return Assert.IsType<BrokerRequestEnvelope>(
            decoded.Envelope);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        BrokerResponseEnvelope response,
        CancellationToken cancellationToken)
    {
        byte[] payload =
            BrokerResponseProtocolCodec.EncodeResponse(response);
        byte[] frame = BrokerFrameCodec.Encode(payload);

        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadPayloadAsync(
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
        Assert.InRange(length, 1, maximumPayloadBytes);

        byte[] payload = new byte[length];
        await ReadExactlyAsync(
            stream,
            payload,
            cancellationToken);
        return payload;
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
}
