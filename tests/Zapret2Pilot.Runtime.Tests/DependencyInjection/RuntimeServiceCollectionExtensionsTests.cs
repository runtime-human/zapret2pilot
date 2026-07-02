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
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.State;

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
                services.AddRuntimeKernelWorker();
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

            RuntimeKernelWorker worker = host.Services.GetRequiredService<RuntimeKernelWorker>();
            IHostedService hostedService = host.Services.GetRequiredService<IHostedService>();

            Assert.Same(worker, hostedService);
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
                services.AddRuntimeKernelWorker();
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
}
