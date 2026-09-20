using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Broker.Transport;

namespace Zapret2Pilot.Broker.Tests.Transport;

public sealed class WindowsSecureNamedPipeFactoryTests
{
    [Fact]
    public static async Task SecureFactoryAcceptsLocalExpectedUserAndSupportsActualPeerResolution()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Current Windows identity has no user SID.");
        string pipeName = $"z2p-secure-{Guid.NewGuid():N}";

        WindowsSecureNamedPipeFactory factory = new();
        await using NamedPipeServerStream server =
            factory.Create(pipeName, userSid, firstInstance: true);
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
        BrokerResolvedPeer peer = resolver.Resolve(server.SafePipeHandle);
        using (peer.ProcessLease)
        {
            Assert.Equal(Environment.ProcessId, peer.Identity.ProcessId);
            Assert.Equal(userSid, peer.Identity.UserSid);
        }
    }

    [Fact]
    public static void FirstInstanceFlagRejectsPreexistingPipeName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Current Windows identity has no user SID.");
        string pipeName = $"z2p-squat-{Guid.NewGuid():N}";

        using NamedPipeServerStream squatter = new(
            pipeName,
            PipeDirection.InOut,
            2,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        WindowsSecureNamedPipeFactory factory = new();

        Assert.Throws<Win32Exception>(
            () => factory.Create(pipeName, userSid, firstInstance: true));
    }

    [Theory]
    [InlineData("bad\\name")]
    [InlineData("bad/name")]
    [InlineData("bad:name")]
    [InlineData("bad name")]
    public static void PipeNameRejectsUnexpectedNamespaceCharacters(string name)
    {
        WindowsSecureNamedPipeFactory factory = new();

        Assert.Throws<ArgumentException>(
            () => factory.Create(
                name,
                "S-1-5-18",
                firstInstance: true));
    }
}
