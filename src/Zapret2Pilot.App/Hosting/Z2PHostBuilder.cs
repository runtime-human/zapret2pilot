using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zapret2Pilot.App.DependencyInjection;
using Zapret2Pilot.Storage.Sqlite;

namespace Zapret2Pilot.App.Hosting;

internal static class Z2PHostBuilder
{
    public static IHost Build(string[] args)
    {
        return CreateBuilder(args).Build();
    }

    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Explicit, approved configuration sources only.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        // Security-critical values are set in code and cannot be overridden
        // by environment variables, ordinary appsettings, or arbitrary CLI.
        builder.Services.Configure<Z2PApplicationOptions>(options =>
        {
            options.StorageDatabasePath = Z2PConfigurationDefaults.DefaultStorageDatabasePath();
            options.TrustedTufRoot = Z2PConfigurationDefaults.TrustedTufRoot;
            options.DeploymentFlavor = DeploymentFlavor.Development;
            options.DeveloperMode = false;
        });

        builder.Services.AddSingleton<IValidateOptions<Z2PApplicationOptions>, Z2PApplicationOptionsValidator>();
        builder.Services.AddOptions<Z2PApplicationOptions>().ValidateOnStart();

        Z2PApplicationOptions options = new()
        {
            StorageDatabasePath = Z2PConfigurationDefaults.DefaultStorageDatabasePath()
        };

        // v7-D / #17: the unelevated Control Plane owns general application
        // storage, but it must not register writable Runtime Kernel authority.
        // Runtime lifecycle state is broker-owned across the privilege boundary.
        builder.Services.AddSingleton(new SqliteStorageOptions(options.StorageDatabasePath));
        builder.Services.AddSingleton<SqliteConnectionFactory>();
        builder.Services.AddSingleton<SqliteDbInitializer>();

        builder.Services.AddZ2PAppServices();

        return builder;
    }
}
