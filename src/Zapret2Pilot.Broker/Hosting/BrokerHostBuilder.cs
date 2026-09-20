using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Broker.Transport;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Runtime.State;

namespace Zapret2Pilot.Broker.Hosting;

public static class BrokerHostBuilder
{
    public static IHost Build(string[] args)
    {
        return CreateBuilder(args).Build();
    }

    public static IHost Build(
        string[] args,
        BrokerSessionBootstrap bootstrap)
    {
        return CreateBuilder(args, bootstrap).Build();
    }

    public static IHost Build(
        string[] args,
        BrokerStartupContext startupContext)
    {
        return CreateBuilder(args, startupContext).Build();
    }

    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // #17 keeps the Broker configuration surface deliberately closed.
        // Bootstrap/session material must never become ordinary environment-
        // variable, appsettings, or arbitrary CLI configuration.
        builder.Configuration.Sources.Clear();

        // v7-D / #17: the Broker must not become an elevated owner of the
        // Control Plane's general z2p.db. The lifecycle proof uses a
        // process-lifetime state store. A durable Broker recovery store may
        // be added only if later correctness work proves that it is required.
        builder.Services.AddSingleton<IRuntimeKernelStateStore, SessionRuntimeKernelStateStore>();
        builder.Services.AddRuntimeProcessHost();
        builder.Services.AddRuntimeHealthMonitor();
        builder.Services.AddCrashLoopGuard();
        builder.Services.AddRuntimeKernelLoop();
        builder.Services.AddRuntimeSupervisor();

        // #17 Task 4: broker protocol admission is a façade over the
        // existing kernel/supervisor authority, never a second state machine.
        builder.Services.AddSingleton<IBrokerRuntimeStateProjection, RuntimeKernelStateProjection>();
        builder.Services.AddSingleton<IPreparedRuntimePlanResolver, RejectingPreparedRuntimePlanResolver>();
        builder.Services.AddSingleton(static _ => new BrokerOperationLedger(
            BrokerProtocolLimits.OperationLedgerCapacity,
            BrokerProtocolLimits.OperationLedgerTtl));
        builder.Services.AddSingleton(static _ => new BrokerConcurrencyGate(
            BrokerProtocolLimits.MaxInFlightQueries,
            BrokerProtocolLimits.MaxConcurrentMutations));
        builder.Services.AddSingleton<IBrokerLifetimeController, BrokerLifetimeController>();

        builder.Services.AddSingleton<BrokerAppSessionLeaseHolder>();
        builder.Services.AddSingleton<IBrokerAppSessionLease>(
            static sp => sp.GetRequiredService<BrokerAppSessionLeaseHolder>());
        builder.Services.AddSingleton<IBrokerAppSessionLeaseBinder>(
            static sp => sp.GetRequiredService<BrokerAppSessionLeaseHolder>());
        builder.Services.AddSingleton<BrokerSessionLifetimeService>();
        builder.Services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<BrokerSessionLifetimeService>());

        builder.Services.AddSingleton<BrokerRuntimeDispatcher>();
        builder.Services.AddSingleton<IBrokerRequestDispatcher>(
            static sp => sp.GetRequiredService<BrokerRuntimeDispatcher>());

        return builder;
    }

    public static HostApplicationBuilder CreateBuilder(
        string[] args,
        BrokerStartupContext startupContext)
    {
        ArgumentNullException.ThrowIfNull(startupContext);

        HostApplicationBuilder builder = CreateBuilder(
            args,
            startupContext.SessionBootstrap);

        BrokerAppSessionLeaseHolder holder = new();
        if (!holder.TryBind(startupContext.TakeAppProcessLease()))
        {
            holder.Dispose();
            throw new InvalidOperationException(
                "The verified App process lease could not be bound to the Broker session.");
        }

        builder.Services.Replace(
            ServiceDescriptor.Singleton(holder));

        return builder;
    }

    /// <summary>
    /// Creates the Broker host with the concrete local authenticated transport
    /// for one already-established AppSession bootstrap.
    ///
    /// The caller owns how the bootstrap is transferred across the elevation
    /// boundary. This overload intentionally accepts the material in-memory so
    /// the secret cannot accidentally become a CLI/environment configuration
    /// source. The one-shot cross-process bootstrap transport is composed
    /// separately.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(
        string[] args,
        BrokerSessionBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        HostApplicationBuilder builder = CreateBuilder(args);
        AddSessionTransport(builder.Services, bootstrap);
        return builder;
    }

    private static void AddSessionTransport(
        IServiceCollection services,
        BrokerSessionBootstrap bootstrap)
    {
        services.AddSingleton(bootstrap);
        services.AddSingleton(new BrokerPipeServerOptions(
            bootstrap.PipeName,
            bootstrap.ClientBinding.UserSid));

        services.AddSingleton<IBrokerNamedPipeFactory, WindowsSecureNamedPipeFactory>();
        services.AddSingleton<IBrokerPeerIdentityResolver, WindowsBrokerPeerIdentityResolver>();

        services.AddSingleton(static sp =>
        {
            BrokerSessionBootstrap material =
                sp.GetRequiredService<BrokerSessionBootstrap>();

            return new BrokerAuthenticatedSession(
                material.ClientBinding,
                material.TakeBootstrapSecret(),
                sp.GetRequiredService<IBrokerRequestDispatcher>(),
                sp.GetRequiredService<IBrokerLifetimeController>());
        });
        services.AddSingleton<IBrokerAuthenticatedSession>(
            static sp => sp.GetRequiredService<BrokerAuthenticatedSession>());

        services.AddSingleton<WindowsBrokerPipeServer>();
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<WindowsBrokerPipeServer>());
    }
}
