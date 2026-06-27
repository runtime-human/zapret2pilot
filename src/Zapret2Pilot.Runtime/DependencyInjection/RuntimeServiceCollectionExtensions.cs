using System;
using Microsoft.Extensions.DependencyInjection;
using Zapret2Pilot.Runtime.State;
using Zapret2Pilot.Storage.Sqlite;

namespace Microsoft.Extensions.DependencyInjection;

public static class RuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimeKernelStateStore(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddSingleton(new SqliteStorageOptions(databasePath));
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqliteDbInitializer>();
        services.AddSingleton<IRuntimeKernelStateStore>(static sp =>
        {
            sp.GetRequiredService<SqliteDbInitializer>().Initialize();
            return new RuntimeKernelStateStore(sp.GetRequiredService<SqliteConnectionFactory>());
        });

        return services;
    }
}
