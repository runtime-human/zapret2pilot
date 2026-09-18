using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.App.Hosting;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.App.ViewModelTests.Hosting;

public sealed class RuntimeAuthorityBoundaryTests
{
    [Fact]
    public static void AppCompositionDoesNotRegisterRuntimeMutationAuthority()
    {
        HostApplicationBuilder builder = Z2PHostBuilder.CreateBuilder([]);

        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(RuntimeKernelLoop));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(RuntimeProcessHost));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(IRuntimeProcessHost));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(RuntimeSupervisor));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(IRuntimeSupervisor));
        Assert.DoesNotContain(builder.Services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(RuntimeSupervisor));
    }
}
