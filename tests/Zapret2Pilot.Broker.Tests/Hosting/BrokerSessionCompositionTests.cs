using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Broker.Transport;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Security;

namespace Zapret2Pilot.Broker.Tests.Hosting;

public sealed class BrokerSessionCompositionTests
{
    [Fact]
    public static void SessionBootstrapComposesConcreteAuthenticatedTransportOnce()
    {
        byte[] secret = Enumerable.Range(
                1,
                BrokerAuthenticator.SecretSizeBytes)
            .Select(static value => checked((byte)value))
            .ToArray();

        BrokerClientBinding binding = CreateBinding();
        using BrokerSessionBootstrap bootstrap = new(
            binding,
            "z2p-broker-test-session",
            secret);

        HostApplicationBuilder builder =
            BrokerHostBuilder.CreateBuilder([], bootstrap);

        Assert.Single(
            builder.Services,
            static descriptor =>
                descriptor.ServiceType == typeof(BrokerSessionBootstrap));
        Assert.Single(
            builder.Services,
            static descriptor =>
                descriptor.ServiceType == typeof(BrokerPipeServerOptions));
        Assert.Single(
            builder.Services,
            static descriptor =>
                descriptor.ServiceType == typeof(IBrokerNamedPipeFactory));
        Assert.Single(
            builder.Services,
            static descriptor =>
                descriptor.ServiceType == typeof(IBrokerPeerIdentityResolver));
        Assert.Single(
            builder.Services,
            static descriptor =>
                descriptor.ServiceType == typeof(BrokerAuthenticatedSession));
        Assert.Single(
            builder.Services,
            static descriptor =>
                descriptor.ServiceType == typeof(WindowsBrokerPipeServer));

        // RuntimeHealthMonitor + RuntimeSupervisor + App-session lifetime
        // watcher + concrete authenticated pipe server.
        Assert.Equal(
            4,
            builder.Services.Count(
                static descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)));

        using IHost host = builder.Build();

        BrokerAuthenticatedSession first =
            host.Services.GetRequiredService<BrokerAuthenticatedSession>();
        BrokerAuthenticatedSession second =
            host.Services.GetRequiredService<BrokerAuthenticatedSession>();
        WindowsBrokerPipeServer server =
            host.Services.GetRequiredService<WindowsBrokerPipeServer>();

        Assert.Same(first, second);
        Assert.NotNull(server);
        Assert.False(
            secret.All(static value => value == 0),
            "The live authenticated session must still own the bootstrap secret before host disposal.");
    }

    [Fact]
    public static void HostDisposalZerosTransferredBootstrapSecret()
    {
        byte[] secret = Enumerable.Repeat(
                (byte)0xA5,
                BrokerAuthenticator.SecretSizeBytes)
            .ToArray();

        using BrokerSessionBootstrap bootstrap = new(
            CreateBinding(),
            "z2p-broker-secret-zero-test",
            secret);

        HostApplicationBuilder builder =
            BrokerHostBuilder.CreateBuilder([], bootstrap);

        using (IHost host = builder.Build())
        {
            _ = host.Services.GetRequiredService<BrokerAuthenticatedSession>();
        }

        Assert.All(
            secret,
            static value => Assert.Equal((byte)0, value));
    }

    private static BrokerClientBinding CreateBinding()
        => new(
            AppSessionId.New(),
            ProcessId: 4242,
            ProcessCreationTimeFileTime: 638940000000000000,
            WindowsSessionId: 1,
            UserSid: "S-1-5-18",
            LogonSessionId: new LogonSessionId(11, 22),
            IntegrityLevelRid: 0x2000);
}
