using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.App.Hosting;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.State;
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
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(IRuntimeKernelStateStore));
        Assert.DoesNotContain(builder.Services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(RuntimeSupervisor));
    }

    [Fact]
    public static void AppProjectDoesNotReferenceRuntimeImplementation()
    {
        string repositoryRoot = FindRepositoryRoot();
        string project = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "Zapret2Pilot.App",
                "Zapret2Pilot.App.csproj"));

        Assert.DoesNotContain(
            "Zapret2Pilot.Runtime\\Zapret2Pilot.Runtime.csproj",
            project,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Zapret2Pilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test output directory.");
    }
}
