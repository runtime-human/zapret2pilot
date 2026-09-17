using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Zapret2Pilot.Broker.Hosting;

public static class BrokerHostBuilder
{
    public static IHost Build(string[] args)
    {
        return CreateBuilder(args).Build();
    }

    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // #17 keeps the Broker configuration surface deliberately closed.
        // Bootstrap/session material is introduced by the bounded broker
        // transport later in this issue and must never become ordinary
        // environment-variable or arbitrary CLI configuration.
        builder.Configuration.Sources.Clear();

        string commonApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonApplicationData))
        {
            throw new InvalidOperationException("Common application data directory is not available.");
        }

        string databasePath = Path.Combine(
            commonApplicationData,
            "Zapret2Pilot",
            "z2p.db");

        // v7-D: these services form the sole production runtime mutation
        // authority. The unelevated App must never register this graph.
        builder.Services.AddRuntimeKernelStateStore(databasePath);
        builder.Services.AddRuntimeProcessHost();
        builder.Services.AddRuntimeHealthMonitor();
        builder.Services.AddCrashLoopGuard();
        builder.Services.AddRuntimeKernelLoop();
        builder.Services.AddRuntimeSupervisor();

        return builder;
    }
}
