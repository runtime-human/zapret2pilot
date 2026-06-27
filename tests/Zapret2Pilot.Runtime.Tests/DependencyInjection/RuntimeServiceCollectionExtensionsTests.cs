using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Runtime;
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
}
