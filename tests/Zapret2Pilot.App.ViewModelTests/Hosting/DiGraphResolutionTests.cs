using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.App.Hosting;

namespace Zapret2Pilot.App.ViewModelTests.Hosting;

public sealed class DiGraphResolutionTests
{
    [Fact]
    public static void AllSingletonServicesResolve()
    {
        HostApplicationBuilder builder = Z2PHostBuilder.CreateBuilder([]);
        using IHost host = builder.Build();

        // Filter to singletons that can actually be resolved without an
        // Avalonia application context. Window types (e.g. MainWindow)
        // require a windowing platform and an IWindowingPlatform
        // implementation, so we exclude them here. The production code
        // path resolves MainWindow inside Program.Main, after Avalonia
        // has initialized, where the platform is available.
        IEnumerable<ServiceDescriptor> singletons = builder.Services
            .Where(sd => sd.Lifetime == ServiceLifetime.Singleton &&
                         sd.ImplementationType is not null &&
                         !IsOpenGeneric(sd.ServiceType) &&
                         !RequiresAvaloniaPlatform(sd.ImplementationType));

        Assert.All(singletons, descriptor =>
        {
            _ = host.Services.GetRequiredService(descriptor.ServiceType);
        });
    }

    private static bool IsOpenGeneric(Type type) => type.IsGenericTypeDefinition;

    private static bool RequiresAvaloniaPlatform(Type type) => typeof(Window).IsAssignableFrom(type);
}
