using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Supervisor;

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

        // RuntimeHealthMonitor and RuntimeSupervisor are the two hosted
        // runtime services. Their factories resolve the corresponding
        // singleton registrations; no duplicate hosted authority is added.
        Assert.Equal(
            2,
            builder.Services.Count(
                static descriptor => descriptor.ServiceType == typeof(IHostedService)));
    }
}
