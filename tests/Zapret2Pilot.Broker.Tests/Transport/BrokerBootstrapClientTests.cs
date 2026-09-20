using System;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Broker.Transport;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Security;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Broker.Tests.Transport;

public sealed class BrokerBootstrapClientTests
{
    [Fact]
    public static async Task BootstrapClientAcceptsOnlyActualPipeServerBinding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string bootstrapPipe =
            $"z2p-bootstrap-test-{Guid.NewGuid():N}";
        string runtimePipe =
            $"z2p-runtime-test-{Guid.NewGuid():N}";

        await using NamedPipeServerStream server = new(
            bootstrapPipe,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
                | PipeOptions.FirstPipeInstance);

        WindowsBrokerPeerIdentityResolver resolver = new();
        BrokerBootstrapClient client = new(resolver);

        Task<BrokerStartupContext> clientTask =
            client.ReceiveAsync(
                new BrokerBootstrapLaunchOptions(
                    bootstrapPipe,
                    Environment.ProcessId),
                TestContext.Current.CancellationToken);

        await server.WaitForConnectionAsync(
            TestContext.Current.CancellationToken);

        // In this integration test server and client execute in the same test
        // process. Resolve() therefore yields the exact identity that
        // ResolveServer() must independently observe on the client side.
        BrokerResolvedPeer resolvedClient =
            resolver.Resolve(server.SafePipeHandle);

        using (resolvedClient.ProcessLease)
        {
            AppSessionId appSessionId = AppSessionId.New();
            BrokerPeerIdentity peer = resolvedClient.Identity;
            BrokerClientBinding binding = new(
                appSessionId,
                peer.ProcessId,
                peer.ProcessCreationTimeFileTime,
                peer.WindowsSessionId,
                peer.UserSid,
                peer.LogonSessionId,
                peer.IntegrityLevelRid);

            byte[] secret = RandomNumberGenerator.GetBytes(
                BrokerAuthenticator.SecretSizeBytes);
            BrokerBootstrapMessage bootstrap = new(
                BrokerProtocolVersion.V1,
                appSessionId,
                runtimePipe,
                binding,
                secret);

            byte[] frame =
                BrokerBootstrapFrameCodec.Encode(bootstrap);
            try
            {
                await server.WriteAsync(
                    frame,
                    TestContext.Current.CancellationToken);
                await server.FlushAsync(
                    TestContext.Current.CancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(frame);
                CryptographicOperations.ZeroMemory(secret);
            }

            using BrokerStartupContext context =
                await clientTask.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                binding,
                context.SessionBootstrap.ClientBinding);
            Assert.Equal(
                runtimePipe,
                context.SessionBootstrap.PipeName);
        }
    }

    [Fact]
    public static async Task BootstrapClientRejectsCommandLinePidThatIsNotActualServer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string bootstrapPipe =
            $"z2p-bootstrap-test-{Guid.NewGuid():N}";

        await using NamedPipeServerStream server = new(
            bootstrapPipe,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
                | PipeOptions.FirstPipeInstance);

        BrokerBootstrapClient client = new(
            new WindowsBrokerPeerIdentityResolver());

        Task<BrokerStartupContext> clientTask =
            client.ReceiveAsync(
                new BrokerBootstrapLaunchOptions(
                    bootstrapPipe,
                    int.MaxValue),
                TestContext.Current.CancellationToken);

        await server.WaitForConnectionAsync(
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await clientTask);
    }
}
