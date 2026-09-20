using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Broker.Transport;

namespace Zapret2Pilot.Broker.Tests.Transport;

public sealed class WindowsBrokerPeerIdentityResolverTests
{
    [Fact]
    public static async Task ResolverUsesActualConnectedPipeClientAndReturnsRetainedLease()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string pipeName = $"z2p-peer-test-{Guid.NewGuid():N}";

        await using NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await using NamedPipeClientStream client = new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Task serverConnect = server.WaitForConnectionAsync(
            TestContext.Current.CancellationToken);
        await client.ConnectAsync(
            5_000,
            TestContext.Current.CancellationToken);
        await serverConnect;

        WindowsBrokerPeerIdentityResolver resolver = new();
        BrokerResolvedPeer resolved = resolver.Resolve(server.SafePipeHandle);

        using (resolved.ProcessLease)
        {
            Assert.Equal(Environment.ProcessId, resolved.Identity.ProcessId);
            Assert.NotEqual(0, resolved.Identity.ProcessCreationTimeFileTime);
            Assert.False(string.IsNullOrWhiteSpace(resolved.Identity.UserSid));
            Assert.StartsWith("S-", resolved.Identity.UserSid, StringComparison.Ordinal);
            Assert.True(resolved.Identity.IntegrityLevelRid > 0);
        }
    }
}
