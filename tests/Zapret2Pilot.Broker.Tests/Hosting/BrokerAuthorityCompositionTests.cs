using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Supervisor;
using Zapret2Pilot.Runtime.State;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.Broker.Tests.Hosting;

public sealed class BrokerAuthorityCompositionTests
{
    [Fact]
    public static void BrokerCompositionOwnsExactlyOneRuntimeMutationAuthorityGraph()
    {
        HostApplicationBuilder builder = BrokerHostBuilder.CreateBuilder([]);

        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(RuntimeKernelLoop));
        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(RuntimeProcessHost));
        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(IRuntimeProcessHost));
        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(RuntimeSupervisor));
        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(IRuntimeSupervisor));
        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(IRuntimeKernelStateStore));

        Assert.DoesNotContain(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(SqliteStorageOptions));
        Assert.DoesNotContain(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(SqliteConnectionFactory));
        Assert.DoesNotContain(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(SqliteDbInitializer));

        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(BrokerSessionLifetimeService));
        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(IBrokerAppSessionLease));
        Assert.Single(
            builder.Services,
            static descriptor => descriptor.ServiceType == typeof(IBrokerAppSessionLeaseBinder));

        // RuntimeHealthMonitor, RuntimeSupervisor and the App-session lifetime
        // watcher are hosted. Only RuntimeSupervisor/Kernel can mutate runtime;
        // the lifetime watcher can request terminal cleanup through that same
        // authority but cannot transition lifecycle state itself.
        Assert.Equal(
            3,
            builder.Services.Count(
                static descriptor => descriptor.ServiceType == typeof(IHostedService)));
    }
}
