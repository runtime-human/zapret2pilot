using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Runtime.State;

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
}
