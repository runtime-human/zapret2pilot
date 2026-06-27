using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Runtime.Hosting;
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

    /// <summary>
    /// Registers <see cref="RuntimeKernelWorker"/> as a singleton
    /// in the supplied <see cref="IServiceCollection"/>, both as
    /// its concrete type and as an <see cref="IHostedService"/>
    /// (which the Microsoft.Extensions.Hosting infrastructure will
    /// start and stop alongside the rest of the host). The two
    /// registrations resolve to the same instance.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeKernelWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RuntimeKernelWorker>();
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<RuntimeKernelWorker>());

        return services;
    }
}
