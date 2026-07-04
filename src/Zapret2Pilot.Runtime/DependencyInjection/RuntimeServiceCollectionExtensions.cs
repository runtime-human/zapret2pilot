using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zapret2Pilot.Infrastructure.FileSystem;
using Zapret2Pilot.Runtime.Guard;
using Zapret2Pilot.Runtime.Health;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Locking;
using Zapret2Pilot.Runtime.Ownership;
using Zapret2Pilot.Runtime.Recovery;
using Zapret2Pilot.Runtime.State;
using Zapret2Pilot.Runtime.Supervisor;
using Zapret2Pilot.Runtime.Transactions;
using Zapret2Pilot.Runtime.Windows;
using Zapret2Pilot.Runtime.Workspace;
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

    /// <summary>
    /// Registers <see cref="RuntimeProcessHost"/> and every constructor
    /// dependency it needs as singletons in the supplied
    /// <see cref="IServiceCollection"/>. The runtime directory is
    /// resolved once via <see cref="AppDataPathProvider.GetDefaultLayout"/>
    /// and shared by the lock file store and the workspace materializer;
    /// both factory lambdas resolve the registered
    /// <see cref="AppDataLayout"/> rather than capturing the directory
    /// string in a closure, so the extension stays free of "static
    /// lambda captures local" diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration is intentionally pure: this method does NOT call
    /// <see cref="AppDataLayout.EnsureCreated"/>. Directory creation
    /// remains a kernel / host responsibility so DI resolution never
    /// touches the filesystem.
    /// </para>
    /// <para>
    /// <see cref="RuntimeKernelWorker"/> is NOT registered here. The
    /// worker is registered by <see cref="AddRuntimeKernelWorker"/>;
    /// callers MUST register the worker before (or independently of)
    /// this extension so the host resolves the same worker instance
    /// that the process host depends on.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeProcessHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The runtime directory is the only piece of configuration
        // shared by RuntimeLockFileStore and RuntimeWorkspaceMaterializer.
        // Register the layout once as a singleton; every downstream
        // factory resolves it from the service provider so the
        // lambdas stay free of captured locals.
        services.TryAddSingleton<AppDataLayout>(_ => AppDataPathProvider.GetDefaultLayout());

        services.AddSingleton<RuntimeOwnershipMutex>(
            static _ => new RuntimeOwnershipMutex(RuntimeOwnershipNames.GlobalMutexName));

        services.AddSingleton<RuntimeLockFileStore>(
            static sp => new RuntimeLockFileStore(
                sp.GetRequiredService<AppDataLayout>().RuntimeDirectory));

        services.AddSingleton<RuntimeStaleLockRecovery>(
            static sp => new RuntimeStaleLockRecovery(
                sp.GetRequiredService<RuntimeLockFileStore>()));

        services.AddSingleton<IRuntimeWorkspaceMaterializer>(
            static sp => RuntimeWorkspaceMaterializer.CreateForRoot(
                sp.GetRequiredService<AppDataLayout>().RuntimeDirectory));

        services.AddSingleton<IRuntimeTransactionManager, RuntimeTransactionManager>();
        services.AddSingleton<IRuntimeJobObjectProcessAssigner, RuntimeJobObjectProcessAssigner>();

        services.AddSingleton<RuntimeProcessHost>(
            static sp => new RuntimeProcessHost(
                sp.GetRequiredService<RuntimeOwnershipMutex>(),
                sp.GetRequiredService<RuntimeStaleLockRecovery>(),
                sp.GetRequiredService<IRuntimeWorkspaceMaterializer>(),
                sp.GetRequiredService<IRuntimeTransactionManager>(),
                sp.GetRequiredService<IRuntimeJobObjectProcessAssigner>(),
                sp.GetRequiredService<RuntimeLockFileStore>(),
                sp.GetRequiredService<ILogger<RuntimeProcessHost>>(),
                sp.GetRequiredService<RuntimeKernelWorker>()));

        // The supervisor depends on IRuntimeProcessHost, not on
        // the concrete RuntimeProcessHost, so expose the same
        // singleton under the abstraction as well.
        services.AddSingleton<IRuntimeProcessHost>(
            static sp => sp.GetRequiredService<RuntimeProcessHost>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="RuntimeHealthMonitor"/> as a singleton
    /// in the supplied <see cref="IServiceCollection"/>, both as
    /// its concrete type, as <see cref="IRuntimeHealthMonitor"/>,
    /// and as an <see cref="IHostedService"/>. All three
    /// registrations resolve to the same singleton instance so the
    /// Generic Host starts and stops the very same object the
    /// Application layer can inject through
    /// <see cref="IRuntimeHealthMonitor"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This extension depends on the singletons registered by
    /// <see cref="AddRuntimeKernelStateStore"/>,
    /// <see cref="AddRuntimeKernelWorker"/> and
    /// <see cref="AddRuntimeProcessHost"/>. Callers MUST register
    /// those extensions first; the method does not register the
    /// state store, the worker or the process host.
    /// </para>
    /// <para>
    /// Registration is pure: this method does not touch the
    /// filesystem, does not start the monitor and does not depend
    /// on any other extension beyond the ones above.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeHealthMonitor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register a default TimeProvider so the Generic Host's
        // container can construct RuntimeHealthMonitor without the
        // caller having to add the registration themselves. Tests
        // that need a deterministic clock resolve the monitor
        // directly with a custom TimeProvider and do not go
        // through this extension.
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        services.AddSingleton<RuntimeHealthMonitor>();
        services.AddSingleton<IRuntimeHealthMonitor>(
            static sp => sp.GetRequiredService<RuntimeHealthMonitor>());
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<RuntimeHealthMonitor>());

        return services;
    }

    /// <summary>
    /// Registers the in-memory <see cref="CrashLoopGuard"/> as a
    /// singleton in the supplied <see cref="IServiceCollection"/>,
    /// both as its concrete type and as
    /// <see cref="ICrashLoopGuard"/>. The guard uses
    /// <see cref="CrashLoopGuardOptions"/> with the documented
    /// defaults (2s base backoff, 5m cap, 60s stability window,
    /// 10-failure lockout threshold).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration is pure: the guard is a pure in-memory
    /// primitive, the extension does not touch the filesystem and
    /// does not depend on any other extension. The default clock
    /// (<see cref="DateTimeOffset.UtcNow"/>) is used; tests can
    /// construct a guard directly with an injectable clock to
    /// drive time deterministically.
    /// </para>
    /// <para>
    /// This extension does NOT register the
    /// <see cref="ICrashLoopGuard"/> as an
    /// <see cref="IHostedService"/>: the guard is a passive
    /// primitive that supervisors query on demand, it owns no
    /// background timer and has no resources to dispose.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddCrashLoopGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new CrashLoopGuardOptions());
        services.AddSingleton<ICrashLoopGuard>(
            static sp => new CrashLoopGuard(
                sp.GetRequiredService<CrashLoopGuardOptions>(),
                clock: null));

        return services;
    }

    /// <summary>
    /// Registers <see cref="RuntimeSupervisor"/> as a singleton in
    /// the supplied <see cref="IServiceCollection"/>, both as its
    /// concrete type, as <see cref="IRuntimeSupervisor"/> and as an
    /// <see cref="IHostedService"/>. All three registrations resolve
    /// to the same instance so the Generic Host starts and stops
    /// the very same object the Application layer can inject
    /// through <see cref="IRuntimeSupervisor"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This extension depends on the singletons registered by
    /// <see cref="AddRuntimeProcessHost"/>,
    /// <see cref="AddRuntimeHealthMonitor"/> and
    /// <see cref="AddCrashLoopGuard"/>. Callers MUST register those
    /// extensions first; the method does not register the process
    /// host, the health monitor or the guard.
    /// </para>
    /// <para>
    /// Registration is pure: the supervisor is constructed lazily
    /// and the extension does not touch the filesystem, does not
    /// start the supervisor and does not depend on any other
    /// extension beyond the ones above.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeSupervisor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RuntimeSupervisor>();
        services.AddSingleton<IRuntimeSupervisor>(
            static sp => sp.GetRequiredService<RuntimeSupervisor>());
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<RuntimeSupervisor>());

        return services;
    }
}
