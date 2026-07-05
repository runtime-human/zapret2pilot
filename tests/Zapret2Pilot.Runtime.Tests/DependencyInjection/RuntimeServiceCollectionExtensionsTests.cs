using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.State;
using Zapret2Pilot.Runtime.Supervisor;

namespace Zapret2Pilot.Runtime.Tests.DependencyInjection;

public sealed class RuntimeServiceCollectionExtensionsTests
{
    [Fact]
    public static void AddRuntimeKernelStateStoreRegistersResolvableStore()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.DirectoryPath, "z2p.db");

        {
            using IHost host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddRuntimeKernelStateStore(databasePath))
                .Build();

            IRuntimeKernelStateStore store = host.Services.GetRequiredService<IRuntimeKernelStateStore>();

            RuntimeSessionRecord session = store.StartSession(
                new ProfileId("profile-a"),
                new RuntimePlanId("plan-a"),
                new RuntimePlanCacheKey("a".PadRight(64, 'a')));

            Assert.NotNull(session);
            Assert.Equal(RuntimeSessionState.Active, session.State);

            RuntimeSessionRecord? current = store.GetCurrentSession();
            Assert.NotNull(current);
            Assert.Equal(session.Id, current!.Id);

            Assert.True(File.Exists(databasePath), "SQLite database file should be created.");
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public static async Task AddRuntimeProcessHostRegistersResolvableHost()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(static services =>
            {
                services.AddRuntimeProcessHost();
            })
            .Build();

        await host.StartAsync(cancellationToken);

        try
        {
            RuntimeProcessHost host1 = host.Services.GetRequiredService<RuntimeProcessHost>();
            RuntimeProcessHost host2 = host.Services.GetRequiredService<RuntimeProcessHost>();

            Assert.NotNull(host1);
            Assert.Same(host1, host2);

            IRuntimeProcessHost abstraction1 = host.Services.GetRequiredService<IRuntimeProcessHost>();
            Assert.Same(host1, abstraction1);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public static async Task AddRuntimeHealthMonitorRegistersMonitorAsHostedService()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.DirectoryPath, "z2p.db");

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddRuntimeKernelStateStore(databasePath);
                services.AddRuntimeProcessHost();
                services.AddRuntimeHealthMonitor();
            })
            .Build();

        await host.StartAsync(cancellationToken);

        try
        {
            IRuntimeHealthMonitor interfaceResolution = host.Services.GetRequiredService<IRuntimeHealthMonitor>();
            RuntimeHealthMonitor concreteResolution = host.Services.GetRequiredService<RuntimeHealthMonitor>();

            Assert.NotNull(interfaceResolution);
            Assert.Same(interfaceResolution, concreteResolution);

            // The monitor is the LAST hosted service the host
            // starts, so the last IHostedService resolved by the
            // generic IEnumerable<IHostedService> service must
            // resolve to the same instance.
            System.Collections.Generic.IEnumerable<IHostedService> hostedServices =
                host.Services.GetServices<IHostedService>();
            IHostedService lastHosted = Assert.Single(hostedServices, s => s is RuntimeHealthMonitor);
            Assert.Same(concreteResolution, lastHosted);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public static void AddCrashLoopGuardRegistersGuardAsSingletonAndNotAsHostedService()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddCrashLoopGuard()
            .BuildServiceProvider();

        ICrashLoopGuard first = provider.GetRequiredService<ICrashLoopGuard>();
        ICrashLoopGuard second = provider.GetRequiredService<ICrashLoopGuard>();

        Assert.NotNull(first);
        Assert.IsType<CrashLoopGuard>(first);
        // Singleton lifetime: two resolutions must return the same instance.
        Assert.Same(first, second);

        // The guard is a passive in-memory primitive and must NOT be
        // registered as an IHostedService — it owns no background
        // timer and has no resources to dispose. A caller asking for
        // the generic IHostedService service must get a null result.
        Assert.Null(provider.GetService<IHostedService>());
    }

    [Fact]
    public static async Task AddRuntimeSupervisorRegistersSupervisorAsHostedService()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.DirectoryPath, "z2p.db");

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddRuntimeKernelStateStore(databasePath);
                services.AddRuntimeProcessHost();
                services.AddRuntimeHealthMonitor();
                services.AddCrashLoopGuard();
                services.AddRuntimeKernelLoop();
                services.AddRuntimeSupervisor();
            })
            .Build();

        await host.StartAsync(cancellationToken);

        try
        {
            IRuntimeSupervisor interfaceResolution = host.Services.GetRequiredService<IRuntimeSupervisor>();
            RuntimeSupervisor concreteResolution = host.Services.GetRequiredService<RuntimeSupervisor>();

            Assert.Same(interfaceResolution, concreteResolution);

            System.Collections.Generic.IEnumerable<IHostedService> hostedServices =
                host.Services.GetServices<IHostedService>();
            IHostedService supervisorHosted = Assert.Single(hostedServices, s => s is RuntimeSupervisor);
            Assert.Same(concreteResolution, supervisorHosted);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public static async Task AddRuntimeKernelLoopRegistersLoopAndExecutor()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // The kernel loop and the process-host it depends on both
        // resolve ILogger<T> from the container, so the test must
        // use Host.CreateDefaultBuilder() rather than a bare
        // ServiceCollection(). The loop and the process host are
        // not registered as IHostedService themselves — the loop
        // is driven by the supervisor, which IS a hosted service,
        // so the full IHost is still required to start the
        // supervisor and exercise StartAsync.
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(static services =>
            {
                services.AddRuntimeProcessHost();
                services.AddCrashLoopGuard();
                services.AddRuntimeKernelLoop();
            })
            .Build();

        await host.StartAsync(cancellationToken);

        try
        {
            RuntimeKernelLoop first = host.Services.GetRequiredService<RuntimeKernelLoop>();
            RuntimeKernelLoop second = host.Services.GetRequiredService<RuntimeKernelLoop>();

            Assert.NotNull(first);
            // Singleton lifetime: two resolutions must return the same instance.
            Assert.Same(first, second);

            // The runner is exposed only as the internal
            // IRuntimeEffectRunner contract; the production
            // binding resolves it to RuntimeProcessEffectRunner
            // so the kernel loop stays free of any direct host
            // reference. The test project is whitelisted via
            // InternalsVisibleTo("Zapret2Pilot.Runtime.Tests"),
            // so the internal type is accessible here.
            IRuntimeEffectRunner runner = host.Services.GetRequiredService<IRuntimeEffectRunner>();
            Assert.NotNull(runner);
            Assert.IsType<RuntimeProcessEffectRunner>(runner);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }
}
